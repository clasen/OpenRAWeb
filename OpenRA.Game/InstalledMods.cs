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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenRA.FileSystem;

namespace OpenRA
{
	public class InstalledMods : IReadOnlyDictionary<string, Manifest>
	{
		readonly Dictionary<string, Manifest> mods;

#if OPENRA_BROWSER
		static MemoryStream browserModsZipStream;
		static ZipFileLoader.ReadOnlyZipFile browserModsZipRoot;

		/// <summary>Embedded mods archive; used to back ^EngineDir paths in Wasm.</summary>
		public static ZipFileLoader.ReadOnlyZipFile BrowserModZipArchive => browserModsZipRoot;
#endif

		/// <summary>Initializes the collection of locally installed mods.</summary>
		/// <param name="searchPaths">Filesystem paths to search for mod packages.</param>
		/// <param name="explicitPaths">Filesystem paths to additional mod packages.</param>
		public InstalledMods(IEnumerable<string> searchPaths, IEnumerable<string> explicitPaths)
		{
#if OPENRA_BROWSER
			// Wasm has no real directory for ^EngineDir/mods; ship mods as an embedded zip (see OpenRA.Game.csproj).
			mods = GetInstalledModsBrowser(searchPaths, explicitPaths);
#else
			mods = GetInstalledMods(searchPaths, explicitPaths);
#endif
		}

#if OPENRA_BROWSER
		static Dictionary<string, Manifest> GetInstalledModsBrowser(IEnumerable<string> searchPaths, IEnumerable<string> explicitPaths)
		{
			var ret = LoadEmbeddedZipMods() ?? new Dictionary<string, Manifest>();

			foreach (var pair in GetCandidateMods(searchPaths).Concat(explicitPaths.Select(p => (Id: Path.GetFileNameWithoutExtension(p), Path: p))))
			{
				var mod = LoadMod(pair.Id, pair.Path);
				if (mod != null)
					ret[pair.Id] = mod;
			}

			return ret;
		}

		static Dictionary<string, Manifest> LoadEmbeddedZipMods()
		{
			try
			{
				var asm = typeof(InstalledMods).Assembly;
				using (var raw = asm.GetManifestResourceStream("OpenRA.BrowserMods.zip"))
				{
					if (raw == null)
						return null;

					browserModsZipStream?.Dispose();
					browserModsZipRoot?.Dispose();

					browserModsZipStream = new MemoryStream();
					raw.CopyTo(browserModsZipStream);
					browserModsZipStream.Position = 0;
					browserModsZipRoot = new ZipFileLoader.ReadOnlyZipFile(browserModsZipStream, "BrowserMods.zip");
				}

				const string ModYamlSuffix = "/mod.yaml";
				var modIds = new HashSet<string>();
				foreach (var name in browserModsZipRoot.Contents)
				{
					if (!name.EndsWith(ModYamlSuffix, StringComparison.OrdinalIgnoreCase))
						continue;

					var id = name[..^ModYamlSuffix.Length];
					if (id.Length == 0 || id.Contains('/'))
						continue;

					modIds.Add(id);
				}

				var ret = new Dictionary<string, Manifest>();
				foreach (var id in modIds.OrderBy(x => x, StringComparer.Ordinal))
				{
					var sub = browserModsZipRoot.OpenSubfolder(id);
					var mod = LoadModFromPackage(id, sub);
					if (mod != null)
						ret[id] = mod;
				}

				return ret;
			}
			catch (Exception e)
			{
				Console.WriteLine("Failed to load embedded browser mods: {0}", e.Message);
				return null;
			}
		}

		static Manifest LoadModFromPackage(string id, IReadOnlyPackage package)
		{
			try
			{
				if (package == null || !package.Contains("mod.yaml"))
				{
					package?.Dispose();
					return null;
				}

				return new Manifest(id, package);
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Load mod package '{id}': {e}");
				package?.Dispose();
				return null;
			}
		}
#endif

		static IEnumerable<(string Id, string Path)> GetCandidateMods(IEnumerable<string> searchPaths)
		{
			var mods = new List<(string, string)>();
			foreach (var path in searchPaths)
			{
				try
				{
					var resolved = Platform.ResolvePath(path);
					if (!Directory.Exists(resolved))
						continue;

					var directory = new DirectoryInfo(resolved);
					foreach (var subdir in directory.EnumerateDirectories())
						mods.Add((subdir.Name, subdir.FullName));
				}
				catch (Exception e)
				{
					Console.WriteLine("Failed to enumerate mod search path {0}: {1}", path, e.Message);
				}
			}

			return mods;
		}

		static Manifest LoadMod(string id, string path)
		{
			try
			{
				if (!Directory.Exists(path))
				{
					Log.Write("debug", path + " is not a valid mod package");
					return null;
				}

				var package = new Folder(path);
				if (package.Contains("mod.yaml"))
					return new Manifest(id, package);

				package.Dispose();
			}
			catch (Exception e)
			{
				Log.Write("debug", $"Load mod '{path}': {e}");
			}

			return null;
		}

		static Dictionary<string, Manifest> GetInstalledMods(IEnumerable<string> searchPaths, IEnumerable<string> explicitPaths)
		{
			var ret = new Dictionary<string, Manifest>();
			var candidates = GetCandidateMods(searchPaths)
				.Concat(explicitPaths.Select(p => (Id: Path.GetFileNameWithoutExtension(p), Path: p)));

			foreach (var pair in candidates)
			{
				var mod = LoadMod(pair.Id, pair.Path);
				if (mod != null)
					ret[pair.Id] = mod;
			}

			return ret;
		}

		public Manifest this[string key] => mods[key];
		public IEnumerable<string> Keys => mods.Keys;
		public IEnumerable<Manifest> Values => mods.Values;
		public int Count => mods.Count;
		public bool ContainsKey(string key) { return mods.ContainsKey(key); }
		public IEnumerator<KeyValuePair<string, Manifest>> GetEnumerator() { return mods.GetEnumerator(); }
		public bool TryGetValue(string key, out Manifest value) { return mods.TryGetValue(key, out value); }
		IEnumerator IEnumerable.GetEnumerator() { return mods.GetEnumerator(); }
	}
}
