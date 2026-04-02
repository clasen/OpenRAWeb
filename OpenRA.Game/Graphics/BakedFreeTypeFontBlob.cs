#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using OpenRA.Primitives;

namespace OpenRA.Graphics
{
	/// <summary>
	/// On-disk / wire format for glyphs rasterized with desktop FreeType, consumed by the browser host
	/// to match desktop metrics and bitmaps without native FreeType on WASM.
	/// </summary>
	public static class BakedFreeTypeFontBlob
	{
		public const int MagicLength = 4;
		public const uint Version = 1;
		public const int Sha256Length = 32;
		public static readonly byte[] MagicBytes = { (byte)'O', (byte)'R', (byte)'B', (byte)'F' };

		public sealed class ParsedBlob
		{
			public readonly byte[] TtfSha256;
			public readonly Dictionary<(int LogicalSize, uint ScaledPixels), Dictionary<uint, GlyphEntry>> Tables;

			public ParsedBlob(byte[] ttfSha256, Dictionary<(int LogicalSize, uint ScaledPixels), Dictionary<uint, GlyphEntry>> tables)
			{
				TtfSha256 = ttfSha256;
				Tables = tables;
			}
		}

		public sealed class GlyphEntry
		{
			public float Advance;
			public int2 Offset;
			public Size Size;
			public byte[] Data;
		}

		public static bool TryParse(ReadOnlySpan<byte> blob, out ParsedBlob parsed, out string error)
		{
			parsed = null;
			error = null;
			if (blob.Length < MagicLength + 4 + Sha256Length + 4)
			{
				error = "Blob too small.";
				return false;
			}

			if (!blob[..MagicLength].SequenceEqual(MagicBytes))
			{
				error = "Invalid magic.";
				return false;
			}

			var ver = BitConverter.ToUInt32(blob.Slice(MagicLength, 4));
			if (ver != Version)
			{
				error = $"Unsupported version {ver}.";
				return false;
			}

			var off = MagicLength + 4;
			var ttfSha = blob.Slice(off, Sha256Length).ToArray();
			off += Sha256Length;
			if (off + 4 > blob.Length)
			{
				error = "Truncated header.";
				return false;
			}

			var tableCount = (int)BitConverter.ToUInt32(blob.Slice(off, 4));
			off += 4;
			var tables = new Dictionary<(int LogicalSize, uint ScaledPixels), Dictionary<uint, GlyphEntry>>();

			for (var t = 0; t < tableCount; t++)
			{
				const int TableHeader = 4 + 4 + 4 + 4; // logical, deviceScale, scaledPixels, glyphCount
				if (off + TableHeader > blob.Length)
				{
					error = "Truncated table header.";
					return false;
				}

				var logicalSize = BitConverter.ToInt32(blob.Slice(off, 4));
				off += 4;
				var deviceScale = BitConverter.ToSingle(blob.Slice(off, 4));
				off += 4;
				var scaledPixels = BitConverter.ToUInt32(blob.Slice(off, 4));
				off += 4;
				var glyphCount = (int)BitConverter.ToUInt32(blob.Slice(off, 4));
				off += 4;

				if (logicalSize <= 0 || glyphCount < 0)
				{
					error = "Invalid table dimensions.";
					return false;
				}

				var expectedScaled = (uint)(logicalSize * deviceScale);
				if (expectedScaled != scaledPixels)
				{
					error = "scaledPixels does not match logicalSize * deviceScale.";
					return false;
				}

				var dict = new Dictionary<uint, GlyphEntry>();
				var key = (LogicalSize: logicalSize, ScaledPixels: scaledPixels);
				for (var g = 0; g < glyphCount; g++)
				{
					const int GlyphHeader = 4 + 4 + 2 + 2 + 2 + 2; // cp, advance, ox, oy, w, h
					if (off + GlyphHeader > blob.Length)
					{
						error = "Truncated glyph header.";
						return false;
					}

					var cp = BitConverter.ToUInt32(blob.Slice(off, 4));
					off += 4;
					var advance = BitConverter.ToSingle(blob.Slice(off, 4));
					off += 4;
					var ox = BitConverter.ToInt16(blob.Slice(off, 2));
					off += 2;
					var oy = BitConverter.ToInt16(blob.Slice(off, 2));
					off += 2;
					var w = BitConverter.ToUInt16(blob.Slice(off, 2));
					off += 2;
					var h = BitConverter.ToUInt16(blob.Slice(off, 2));
					off += 2;

					var count = w * h;
					if (count < 0 || off + count > blob.Length)
					{
						error = "Truncated glyph bitmap.";
						return false;
					}

					byte[] data = null;
					if (count > 0)
					{
						data = blob.Slice(off, count).ToArray();
						off += count;
					}

					dict[cp] = new GlyphEntry
					{
						Advance = advance,
						Offset = new int2(ox, oy),
						Size = new Size(w, h),
						Data = data
					};
				}

				tables[key] = dict;
			}

			if (off != blob.Length)
			{
				error = "Trailing bytes in blob.";
				return false;
			}

			parsed = new ParsedBlob(ttfSha, tables);
			return true;
		}

		public static void Write(Stream stream, byte[] ttfBytes, List<TableBuilder> tables)
		{
			using var ms = new MemoryStream();
			using var w = new BinaryWriter(ms);
			w.Write(MagicBytes);
			w.Write(Version);
			w.Write(SHA256.HashData(ttfBytes));
			w.Write(tables.Count);
			foreach (var tb in tables)
			{
				w.Write(tb.LogicalSize);
				w.Write(tb.DeviceScale);
				var scaled = (uint)(tb.LogicalSize * tb.DeviceScale);
				w.Write(scaled);
				w.Write(tb.Glyphs.Count);
				foreach (var kv in tb.Glyphs)
				{
					var cp = kv.Key;
					var g = kv.Value;
					w.Write(cp);
					w.Write(g.Advance);
					w.Write((short)g.Offset.X);
					w.Write((short)g.Offset.Y);
					w.Write((ushort)g.Size.Width);
					w.Write((ushort)g.Size.Height);
					if (g.Data != null && g.Data.Length > 0)
						w.Write(g.Data);
				}
			}

			w.Flush();
			ms.Position = 0;
			ms.CopyTo(stream);
		}

		public sealed class TableBuilder
		{
			public int LogicalSize;
			public float DeviceScale;
			public readonly SortedDictionary<uint, FontGlyph> Glyphs = new();
		}

		public static bool TtfMatchesBlob(byte[] ttfBytes, ParsedBlob blob)
		{
			var h = SHA256.HashData(ttfBytes);
			return h.AsSpan().SequenceEqual(blob.TtfSha256);
		}
	}
}
