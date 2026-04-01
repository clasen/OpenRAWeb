#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License.
 */
#endregion

#if OPENRA_BROWSER
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Reflection;
using OpenRA.FileSystem;

namespace OpenRA
{
	/// <summary>
	/// Maps resolved ^EngineDir paths (e.g. engine/mods/common) to the embedded mods zip; Wasm has no real engine directory.
	/// </summary>
	static class BrowserVirtualEnginePackages
	{
		internal static bool TryOpenStreamForResolvedPath(string resolvedPath, out Stream stream)
		{
			stream = null;
			if (!TrySplitEngineModsPath(resolvedPath, out var modId, out var subPath))
				return false;

			var zip = InstalledMods.BrowserModZipArchive;
			var mod = zip?.OpenSubfolder(modId);
			if (mod == null)
				return false;

			stream = mod.GetStream(subPath);
			mod.Dispose();
			return stream != null;
		}

		internal static bool TryOpenPackageForResolvedPath(string resolvedPath, out IReadOnlyPackage package)
		{
			package = null;
			var zip = InstalledMods.BrowserModZipArchive;
			if (zip == null)
				return false;

			var engineDir = Platform.EngineDir.Replace('\\', '/').TrimEnd('/') + "/";
			var path = resolvedPath.Replace('\\', '/');
			if (!path.StartsWith(engineDir, StringComparison.Ordinal))
				return false;

			var rel = path.Substring(engineDir.Length).TrimEnd('/');
			if (rel.Length == 0)
			{
				package = new BrowserEngineRootFolderPackage(zip);
				return true;
			}

			const string ModsPrefix = "mods/";
			if (!rel.StartsWith(ModsPrefix, StringComparison.Ordinal))
				return false;

			var afterMods = rel[ModsPrefix.Length..];
			var slash = afterMods.IndexOf('/');
			var modId = slash < 0 ? afterMods : afterMods[..slash];
			if (string.IsNullOrEmpty(modId))
				return false;

			package = zip.OpenSubfolder(modId);
			return package != null;
		}

		static bool TrySplitEngineModsPath(string resolvedPath, out string modId, out string subPath)
		{
			modId = null;
			subPath = null;

			var engineDir = Platform.EngineDir.Replace('\\', '/').TrimEnd('/') + "/";
			var path = resolvedPath.Replace('\\', '/');
			if (!path.StartsWith(engineDir, StringComparison.Ordinal))
				return false;

			var rel = path.Substring(engineDir.Length).TrimStart('/');
			const string ModsPrefix = "mods/";
			if (!rel.StartsWith(ModsPrefix, StringComparison.Ordinal))
				return false;

			var afterMods = rel[ModsPrefix.Length..];
			var slash = afterMods.IndexOf('/');
			if (slash <= 0 || slash >= afterMods.Length - 1)
				return false;

			modId = afterMods[..slash];
			subPath = afterMods[(slash + 1)..];
			return true;
		}
	}

	/// <summary>Virtual engine root so OpenPackage can resolve mods/... into the embedded zip.</summary>
	sealed class BrowserEngineRootFolderPackage : IReadOnlyPackage
	{
		readonly ZipFileLoader.ReadOnlyZipFile zip;

		public BrowserEngineRootFolderPackage(ZipFileLoader.ReadOnlyZipFile zip)
		{
			this.zip = zip;
		}

		public string Name => "BrowserEngineRoot";

		public IEnumerable<string> Contents => Array.Empty<string>();

		public Stream GetStream(string filename) => null;

		public bool Contains(string filename) => false;

		public IReadOnlyPackage OpenPackage(string filename, global::OpenRA.FileSystem.FileSystem context)
		{
			var n = filename.Replace('\\', '/').Trim('/');
			if (!n.StartsWith("mods/", StringComparison.Ordinal))
				return null;

			var rest = n["mods/".Length..];
			var s = rest.IndexOf('/');
			var modId = s < 0 ? rest : rest[..s];
			return string.IsNullOrEmpty(modId) ? null : zip.OpenSubfolder(modId);
		}

		public void Dispose() { }
	}

	/// <summary>
	/// Loose engine files (next to the desktop binary) are served from <c>wwwroot/engine/</c> in the Blazor host.
	/// Required by MIX loading to resolve hashed entries (e.g. temperat.pal) via global mix database.dat.
	/// </summary>
	static class BrowserEngineLooseHttpFiles
	{
		static readonly HttpClient Http = new();
		const string GlobalMixDatabaseResource = "OpenRA.BrowserGlobalMixDatabase.dat";

		internal static bool TryOpen(string filename, out Stream stream)
		{
			stream = null;

			// Keep the set minimal: only files the engine opens by bare name before MIX name resolution works.
			if (!string.Equals(filename, "global mix database.dat", StringComparison.OrdinalIgnoreCase))
				return false;

			try
			{
				using var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream(GlobalMixDatabaseResource);
				if (embedded != null)
				{
					var ms = new MemoryStream();
					embedded.CopyTo(ms);
					ms.Position = 0;
					stream = ms;
					return true;
				}
			}
			catch
			{
				stream = null;
			}

			if (string.IsNullOrEmpty(Game.WasmHttpOrigin))
				return false;

			try
			{
				var engineDir = Platform.EngineDir.Replace('\\', '/').TrimEnd('/') + "/";
				const string servedName = "global-mix-database.dat";
				var origin = Game.WasmHttpOrigin.EndsWith("/", StringComparison.Ordinal) ? Game.WasmHttpOrigin : Game.WasmHttpOrigin + "/";
				var uri = new Uri(new Uri(origin, UriKind.Absolute), engineDir + servedName);
				var bytes = Http.GetByteArrayAsync(uri).GetAwaiter().GetResult();
				stream = new MemoryStream(bytes, writable: false);
				return true;
			}
			catch
			{
				stream = null;
				return false;
			}
		}
	}
}
#endif
