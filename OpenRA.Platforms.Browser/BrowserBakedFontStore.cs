#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using OpenRA.Graphics;

namespace OpenRA.Platforms.Browser
{
	/// <summary>
	/// Preloaded desktop FreeType glyph blobs (see <see cref="BakedTrueTypeFont"/>), keyed by SHA-256 of the TTF bytes.
	/// </summary>
	public static class BrowserBakedFontStore
	{
		static readonly Dictionary<string, byte[]> HexSha256ToBlob = new();

		public static void Clear() => HexSha256ToBlob.Clear();

		public static void RegisterOrbf(byte[] blob)
		{
			if (blob == null || blob.Length == 0)
				return;

			if (!BakedFreeTypeFontBlob.TryParse(blob, out var parsed, out var err))
			{
				Console.WriteLine("[browser] Skipping invalid baked font blob: " + err);
				return;
			}

			var hex = Convert.ToHexString(parsed.TtfSha256).ToLowerInvariant();
			HexSha256ToBlob[hex] = blob;
		}

		public static bool TryGetBlob(byte[] ttfBytes, out byte[] blob)
		{
			blob = null;
			if (ttfBytes == null || ttfBytes.Length == 0)
				return false;

			var hex = Convert.ToHexString(SHA256.HashData(ttfBytes)).ToLowerInvariant();
			return HexSha256ToBlob.TryGetValue(hex, out blob);
		}
	}
}
