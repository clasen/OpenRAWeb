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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ICSharpCode.SharpZipLib.Zip;

namespace OpenRA.FileSystem
{
	public class ZipFileLoader : IPackageLoader
	{
		const uint ZipSignature = 0x04034b50;

		public class ReadOnlyZipFile : IReadOnlyPackage
		{
			public string Name { get; protected set; }
			protected ZipFile pkg;

			// Dummy constructor for use with ReadWriteZipFile
			protected ReadOnlyZipFile() { }

			public ReadOnlyZipFile(Stream s, string filename)
			{
				Name = filename;
				pkg = new ZipFile(s);
			}

			public Stream GetStream(string filename)
			{
				var entry = pkg.GetEntry(filename);
				if (entry == null)
					return null;

				using (var z = pkg.GetInputStream(entry))
				{
					var ms = new MemoryStream((int)entry.Size);
					z.CopyTo(ms);
					ms.Seek(0, SeekOrigin.Begin);
					return ms;
				}
			}

			public IEnumerable<string> Contents
			{
				get
				{
					foreach (ZipEntry entry in pkg)
						if (entry.IsFile)
							yield return entry.Name;
				}
			}

			public bool Contains(string filename)
			{
				return pkg.GetEntry(filename) != null;
			}

			/// <summary>Open a top-level mod folder (e.g. "ra") even if the archive has no explicit directory entry.</summary>
			public IReadOnlyPackage OpenSubfolder(string folderName)
			{
				var prefix = folderName + "/";
				foreach (var name in Contents)
					if (name.StartsWith(prefix, StringComparison.Ordinal))
						return new ZipFolder(this, folderName);

				return null;
			}

			public void Dispose()
			{
				pkg?.Close();
				GC.SuppressFinalize(this);
			}

			public IReadOnlyPackage OpenPackage(string filename, FileSystem context)
			{
				// Directories are stored with a trailing "/" in the index
				var entry = pkg.GetEntry(filename) ?? pkg.GetEntry(filename + "/");
				if (entry == null)
				{
					// No explicit directory entry (common for archives built without folder markers).
					var normalized = filename.Replace('\\', '/').TrimEnd('/');
					var prefix = normalized + "/";
					foreach (ZipEntry ze in pkg)
					{
						if (ze.IsFile && ze.Name.StartsWith(prefix, StringComparison.Ordinal))
							return new ZipFolder(this, normalized);
					}

					return null;
				}

				if (entry.IsDirectory)
					return new ZipFolder(this, filename);

				// Other package types can be loaded normally
				var s = GetStream(filename);
				if (s == null)
					return null;

				if (context.TryParsePackage(s, filename, out var package))
					return package;

				s.Dispose();
				return null;
			}
		}

		public sealed class ReadWriteZipFile : ReadOnlyZipFile, IReadWritePackage
		{
			readonly MemoryStream pkgStream = new();

			public ReadWriteZipFile(string filename, bool create = false)
			{
				// SharpZipLib breaks when asked to update archives loaded from outside streams or files
				// We can work around this by creating a clean in-memory-only file, cutting all outside references
				if (!create)
				{
					using (var copy = new MemoryStream(File.ReadAllBytes(filename)))
					{
						pkgStream.Capacity = (int)copy.Length;
						copy.CopyTo(pkgStream);
					}
				}

				pkgStream.Position = 0;
				pkg = new ZipFile(pkgStream);
				Name = filename;

				// Remove subfields that can break ZIP updating.
				foreach (ZipEntry entry in pkg)
					entry.ExtraData = null;
			}

			void Commit()
			{
				File.WriteAllBytes(Name, pkgStream.ToArray());
			}

			public void Update(string filename, byte[] contents)
			{
				pkg.BeginUpdate();
				pkg.Add(new StaticStreamDataSource(new MemoryStream(contents)), filename);
				pkg.CommitUpdate();
				Commit();
			}

			public void Delete(string filename)
			{
				pkg.BeginUpdate();
				pkg.Delete(filename);
				pkg.CommitUpdate();
				Commit();
			}
		}

		sealed class ZipFolder : IReadOnlyPackage
		{
			public string Name { get; }
			public ReadOnlyZipFile Parent { get; }

			public ZipFolder(ReadOnlyZipFile parent, string path)
			{
				if (path.EndsWith('/'))
					path = path[..^1];

				Name = path;
				Parent = parent;
			}

			public Stream GetStream(string filename)
			{
				// Zip files use '/' as a path separator
				return Parent.GetStream(Name + '/' + filename);
			}

			public IEnumerable<string> Contents
			{
				get
				{
					// Mirror Folder: top-level files and subdirectories under this logical path.
					var prefix = Name + "/";
					var seen = new HashSet<string>(StringComparer.Ordinal);
					foreach (var entry in Parent.Contents)
					{
						if (!entry.StartsWith(prefix, StringComparison.Ordinal))
							continue;

						var remainder = entry[prefix.Length..];
						if (string.IsNullOrEmpty(remainder))
							continue;

						var slash = remainder.IndexOf('/');
						var first = slash >= 0 ? remainder[..slash] : remainder;
						if (string.IsNullOrEmpty(first))
							continue;

						seen.Add(first);
					}

					foreach (var n in seen.OrderBy(x => x, StringComparer.Ordinal))
						yield return n;
				}
			}

			public bool Contains(string filename)
			{
				return Parent.Contains(Name + '/' + filename);
			}

			public IReadOnlyPackage OpenPackage(string filename, FileSystem context)
			{
				// MapPreview stores the inner package's full zip path (e.g. ra/maps/desert-shellmap).
				// Reopening must not prefix again (would yield ra/maps/ra/maps/... and return null).
				var rel = filename.Replace('\\', '/');
				var pfx = Name.Replace('\\', '/').TrimEnd('/');

				if (rel.StartsWith(pfx + "/", StringComparison.OrdinalIgnoreCase) && rel.Length > pfx.Length + 1)
					return Parent.OpenPackage(rel, context);

				return Parent.OpenPackage(pfx + "/" + rel, context);
			}

			public void Dispose() { /* nothing to do */ }
		}

		sealed class StaticStreamDataSource : IStaticDataSource
		{
			readonly Stream s;
			public StaticStreamDataSource(Stream s)
			{
				this.s = s;
			}

			public Stream GetSource()
			{
				return s;
			}
		}

		public bool TryParsePackage(Stream s, string filename, FileSystem context, out IReadOnlyPackage package)
		{
			var readSignature = s.ReadUInt32();
			s.Position -= 4;

			if (readSignature != ZipSignature)
			{
				package = null;
				return false;
			}

			package = new ReadOnlyZipFile(s, filename);
			return true;
		}

		public static bool TryParseReadWritePackage(string filename, out IReadWritePackage package)
		{
			using (var s = File.OpenRead(filename))
			{
				if (s.ReadUInt32() != ZipSignature)
				{
					package = null;
					return false;
				}
			}

			package = new ReadWriteZipFile(filename);
			return true;
		}

		public static IReadWritePackage Create(string filename)
		{
			return new ReadWriteZipFile(filename, true);
		}
	}
}
