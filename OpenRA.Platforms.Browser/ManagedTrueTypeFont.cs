#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System;
using System.Globalization;
using System.IO;
using OpenRA;
using OpenRA.Primitives;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using OraSize = OpenRA.Primitives.Size;

namespace OpenRA.Platforms.Browser
{
	/// <summary>
	/// Rasterizes TrueType/OpenType from memory using SixLabors (managed — works on WebAssembly).
	/// Uses the same <see cref="RichTextOptions"/> for measurement and drawing so glyph bounds match the bitmap.
	/// Hinting and integer horizontal advance approximate desktop FreeType grid metrics and reduce table drift.
	/// </summary>
	public sealed class ManagedTrueTypeFont : IFont
	{
		static readonly FontGlyph EmptyGlyph = new()
		{
			Offset = int2.Zero,
			Size = new OraSize(0, 0),
			Advance = 0,
			Data = null
		};

		readonly FontCollection collection = new();
		readonly FontFamily family;
		bool disposed;

		public ManagedTrueTypeFont(byte[] data)
		{
			using var ms = new MemoryStream(data, writable: false);
			family = collection.Add(ms, CultureInfo.InvariantCulture);
		}

		public FontGlyph CreateGlyph(char c, int size, float deviceScale)
		{
			if (disposed)
				return EmptyGlyph;

			if (c == '\0' || (char.IsControl(c) && c != '\t'))
				return EmptyGlyph;

			var scaled = Math.Max(1f, size * deviceScale);
			var font = family.CreateFont(scaled, FontStyle.Regular);
			var text = c.ToString();

			var opts = new RichTextOptions(font)
			{
				Origin = PointF.Empty,
				HintingMode = HintingMode.Standard
			};

			FontRectangle bounds;
			if (TextMeasurer.TryMeasureCharacterBounds(text.AsSpan(), opts, out var charBounds) && charBounds.Length > 0)
				bounds = charBounds[0].Bounds;
			else
				bounds = TextMeasurer.MeasureBounds(text, opts);

			var advance = MeasureHorizontalAdvance(text, opts);

			if (bounds.Width <= 0 || bounds.Height <= 0)
			{
				return new FontGlyph
				{
					Offset = int2.Zero,
					Size = new OraSize(0, 0),
					Advance = advance,
					Data = null
				};
			}

			const float Pad = 2f;
			var imgW = (int)Math.Ceiling(bounds.Width + Pad * 2);
			var imgH = (int)Math.Ceiling(bounds.Height + Pad * 2);
			imgW = Math.Max(1, imgW);
			imgH = Math.Max(1, imgH);

			opts.Origin = new PointF(Pad - bounds.Left, Pad - bounds.Top);

			using var image = new Image<Rgba32>(imgW, imgH);
			image.Mutate(ctx =>
			{
				ctx.Clear(SixLabors.ImageSharp.Color.Transparent);
				ctx.DrawText(opts, text, SixLabors.ImageSharp.Color.White);
			});

			var glyphData = new byte[imgW * imgH];
			image.ProcessPixelRows(accessor =>
			{
				for (var y = 0; y < imgH; y++)
				{
					var row = accessor.GetRowSpan(y);
					for (var x = 0; x < imgW; x++)
						glyphData[y * imgW + x] = row[x].A;
				}
			});

			var ox = (int)Math.Floor(bounds.Left - Pad + 1e-4f);
			var oy = (int)Math.Floor(bounds.Top - Pad + 1e-4f);

			// Tight bounds to ink only (matches typical FreeType glyphs). Padded atlas cells would
			// inflate Sprite.Bounds and break global layout that uses glyph pixel extents.
			if (!TryCropToInk(glyphData, imgW, imgH, out var tight, out var tw, out var th, out var cx, out var cy))
			{
				return new FontGlyph
				{
					Offset = int2.Zero,
					Size = new OraSize(0, 0),
					Advance = advance,
					Data = null
				};
			}

			return new FontGlyph
			{
				Offset = new int2(ox + cx, oy + cy),
				Size = new OraSize(tw, th),
				Advance = advance,
				Data = tight
			};
		}

		static bool TryCropToInk(byte[] src, int w, int h, out byte[] tight, out int tw, out int th, out int cx, out int cy)
		{
			tight = null;
			tw = th = cx = cy = 0;
			var minX = w;
			var minY = h;
			var maxX = -1;
			var maxY = -1;
			for (var y = 0; y < h; y++)
			{
				var row = y * w;
				for (var x = 0; x < w; x++)
				{
					if (src[row + x] == 0)
						continue;
					if (x < minX)
						minX = x;
					if (y < minY)
						minY = y;
					if (x > maxX)
						maxX = x;
					if (y > maxY)
						maxY = y;
				}
			}

			if (maxX < 0)
				return false;

			tw = maxX - minX + 1;
			th = maxY - minY + 1;
			tight = new byte[tw * th];
			for (var y = 0; y < th; y++)
			{
				var s = (y + minY) * w + minX;
				Array.Copy(src, s, tight, y * tw, tw);
			}

			cx = minX;
			cy = minY;
			return true;
		}

		/// <summary>Horizontal advance for layout; rounded to whole pixels (FreeType truncates to int).</summary>
		static float MeasureHorizontalAdvance(string text, RichTextOptions opts)
		{
			var w = TextMeasurer.MeasureAdvance(text, opts).Width;
			if (w <= 0f)
				return 0f;

			return Math.Max(0f, (float)Math.Round(w));
		}

		public void Dispose()
		{
			disposed = true;
		}
	}
}
