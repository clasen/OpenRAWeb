#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

#if OPENRA_BROWSER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using OpenRA;

namespace OpenRA.FileSystem
{
	/// <summary>
	/// Serves <c>^SupportDir|Content/ra/v2/</c> (RA) and <c>^SupportDir|Content/cnc/</c> (CnC TD) over HTTP in Wasm.
	/// Files must exist under the host's <c>wwwroot/support/…</c>; manifests mirror each mod's <c>mod.yaml</c> required content.
	/// </summary>
	public static class BrowserSupportHttpContent
	{
		static readonly HttpClient Http = new();
		static readonly Dictionary<string, byte[]> prefetchedAssets = new(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Base URL (with trailing slash) used for GETs under <c>support/Content/…</c>. Uses <see cref="Game.WasmSupportAssetsBaseUrl"/> when set and valid http(s), else <see cref="Game.WasmHttpOrigin"/>.
		/// </summary>
		public static string EffectiveSupportAssetsOrigin()
		{
			var extra = Game.WasmSupportAssetsBaseUrl;
			if (!string.IsNullOrWhiteSpace(extra))
			{
				var t = extra.Trim();
				if (Uri.TryCreate(t, UriKind.Absolute, out var u) &&
				    (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps))
					return t.EndsWith("/", StringComparison.Ordinal) ? t : t + "/";
			}

			var o = Game.WasmHttpOrigin;
			if (string.IsNullOrEmpty(o))
				return null;

			return o.EndsWith("/", StringComparison.Ordinal) ? o : o + "/";
		}

		/// <summary>
		/// Bump when <see cref="DefaultRaV2Manifest"/> or shipped asset semantics change so browser IndexedDB entries are invalidated.
		/// </summary>
		public const string RaV2BrowserAssetCacheVersion = "ra-6";

		/// <summary>IndexedDB/cache namespace for CnC TD support mixes (distinct from RA keys where filenames overlap).</summary>
		public const string CncBrowserAssetCacheVersion = "cnc-3";

		/// <summary>
		/// Lean RA browser set: required <c>mods/ra/mod.yaml</c> content (same as non-optional ContentPackages).
		/// Optional <c>~content|scores.mix</c> is omitted so hosts without that file do not GET 404s; add the file under wwwroot to enable menu music.
		/// No <c>movies/</c>, no <c>general.mix</c>, no extra Counterstrike/Aftermath expand tracks.
		/// </summary>
		public static readonly string[] DefaultRaV2Manifest =
		{
			"allies.mix", "conquer.mix", "interior.mix", "lores.mix", "hires.mix", "local.mix",
			"russian.mix", "snow.mix", "sounds.mix", "speech.mix", "temperat.mix",
			"expand/expand2.mix", "expand/lores1.mix", "expand/hires1.mix",
			"cnc/desert.mix",
			"expand/chrotnk1.aud", "expand/fixit1.aud", "expand/jburn1.aud", "expand/jchrge1.aud",
			"expand/jcrisp1.aud", "expand/jdance1.aud", "expand/jjuice1.aud", "expand/jjump1.aud",
			"expand/jlight1.aud", "expand/jpower1.aud", "expand/jshock1.aud", "expand/jyes1.aud",
			"expand/madchrg2.aud", "expand/madexplo.aud", "expand/mboss1.aud", "expand/mhear1.aud",
			"expand/mhotdig1.aud", "expand/mhowdy1.aud", "expand/mhuh1.aud", "expand/mlaff1.aud",
			"expand/mrise1.aud", "expand/mwrench1.aud", "expand/myeehaw1.aud", "expand/myes1.aud",
		};

		/// <summary>Same as <see cref="DefaultRaV2Manifest"/>; order tuned so terrain/core mixes prefetch before unit voices.</summary>
		public static readonly string[] DefaultRaV2PreloadOrder =
		{
			"conquer.mix", "temperat.mix", "snow.mix", "interior.mix", "lores.mix", "hires.mix",
			"allies.mix", "local.mix", "russian.mix",
			"sounds.mix", "speech.mix",
			"expand/expand2.mix", "expand/lores1.mix", "expand/hires1.mix",
			"cnc/desert.mix",
			"expand/chrotnk1.aud", "expand/fixit1.aud", "expand/jburn1.aud", "expand/jchrge1.aud",
			"expand/jcrisp1.aud", "expand/jdance1.aud", "expand/jjuice1.aud", "expand/jjump1.aud",
			"expand/jlight1.aud", "expand/jpower1.aud", "expand/jshock1.aud", "expand/jyes1.aud",
			"expand/madchrg2.aud", "expand/madexplo.aud", "expand/mboss1.aud", "expand/mhear1.aud",
			"expand/mhotdig1.aud", "expand/mhowdy1.aud", "expand/mhuh1.aud", "expand/mlaff1.aud",
			"expand/mrise1.aud", "expand/mwrench1.aud", "expand/myeehaw1.aud", "expand/myes1.aud",
		};

		/// <summary>Paths relative to <c>Content/cnc/</c>: required <c>mods/cnc/mod.yaml</c> packages (optional <c>scores.mix</c> excluded; add file to wwwroot to enable menu music).</summary>
		public static readonly string[] DefaultCncManifest =
		{
			"speech.mix", "conquer.mix", "sounds.mix", "tempicnh.mix", "temperat.mix", "winter.mix", "desert.mix",
		};

		/// <summary>Preload order for CnC browser host (terrain-style mixes before speech/sounds).</summary>
		public static readonly string[] DefaultCncPreloadOrder =
		{
			"temperat.mix", "winter.mix", "desert.mix", "tempicnh.mix",
			"conquer.mix", "sounds.mix", "speech.mix",
		};

		/// <summary>Tries RA <c>Content/ra/v2</c> then CnC <c>Content/cnc</c> HTTP-backed roots.</summary>
		internal static bool TryOpenBrowserSupportHttpFolder(FileSystem fs, string resolvedPath, out IReadOnlyPackage package)
		{
			if (TryOpenRaV2RootFolder(fs, resolvedPath, out package))
				return true;
			return TryOpenCncContentRootFolder(fs, resolvedPath, out package);
		}

		internal static bool TryOpenRaV2RootFolder(FileSystem _, string resolvedPath, out IReadOnlyPackage package)
		{
			package = null;
			var assetOrigin = EffectiveSupportAssetsOrigin();
			if (string.IsNullOrEmpty(assetOrigin))
				return false;

			if (!IsRaV2ContentRootPath(resolvedPath, out var urlPrefix))
			{
				if (LooksLikeMisconfiguredRaContentPath(resolvedPath))
				{
					var expected = NormalizeFsPath(Path.Combine(Platform.SupportDir, "Content", "ra", "v2"));
					Console.WriteLine(
						$"[browser] support path mismatch: resolved=\"{NormalizeFsPath(resolvedPath)}\" " +
						$"expected=\"{expected}\" SupportDir=\"{Platform.SupportDir}\"");
				}
				return false;
			}

			// Avoid sync HTTP during Mount for manifest; default list matches mod.yaml. User can still override via wwwroot file after first paint if needed.
			var manifest = new List<string>(DefaultRaV2Manifest);

			package = new BrowserHttpFolderPackage(assetOrigin, urlPrefix, manifest);
			return true;
		}

		internal static bool TryOpenCncContentRootFolder(FileSystem _, string resolvedPath, out IReadOnlyPackage package)
		{
			package = null;
			var assetOrigin = EffectiveSupportAssetsOrigin();
			if (string.IsNullOrEmpty(assetOrigin))
				return false;

			if (!IsCncContentRootPath(resolvedPath, out var urlPrefix))
				return false;

			var manifest = new List<string>(DefaultCncManifest);
			package = new BrowserHttpFolderPackage(assetOrigin, urlPrefix, manifest);
			return true;
		}

		public static void CacheRaV2Asset(string relativePath, byte[] bytes)
		{
			if (string.IsNullOrWhiteSpace(relativePath) || bytes == null)
				return;

			prefetchedAssets[relativePath.Replace('\\', '/')] = bytes;
		}

		public static void ClearRaV2AssetCache()
		{
			prefetchedAssets.Clear();
		}

		static bool TryGetCachedRaV2Asset(string relativePath, out byte[] bytes)
		{
			return prefetchedAssets.TryGetValue(relativePath.Replace('\\', '/'), out bytes);
		}

		internal static bool IsRaV2ContentRootPath(string resolvedPath, out string urlPrefix)
		{
			urlPrefix = null;
			var path = NormalizeFsPath(resolvedPath);
			var expected = NormalizeFsPath(Path.Combine(Platform.SupportDir, "Content", "ra", "v2"));
			if (!PathsMatchSupportContentRoot(path, expected, "/Content/ra/v2"))
				return false;

			// Always use SupportDir-relative path for HTTP GETs (matches wwwroot/support/…), not an absolute resolved path.
			urlPrefix = expected + "/";
			return true;
		}

		internal static bool IsCncContentRootPath(string resolvedPath, out string urlPrefix)
		{
			urlPrefix = null;
			var path = NormalizeFsPath(resolvedPath);
			var expected = NormalizeFsPath(Path.Combine(Platform.SupportDir, "Content", "cnc"));
			if (!PathsMatchSupportContentRoot(path, expected, "/Content/cnc"))
				return false;

			urlPrefix = expected + "/";
			return true;
		}

		/// <summary>Exact match or ends with <paramref name="canonicalSuffix"/> (e.g. absolute cwd-prefixed paths on some runtimes).</summary>
		static bool PathsMatchSupportContentRoot(string normalizedPath, string expectedUnderSupport, string canonicalSuffix)
		{
			if (string.Equals(normalizedPath, expectedUnderSupport, StringComparison.OrdinalIgnoreCase))
				return true;

			if (normalizedPath.Length < canonicalSuffix.Length)
				return false;

			if (!normalizedPath.EndsWith(canonicalSuffix, StringComparison.OrdinalIgnoreCase))
				return false;

			if (normalizedPath.Length == canonicalSuffix.Length)
				return true;

			var c = normalizedPath[normalizedPath.Length - canonicalSuffix.Length - 1];
			return c == '/' || c == '\\';
		}

		/// <summary>True when the path is under SupportDir/Content/ra but not the exact v2 root (likely misconfiguration).</summary>
		static bool LooksLikeMisconfiguredRaContentPath(string resolvedPath)
		{
			var path = NormalizeFsPath(resolvedPath);
			var contentRa = NormalizeFsPath(Path.Combine(Platform.SupportDir, "Content", "ra"));
			if (path.Length < contentRa.Length)
				return false;

			if (!path.StartsWith(contentRa, StringComparison.OrdinalIgnoreCase))
				return false;

			// Exact v2 root is handled by IsRaV2ContentRootPath; do not warn here.
			var expectedV2 = NormalizeFsPath(Path.Combine(Platform.SupportDir, "Content", "ra", "v2"));
			if (string.Equals(path, expectedV2, StringComparison.OrdinalIgnoreCase))
				return false;

			// Path is Content/ra or Content/ra/... (wrong subpath or trailing typo).
			if (path.Length == contentRa.Length)
				return true;

			return path.Length > contentRa.Length && (path[contentRa.Length] == '/' || path[contentRa.Length] == '\\');
		}

		static string NormalizeFsPath(string p) =>
			(p ?? string.Empty).Replace('\\', '/').TrimEnd('/');

		static Uri BuildAbsoluteUri(string origin, string relativePath)
		{
			var o = origin.EndsWith("/", StringComparison.Ordinal) ? origin : origin + "/";
			var rel = relativePath.Replace('\\', '/');
			if (rel.StartsWith("/", StringComparison.Ordinal))
				rel = rel[1..];
			return new Uri(new Uri(o, UriKind.Absolute), rel);
		}

		sealed class BrowserHttpFolderPackage : IReadOnlyPackage
		{
			readonly string origin;
			readonly string urlPrefix;
			readonly HashSet<string> allPaths;
			readonly HashSet<string> topLevelFiles;
			readonly HashSet<string> topLevelDirs;
			readonly List<string> orderedContents;

			public BrowserHttpFolderPackage(string wasmOrigin, string urlPrefix, IEnumerable<string> manifestRelativePaths)
			{
				origin = wasmOrigin;
				this.urlPrefix = urlPrefix.Replace('\\', '/');
				if (!this.urlPrefix.EndsWith("/", StringComparison.Ordinal))
					this.urlPrefix += "/";

				allPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var line in manifestRelativePaths)
				{
					var s = line.Trim().Replace('\\', '/');
					if (s.Length == 0 || s.StartsWith("#", StringComparison.Ordinal))
						continue;
					allPaths.Add(s);
				}

				topLevelFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				topLevelDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var p in allPaths)
				{
					var slash = p.IndexOf('/');
					if (slash < 0)
						topLevelFiles.Add(p);
					else
						topLevelDirs.Add(p[..slash]);
				}

				orderedContents = topLevelFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
					.Concat(topLevelDirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
					.ToList();
			}

			BrowserHttpFolderPackage(string wasmOrigin, string urlPrefix, HashSet<string> subtreePaths)
			{
				origin = wasmOrigin;
				this.urlPrefix = urlPrefix.Replace('\\', '/');
				if (!this.urlPrefix.EndsWith("/", StringComparison.Ordinal))
					this.urlPrefix += "/";

				allPaths = subtreePaths;
				topLevelFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				topLevelDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach (var p in allPaths)
				{
					var slash = p.IndexOf('/');
					if (slash < 0)
						topLevelFiles.Add(p);
					else
						topLevelDirs.Add(p[..slash]);
				}

				orderedContents = topLevelFiles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
					.Concat(topLevelDirs.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
					.ToList();
			}

			public string Name => urlPrefix;

			public IEnumerable<string> Contents => orderedContents;

			public bool Contains(string filename)
			{
				filename = filename.Replace('\\', '/');
				if (filename.Length == 0)
					return false;

				// Do not probe with HTTP HEAD here: dev static file servers and Wasm often fail HEAD
				// while GET works, which breaks ContentInstaller RequiredContentFiles (Exists) checks.
				if (filename.IndexOf('/') >= 0)
					return allPaths.Contains(filename);

				if (topLevelFiles.Contains(filename))
					return allPaths.Contains(filename);

				return topLevelDirs.Contains(filename);
			}

			public Stream GetStream(string filename)
			{
				filename = filename.Replace('\\', '/');
				if (!allPaths.Contains(filename))
					return null;

				if (TryGetCachedRaV2Asset(filename, out var prefetched))
					return new MemoryStream(prefetched, writable: false);

				try
				{
					var uri = BuildAbsoluteUri(origin, urlPrefix + filename);
					var bytes = Http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
					CacheRaV2Asset(filename, bytes);
					return new MemoryStream(bytes);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[browser] Failed to GET support asset '{urlPrefix + filename}': {ex.Message}");
					return null;
				}
			}

			public IReadOnlyPackage OpenPackage(string filename, FileSystem context)
			{
				filename = filename.Replace('\\', '/');
				if (topLevelDirs.Contains(filename))
				{
					var prefix = filename + "/";
					var child = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
					foreach (var p in allPaths)
					{
						if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && p.Length > prefix.Length)
							child.Add(p[prefix.Length..]);
					}

					return new BrowserHttpFolderPackage(origin, urlPrefix + filename + "/", child);
				}

				if (filename.EndsWith(".mix", StringComparison.OrdinalIgnoreCase))
				{
					if (!topLevelFiles.Contains(filename) && !allPaths.Contains(filename))
						return null;

					try
					{
						byte[] bytes;
						if (TryGetCachedRaV2Asset(filename, out var prefetched))
							bytes = prefetched;
						else
						{
							bytes = Http.GetByteArrayAsync(BuildAbsoluteUri(origin, urlPrefix + filename)).GetAwaiter().GetResult();
							CacheRaV2Asset(filename, bytes);
						}

						var ms = new MemoryStream(bytes);
						if (!context.TryParsePackage(ms, filename, out var package))
						{
							ms.Dispose();
							return null;
						}

						return package;
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[browser] Failed to open support mix '{urlPrefix + filename}': {ex.Message}");
						return null;
					}
				}

				return null;
			}

			public void Dispose() { }
		}
	}
}
#endif
