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

namespace OpenRA.Network
{
	/// <summary>
	/// In-memory replacement for a TCP stream between OpenRA.Server.Connection and
	/// LoopbackNetworkConnection (WebAssembly / browser builds).
	/// </summary>
	public sealed class LoopbackChannel : IDisposable
	{
		readonly BlockingCollection<byte[]> clientToServer = new();
		readonly BlockingCollection<byte[]> serverToClient = new();
		volatile bool disposed;

		public bool ClientToServerCompleted => clientToServer.IsAddingCompleted;

		public bool ServerToClientCompleted => serverToClient.IsAddingCompleted;

		public void SendFromClient(byte[] data)
		{
			if (data == null || data.Length == 0 || disposed || clientToServer.IsAddingCompleted)
				return;

			try
			{
				clientToServer.Add(data);
			}
			catch (InvalidOperationException)
			{
			}
		}

		public bool TryTakeClientForServer(out byte[] data, int millisecondsTimeout)
		{
			return clientToServer.TryTake(out data, millisecondsTimeout);
		}

		public void SendFromServer(byte[] data)
		{
			if (data == null || data.Length == 0 || disposed || serverToClient.IsAddingCompleted)
				return;

			try
			{
				serverToClient.Add(data);
			}
			catch (InvalidOperationException)
			{
			}
		}

		public bool TryTakeServerForClient(out byte[] data, int millisecondsTimeout)
		{
			return serverToClient.TryTake(out data, millisecondsTimeout);
		}

		public void Dispose()
		{
			if (disposed)
				return;

			disposed = true;
			clientToServer.CompleteAdding();
			serverToClient.CompleteAdding();
		}
	}
}
