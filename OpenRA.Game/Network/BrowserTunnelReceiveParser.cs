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

using System.Collections.Generic;
using OpenRA.Server;

namespace OpenRA.Network
{
	/// <summary>
	/// Incremental parsing of the dedicated-server byte stream fed by WebSocket frames (same layout as <see cref="NetworkConnection"/> receive).
	/// </summary>
	public static class BrowserTunnelReceiveParser
	{
		const int MaxPacketLength = 64 * 1024 * 1024;

		public static int ReadInt32LittleEndian(IReadOnlyList<byte> buffer, int offset) =>
			buffer[offset]
			| (buffer[offset + 1] << 8)
			| (buffer[offset + 2] << 16)
			| (buffer[offset + 3] << 24);

		/// <summary>When buffer has fewer than 8 bytes, returns false and <paramref name="errorMessage"/> is null.</summary>
		public static bool TryConsumeHandshake(List<byte> buffer, out int clientId, out string? errorMessage)
		{
			clientId = 0;
			errorMessage = null;
			if (buffer.Count < 8)
				return false;

			var handshakeProtocol = ReadInt32LittleEndian(buffer, 0);
			clientId = ReadInt32LittleEndian(buffer, 4);

			if (handshakeProtocol != ProtocolVersion.Handshake)
			{
				errorMessage = $"Handshake protocol version mismatch. Server={handshakeProtocol} Client={ProtocolVersion.Handshake}";
				buffer.RemoveRange(0, 8);
				return false;
			}

			buffer.RemoveRange(0, 8);
			return true;
		}

		/// <summary>
		/// Consumes one framed packet (length, client id, payload). Returns false with null error if more bytes are needed;
		/// false with non-null error on protocol violation.
		/// </summary>
		public static bool TryConsumePacket(List<byte> buffer, out int fromClient, out byte[] payload, out string? errorMessage)
		{
			fromClient = 0;
			payload = null;
			errorMessage = null;

			if (buffer.Count < 8)
				return false;

			var len = ReadInt32LittleEndian(buffer, 0);
			fromClient = ReadInt32LittleEndian(buffer, 4);

			if (len < 0)
			{
				errorMessage = $"Negative packet length {len}";
				return false;
			}

			if (len > MaxPacketLength)
			{
				errorMessage = $"Packet length {len} exceeds maximum {MaxPacketLength}";
				return false;
			}

			if (len == 0)
			{
				errorMessage = "Zero-length packet is not supported";
				return false;
			}

			var total = 8 + len;
			if (buffer.Count < total)
				return false;

			payload = new byte[len];
			for (var i = 0; i < len; i++)
				payload[i] = buffer[8 + i];

			buffer.RemoveRange(0, total);
			return true;
		}
	}
}
