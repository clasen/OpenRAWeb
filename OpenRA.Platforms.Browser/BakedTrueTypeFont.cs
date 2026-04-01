#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Browser
{
	/// <summary>
	/// Uses glyphs baked from desktop FreeType when the active (logical size, scaled pixel size) table is present;
	/// otherwise defers to <see cref="ManagedTrueTypeFont"/>.
	/// </summary>
	public sealed class BakedTrueTypeFont : IFont
	{
		readonly byte[] ttfData;
		readonly BakedFreeTypeFontBlob.ParsedBlob parsed;
		ManagedTrueTypeFont fallback;
		bool disposed;

		public BakedTrueTypeFont(byte[] ttfData, BakedFreeTypeFontBlob.ParsedBlob parsed)
		{
			this.ttfData = ttfData;
			this.parsed = parsed;
		}

		public FontGlyph CreateGlyph(char c, int size, float deviceScale)
		{
			if (disposed)
				return Empty();

			var scaled = (uint)(size * deviceScale);
			if (!parsed.Tables.TryGetValue((size, scaled), out var table))
				return Fallback().CreateGlyph(c, size, deviceScale);

			var cp = (uint)c;
			if (!table.TryGetValue(cp, out var e))
				return Fallback().CreateGlyph(c, size, deviceScale);

			byte[] data = null;
			if (e.Data != null && e.Data.Length > 0)
				data = (byte[])e.Data.Clone();

			return new FontGlyph
			{
				Advance = e.Advance,
				Offset = e.Offset,
				Size = e.Size,
				Data = data
			};
		}

		ManagedTrueTypeFont Fallback() => fallback ??= new ManagedTrueTypeFont(ttfData);

		static FontGlyph Empty() => new()
		{
			Offset = int2.Zero,
			Size = new Size(0, 0),
			Advance = 0,
			Data = null
		};

		public void Dispose()
		{
			disposed = true;
			fallback?.Dispose();
			fallback = null;
		}
	}
}
