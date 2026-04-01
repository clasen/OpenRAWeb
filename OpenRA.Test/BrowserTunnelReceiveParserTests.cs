using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Network;
using OpenRA.Server;

namespace OpenRA.Test
{
	[TestFixture]
	public class BrowserTunnelReceiveParserTests
	{
		static void WriteInt32Le(List<byte> buf, int value)
		{
			buf.Add((byte)value);
			buf.Add((byte)(value >> 8));
			buf.Add((byte)(value >> 16));
			buf.Add((byte)(value >> 24));
		}

		[Test]
		public void HandshakeNeedMoreData()
		{
			var buf = new List<byte> { 1, 2, 3 };
			Assert.False(BrowserTunnelReceiveParser.TryConsumeHandshake(buf, out var cid, out var err));
			Assert.AreEqual(0, cid);
			Assert.IsNull(err);
			Assert.AreEqual(3, buf.Count);
		}

		[Test]
		public void HandshakeSuccess()
		{
			var buf = new List<byte>();
			WriteInt32Le(buf, ProtocolVersion.Handshake);
			WriteInt32Le(buf, 42);
			Assert.True(BrowserTunnelReceiveParser.TryConsumeHandshake(buf, out var cid, out var err));
			Assert.AreEqual(42, cid);
			Assert.IsNull(err);
			Assert.AreEqual(0, buf.Count);
		}

		[Test]
		public void HandshakeWrongProtocol()
		{
			var buf = new List<byte>();
			WriteInt32Le(buf, 999);
			WriteInt32Le(buf, 1);
			Assert.False(BrowserTunnelReceiveParser.TryConsumeHandshake(buf, out _, out var err));
			Assert.IsNotNull(err);
			Assert.AreEqual(0, buf.Count);
		}

		[Test]
		public void PacketSplitAcrossAppends()
		{
			var buf = new List<byte>();
			WriteInt32Le(buf, 4);
			WriteInt32Le(buf, 7);
			buf.Add(1);
			buf.Add(2);

			Assert.False(BrowserTunnelReceiveParser.TryConsumePacket(buf, out _, out _, out var err1));
			Assert.IsNull(err1);

			buf.Add(3);
			buf.Add(4);

			Assert.True(BrowserTunnelReceiveParser.TryConsumePacket(buf, out var from, out var payload, out var err2));
			Assert.IsNull(err2);
			Assert.AreEqual(7, from);
			Assert.AreEqual(new byte[] { 1, 2, 3, 4 }, payload);
			Assert.AreEqual(0, buf.Count);
		}

		[Test]
		public void HandshakeThenPacketInOneBuffer()
		{
			var buf = new List<byte>();
			WriteInt32Le(buf, ProtocolVersion.Handshake);
			WriteInt32Le(buf, 3);
			WriteInt32Le(buf, 2);
			WriteInt32Le(buf, 9);
			buf.Add(0xAB);
			buf.Add(0xCD);

			Assert.True(BrowserTunnelReceiveParser.TryConsumeHandshake(buf, out var cid, out _));
			Assert.AreEqual(3, cid);
			Assert.True(BrowserTunnelReceiveParser.TryConsumePacket(buf, out var from, out var payload, out _));
			Assert.AreEqual(9, from);
			Assert.AreEqual(new byte[] { 0xAB, 0xCD }, payload);
			Assert.AreEqual(0, buf.Count);
		}
	}
}
