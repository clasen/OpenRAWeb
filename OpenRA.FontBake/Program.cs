#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenRA.Graphics;
using OpenRA.Platforms.Default;

namespace OpenRA.FontBake
{
	static class Program
	{
		static int Main(string[] args)
		{
			try
			{
				TryInstallFreetypeDllImportResolver();
				return Run(args);
			}
			catch (DllNotFoundException e) when (e.Message.Contains("freetype", StringComparison.OrdinalIgnoreCase))
			{
				Console.Error.WriteLine(e);
				Console.Error.WriteLine();
				Console.Error.WriteLine("FreeType native library could not be loaded.");
				if (OperatingSystem.IsMacOS())
					Console.Error.WriteLine("On Apple Silicon, install an arm64 build:  brew install freetype");
				else if (OperatingSystem.IsLinux())
					Console.Error.WriteLine("Install FreeType (e.g. libfreetype6) via your package manager.");
				return 1;
			}
			catch (Exception e)
			{
				Console.Error.WriteLine(e);
				return 1;
			}
		}

		/// <summary>
		/// OpenRA-Freetype6 NuGet ships an x86_64 macOS dylib; on arm64 .NET cannot load it.
		/// Resolve <c>freetype6</c> to Homebrew's <c>libfreetype.6.dylib</c> when present.
		/// </summary>
		static void TryInstallFreetypeDllImportResolver()
		{
			var ftAsm = typeof(FreeTypeFont).Assembly;
			NativeLibrary.SetDllImportResolver(ftAsm, (name, _, _) =>
			{
				if (name != "freetype6")
					return IntPtr.Zero;

				if (OperatingSystem.IsMacOS())
				{
					foreach (var p in MacHomebrewLibFreetypePaths())
					{
						if (File.Exists(p) && NativeLibrary.TryLoad(p, out var h))
							return h;
					}
				}
				else if (OperatingSystem.IsLinux())
				{
					foreach (var p in LinuxSystemLibFreetypePaths())
					{
						if (File.Exists(p) && NativeLibrary.TryLoad(p, out var h))
							return h;
					}
				}

				return IntPtr.Zero;
			});
		}

		static IEnumerable<string> MacHomebrewLibFreetypePaths()
		{
			yield return "/opt/homebrew/opt/freetype/lib/libfreetype.6.dylib";
			yield return "/opt/homebrew/lib/libfreetype.6.dylib";
			yield return "/usr/local/opt/freetype/lib/libfreetype.6.dylib";
			yield return "/usr/local/lib/libfreetype.6.dylib";
		}

		static IEnumerable<string> LinuxSystemLibFreetypePaths()
		{
			yield return "/usr/lib/x86_64-linux-gnu/libfreetype.so.6";
			yield return "/usr/lib/aarch64-linux-gnu/libfreetype.so.6";
			yield return "/lib/x86_64-linux-gnu/libfreetype.so.6";
			yield return "/lib/aarch64-linux-gnu/libfreetype.so.6";
		}

		static int Run(string[] args)
		{
			if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
			{
				PrintHelp();
				return args.Length == 0 ? 1 : 0;
			}

			var input = GetArg(args, "--input");
			var output = GetArg(args, "--output");
			if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output))
			{
				PrintHelp();
				return 1;
			}

			if (!File.Exists(input))
			{
				Console.Error.WriteLine("Input font not found: " + input);
				return 1;
			}

			var sizes = ParseIntList(GetArg(args, "--sizes") ?? "10,12,14,18,24,32");
			var scales = ParseFloatList(GetArg(args, "--scales") ?? "1,2");
			var codepoints = BuildCodepointSet(GetArg(args, "--codepoints"));

			Directory.CreateDirectory(output);
			var ttf = File.ReadAllBytes(input);
			var tables = new List<BakedFreeTypeFontBlob.TableBuilder>();

			var ft = new FreeTypeFont(ttf);
			try
			{
				foreach (var size in sizes)
				{
					foreach (var ds in scales)
					{
						var tb = new BakedFreeTypeFontBlob.TableBuilder
						{
							LogicalSize = size,
							DeviceScale = ds
						};

						foreach (var cp in codepoints)
						{
							var g = ft.CreateGlyph((char)cp, size, ds);
							tb.Glyphs[cp] = g;
						}

						tables.Add(tb);
					}
				}
			}
			finally
			{
				ft.Dispose();
			}

			var shaHex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ttf)).ToLowerInvariant();
			var outFile = Path.Combine(output, shaHex + ".orbf");
			using (var fs = File.Create(outFile))
				BakedFreeTypeFontBlob.Write(fs, ttf, tables);

			var manifestPath = Path.Combine(output, "manifest.json");
			var manifest = new BakedFontManifest { Files = new List<string> { Path.GetFileName(outFile) } };
			if (File.Exists(manifestPath))
			{
				try
				{
					var existing = JsonSerializer.Deserialize<BakedFontManifest>(File.ReadAllText(manifestPath));
					if (existing?.Files != null)
					{
						var set = new HashSet<string>(existing.Files, StringComparer.Ordinal);
						set.Add(Path.GetFileName(outFile));
						manifest.Files = set.OrderBy(s => s).ToList();
					}
				}
				catch
				{
					// Replace with single-file manifest
				}
			}

			var opts = new JsonSerializerOptions { WriteIndented = true };
			File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, opts));
			Console.WriteLine("Wrote " + outFile);
			Console.WriteLine("Updated " + manifestPath);
			return 0;
		}

		static void PrintHelp()
		{
			Console.WriteLine(
				"OpenRA.FontBake — bake FreeType glyphs for the browser host.\n" +
				"Usage:\n" +
				"  OpenRA.FontBake --input path/to/font.ttf --output path/to/wwwroot/engine/baked-fonts/\n" +
				"Options:\n" +
				"  --sizes 10,12,14,18,24,32   Logical font sizes (YAML Size field)\n" +
				"  --scales 1,2                Device scale factors (window DPI)\n" +
				"  --codepoints ascii|extended|9-0x7e,0xa0-0x17f\n");
		}

		static string GetArg(string[] args, string name)
		{
			for (var i = 0; i < args.Length - 1; i++)
				if (args[i] == name)
					return args[i + 1];
			return null;
		}

		static int[] ParseIntList(string s) =>
			s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();

		static float[] ParseFloatList(string s) =>
			s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(x => float.Parse(x, CultureInfo.InvariantCulture)).ToArray();

		static SortedSet<uint> BuildCodepointSet(string spec)
		{
			var set = new SortedSet<uint>();
			if (string.IsNullOrEmpty(spec) || spec.Equals("extended", StringComparison.OrdinalIgnoreCase))
			{
				set.Add(0x09);
				for (var c = 0x20u; c <= 0x7eu; c++)
					set.Add(c);
				for (var c = 0xa0u; c <= 0x17fu; c++)
					set.Add(c);
				return set;
			}

			if (spec.Equals("ascii", StringComparison.OrdinalIgnoreCase))
			{
				set.Add(0x09);
				for (var c = 0x20u; c <= 0x7eu; c++)
					set.Add(c);
				return set;
			}

			foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var p = part.Trim();
				if (p.Contains('-', StringComparison.Ordinal))
				{
					var ab = p.Split('-', 2);
					var a = ParseCp(ab[0]);
					var b = ParseCp(ab[1]);
					if (a > b)
						(a, b) = (b, a);
					for (var c = a; c <= b; c++)
						set.Add(c);
				}
				else
					set.Add(ParseCp(p));
			}

			return set;
		}

		static uint ParseCp(string s)
		{
			s = s.Trim();
			if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
				return Convert.ToUInt32(s, 16);
			return uint.Parse(s, CultureInfo.InvariantCulture);
		}

		sealed class BakedFontManifest
		{
			[JsonPropertyName("files")]
			public List<string> Files { get; set; }
		}
	}
}
