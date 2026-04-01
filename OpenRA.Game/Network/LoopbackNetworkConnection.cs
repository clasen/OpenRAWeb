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

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using OpenRA;
using OpenRA.Server;

namespace OpenRA.Network
{
	public sealed class LoopbackNetworkConnection : IConnection
	{
		public readonly ConnectionTarget Target;
		internal ReplayRecorder Recorder { get; private set; }

		readonly LoopbackChannel channel;
		readonly Queue<(int Frame, int SyncHash, ulong DefeatState)> sentSync = new();
		readonly Queue<(int Frame, int SyncHash, ulong DefeatState)> queuedSyncPackets = new();
		readonly Queue<(int Frame, OrderPacket Orders)> sentOrders = new();
		readonly Queue<OrderPacket> sentImmediateOrders = new();
		readonly ConcurrentQueue<(int FromClient, byte[] Data)> receivedPackets = new();
		readonly List<byte> receiveBuffer = new();
		bool handshakeReceived;

		volatile ConnectionState connectionState = ConnectionState.Connecting;
		volatile int clientId;
		bool disposed;

		public LoopbackNetworkConnection(LoopbackChannel channel)
		{
			this.channel = channel;
			Target = new ConnectionTarget("127.0.0.1", 0);
			EndPoint = new IPEndPoint(IPAddress.Loopback, 0);
		}

		public ConnectionState ConnectionState => connectionState;

		public IPEndPoint EndPoint { get; }

		public string ErrorMessage { get; private set; }

		void PumpIncoming()
		{
			if (disposed || connectionState == ConnectionState.NotConnected)
				return;

			while (channel.TryTakeServerForClient(out var chunk, 0))
				receiveBuffer.AddRange(chunk);

			if (!handshakeReceived)
			{
				if (receiveBuffer.Count < 8)
				{
					if (channel.ServerToClientCompleted)
					{
						ErrorMessage = "Connection failed";
						connectionState = ConnectionState.NotConnected;
					}

					return;
				}

				try
				{
					var handshakeProtocol = BitConverter.ToInt32(receiveBuffer.ToArray(), 0);
					if (handshakeProtocol != ProtocolVersion.Handshake)
						throw new InvalidOperationException($"Handshake protocol version mismatch. Server={handshakeProtocol} Client={ProtocolVersion.Handshake}");

					clientId = BitConverter.ToInt32(receiveBuffer.ToArray(), 4);
					receiveBuffer.RemoveRange(0, 8);
					handshakeReceived = true;
					connectionState = ConnectionState.Connected;
				}
				catch (Exception ex)
				{
					ErrorMessage = "Connection failed";
					Log.Write("client", $"Loopback connection failed: {ex.Message}");
					connectionState = ConnectionState.NotConnected;
					return;
				}
			}

			while (receiveBuffer.Count >= 8)
			{
				var len = BitConverter.ToInt32(receiveBuffer.ToArray(), 0);
				var fromClient = BitConverter.ToInt32(receiveBuffer.ToArray(), 4);
				if (len < 0)
				{
					ErrorMessage = "Connection failed";
					Log.Write("client", $"Loopback connection failed: Negative packet length {len}");
					connectionState = ConnectionState.NotConnected;
					return;
				}

				var total = 8 + len;
				if (receiveBuffer.Count < total)
					break;

				var buf = new byte[len];
				Buffer.BlockCopy(receiveBuffer.ToArray(), 8, buf, 0, len);
				receiveBuffer.RemoveRange(0, total);

				if (len == 0)
				{
					ErrorMessage = "Connection failed";
					connectionState = ConnectionState.NotConnected;
					return;
				}

				receivedPackets.Enqueue((fromClient, buf));
			}

			if (channel.ServerToClientCompleted)
				connectionState = ConnectionState.NotConnected;
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
				channel.SendFromClient(ms.ToArray());
			}
			catch (Exception)
			{
			}
		}

		void IConnection.Receive(OrderManager orderManager)
		{
			PumpIncoming();

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
			channel.Dispose();
			Recorder?.Dispose();
		}
	}
}
