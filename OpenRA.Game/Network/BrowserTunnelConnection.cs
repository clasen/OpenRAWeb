#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

#if OPENRA_BROWSER
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenRA.Server;

namespace OpenRA.Network
{
	/// <summary>WebSocket (ws/wss) URL of the tunnel edge; one logical byte stream to the dedicated server.</summary>
	public readonly struct BrowserTunnelEndpoint
	{
		public readonly Uri WebSocketUri;
		/// <summary>Optional proxy <c>EDGE_TOKEN</c> sent as <c>X-OpenRA-Tunnel-Token</c> during the WebSocket handshake.</summary>
		public readonly string? EdgeToken;

		public BrowserTunnelEndpoint(string uri, string? edgeToken = null)
			: this(new Uri(uri, UriKind.Absolute), edgeToken) { }

		public BrowserTunnelEndpoint(Uri uri, string? edgeToken = null)
		{
			ArgumentNullException.ThrowIfNull(uri);
			if (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != "wss")
				throw new ArgumentException("Tunnel URI must use ws:// or wss://.", nameof(uri));

			WebSocketUri = uri;
			EdgeToken = string.IsNullOrWhiteSpace(edgeToken) ? null : edgeToken.Trim();
		}

		/// <summary>Host:port label for connecting / failure UI (not the full path).</summary>
		public ConnectionTarget DisplayTarget()
		{
			var u = WebSocketUri;
			var port = u.Port > 0 ? u.Port : (u.Scheme == "wss" ? 443 : 80);
			return new ConnectionTarget(u.Host, port);
		}
	}

	public sealed class BrowserTunnelConnection : IConnection, INetworkConnectionStatus
	{
		public readonly BrowserTunnelEndpoint Endpoint;
		public ConnectionTarget Target { get; }
		public IPEndPoint RemoteEndPoint => null;
		public string BrowserTunnelReconnectUrl => Endpoint.WebSocketUri.ToString();

		public string? BrowserTunnelReconnectEdgeToken => Endpoint.EdgeToken;

		internal ReplayRecorder Recorder { get; private set; }

		readonly Queue<(int Frame, int SyncHash, ulong DefeatState)> sentSync = new();
		readonly Queue<(int Frame, int SyncHash, ulong DefeatState)> queuedSyncPackets = new();
		readonly Queue<(int Frame, OrderPacket Orders)> sentOrders = new();
		readonly Queue<OrderPacket> sentImmediateOrders = new();
		readonly ConcurrentQueue<(int FromClient, byte[] Data)> receivedPackets = new();
		readonly List<byte> receiveBuffer = new();
		readonly Channel<byte[]> sendChannel = Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions
		{
			SingleReader = true,
			SingleWriter = false
		});

		ClientWebSocket webSocket;
		readonly CancellationTokenSource connectionCts = new();
		Task networkTask;
		bool handshakeReceived;

		volatile ConnectionState connectionState = ConnectionState.Connecting;
		volatile int clientId;
		volatile int lastKnownOrderQueueLength;
		bool disposed;

		public string ErrorMessage { get; private set; }

		public BrowserTunnelConnection(BrowserTunnelEndpoint endpoint)
		{
			Endpoint = endpoint;
			Target = endpoint.DisplayTarget();
			webSocket = new ClientWebSocket();
			if (Endpoint.EdgeToken != null)
				webSocket.Options.SetRequestHeader("X-OpenRA-Tunnel-Token", Endpoint.EdgeToken);

			networkTask = Task.Run(RunNetworkAsync, CancellationToken.None);
		}

		public ConnectionState ConnectionState => connectionState;

		async Task RunNetworkAsync()
		{
			var ct = connectionCts.Token;
			try
			{
				await webSocket.ConnectAsync(Endpoint.WebSocketUri, ct).ConfigureAwait(false);
				using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
				var recvTask = ReceiveLoopAsync(linked.Token);
				var sendTask = SendLoopAsync(linked.Token);
				await Task.WhenAny(recvTask, sendTask).ConfigureAwait(false);
				linked.Cancel();
				try
				{
					await Task.WhenAll(recvTask, sendTask).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					// Expected after we cancel the linked token.
				}
			}
			catch (OperationCanceledException)
			{
				// Expected on dispose.
			}
			catch (Exception ex)
			{
				if (connectionState != ConnectionState.Connected)
					ErrorMessage ??= "WebSocket connection failed";
				else
					ErrorMessage ??= "Connection lost";

				Log.Write("client", $"Browser tunnel error: {ex.Message}");
			}
			finally
			{
				connectionState = ConnectionState.NotConnected;
				sendChannel.Writer.TryComplete();
			}
		}

		async Task ReceiveLoopAsync(CancellationToken ct)
		{
			var buffer = new byte[65536];
			while (!ct.IsCancellationRequested && webSocket.State == WebSocketState.Open)
			{
				using var ms = new MemoryStream();
				WebSocketReceiveResult result;
				do
				{
					result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
					if (result.MessageType == WebSocketMessageType.Close)
						return;

					if (result.MessageType == WebSocketMessageType.Text)
						throw new InvalidOperationException("Unexpected text WebSocket frame from tunnel.");

					ms.Write(buffer, 0, result.Count);
				}
				while (!result.EndOfMessage && !ct.IsCancellationRequested);

				var payload = ms.ToArray();
				if (payload.Length > 0)
				{
					receiveBuffer.AddRange(payload);
					PumpIncomingFramesFromReceiveLoop();
				}
			}
		}

		async Task SendLoopAsync(CancellationToken ct)
		{
			await foreach (var packet in sendChannel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
			{
				if (webSocket.State != WebSocketState.Open || ct.IsCancellationRequested)
					break;

				await webSocket.SendAsync(new ArraySegment<byte>(packet), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
			}
		}

		void PumpIncomingFramesFromReceiveLoop()
		{
			if (!handshakeReceived)
			{
				if (BrowserTunnelReceiveParser.TryConsumeHandshake(receiveBuffer, out var cid, out var handshakeErr))
				{
					clientId = cid;
					handshakeReceived = true;
					connectionState = ConnectionState.Connected;
				}
				else if (handshakeErr != null)
				{
					ErrorMessage = "Connection failed";
					Log.Write("client", $"Browser tunnel handshake failed: {handshakeErr}");
					connectionState = ConnectionState.NotConnected;
					connectionCts.Cancel();
				}

				return;
			}

			while (true)
			{
				if (!BrowserTunnelReceiveParser.TryConsumePacket(receiveBuffer, out var fromClient, out var buf, out var packetErr))
				{
					if (packetErr != null)
					{
						ErrorMessage = "Connection failed";
						Log.Write("client", $"Browser tunnel: {packetErr}");
						connectionState = ConnectionState.NotConnected;
						connectionCts.Cancel();
					}

					break;
				}

				if (OrderIO.TryParsePingRequest((fromClient, buf), out var timestamp))
					SendRawPacket(OrderIO.SerializePingResponse(timestamp, (byte)Math.Clamp(lastKnownOrderQueueLength, 0, byte.MaxValue)));
				else
					receivedPackets.Enqueue((fromClient, buf));
			}
		}

		int IConnection.LocalClientId => clientId;

		void IConnection.StartGame() { }

		void IConnection.Send(int frame, IEnumerable<Order> orders)
		{
			var o = new OrderPacket(orders);
			sentOrders.Enqueue((frame, o));
			SendPacket(o.Serialize(frame));
		}

		void IConnection.SendImmediate(IEnumerable<Order> orders)
		{
			var o = new OrderPacket(orders);
			sentImmediateOrders.Enqueue(o);
			SendPacket(o.Serialize(0));
		}

		void IConnection.SendSync(int frame, int syncHash, ulong defeatState)
		{
			queuedSyncPackets.Enqueue((frame, syncHash, defeatState));
		}

		void SendPacket(byte[] packet)
		{
			if (disposed)
				return;

			try
			{
				var ms = new MemoryStream();
				ms.Write(packet.Length);
				ms.Write(packet);

				foreach (var s in queuedSyncPackets)
				{
					var q = OrderIO.SerializeSync(s);
					ms.Write(q.Length);
					ms.Write(q);
					sentSync.Enqueue(s);
				}

				queuedSyncPackets.Clear();
				SendRawBytes(ms.ToArray());
			}
			catch (Exception)
			{
				// Matches NetworkConnection send: ignore; reader will surface disconnect.
			}
		}

		void SendRawPacket(byte[] packet)
		{
			if (disposed)
				return;

			try
			{
				var ms = new MemoryStream();
				ms.Write(packet.Length);
				ms.Write(packet);
				SendRawBytes(ms.ToArray());
			}
			catch (Exception)
			{
				// Matches NetworkConnection send: ignore; reader will surface disconnect.
			}
		}

		void SendRawBytes(byte[] payload)
		{
			if (!sendChannel.Writer.TryWrite(payload))
			{
				// Channel completed (disconnect).
			}
		}

		void IConnection.Receive(OrderManager orderManager)
		{
			lastKnownOrderQueueLength = orderManager.OrderQueueLength;

			while (sentImmediateOrders.TryDequeue(out var i))
			{
				orderManager.ReceiveImmediateOrders(clientId, i);
				Recorder?.Receive(clientId, i.Serialize(0));
				if (disposed)
					return;
			}

			while (sentSync.TryDequeue(out var s))
			{
				orderManager.ReceiveSync(s);
				Recorder?.Receive(clientId, OrderIO.SerializeSync(s));
			}

			while (receivedPackets.TryDequeue(out var p))
			{
				if (OrderIO.TryParseDisconnect(p, out var disconnect))
				{
					orderManager.ReceiveDisconnect(disconnect.ClientId, disconnect.Frame);
					Recorder?.Receive(p.FromClient, p.Data);
				}
				else if (OrderIO.TryParseSync(p.Data, out var sync))
				{
					orderManager.ReceiveSync(sync);
					Recorder?.Receive(p.FromClient, p.Data);
				}
				else if (OrderIO.TryParseTickScale(p, out var scale))
					orderManager.ReceiveTickScale(scale);
				else if (OrderIO.TryParsePingRequest(p, out var timestamp))
					SendPacket(OrderIO.SerializePingResponse(timestamp, (byte)orderManager.OrderQueueLength));
				else if (OrderIO.TryParseAck(p, out var ackFrame, out var ackCount))
				{
					if (ackCount > sentOrders.Count)
						throw new InvalidOperationException($"Received Ack for {ackCount} > {sentOrders.Count} frames.");

					OrderPacket packet;
					if (ackCount != 1)
					{
						var orders = Enumerable.Range(0, ackCount)
							.Select(i => sentOrders.Dequeue().Orders);
						packet = OrderPacket.Combine(orders);
					}
					else
						packet = sentOrders.Dequeue().Orders;

					orderManager.ReceiveOrders(clientId, (ackFrame, packet));
					Recorder?.Receive(clientId, packet.Serialize(ackFrame));
				}
				else if (OrderIO.TryParseOrderPacket(p.Data, out var orders))
				{
					if (orders.Frame == 0)
						orderManager.ReceiveImmediateOrders(p.FromClient, orders.Orders);
					else
						orderManager.ReceiveOrders(p.FromClient, orders);

					Recorder?.Receive(p.FromClient, p.Data);
				}
				else
					throw new InvalidDataException($"Received unknown packet from client {p.FromClient} with length {p.Data.Length}");

				if (disposed)
					return;
			}
		}

		public void StartRecording(Func<string> chooseFilename)
		{
			Recorder?.Dispose();
			Recorder = new ReplayRecorder(chooseFilename);
		}

		void IDisposable.Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			connectionCts.Cancel();
			sendChannel.Writer.TryComplete();
			try
			{
				webSocket?.Abort();
			}
			catch
			{
				// Ignore.
			}

			webSocket?.Dispose();
			webSocket = null;
			Recorder?.Dispose();
		}
	}
}
#endif
