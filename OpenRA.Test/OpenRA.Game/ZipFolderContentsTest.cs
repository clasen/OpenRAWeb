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

using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using OpenRA.FileSystem;
using Fs = OpenRA.FileSystem.FileSystem;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ZipFolderContentsTest
	{
		static MemoryStream CreateZipWithNestedMapPath()
		{
			var ms = new MemoryStream();
			using (var z = new ZipArchive(ms, ZipArchiveMode.Create, true))
			{
				var e = z.CreateEntry("ra/maps/desert-shellmap/map.yaml");
				using (var w = new StreamWriter(e.Open()))
					w.Write("Map:\n\tAuthor: test");
			}

			ms.Position = 0;
			return ms;
		}

		[Test]
		public void ZipFolderListsNestedMapDirectory()
		{
			using var ms = CreateZipWithNestedMapPath();
			var loader = new ZipFileLoader();
			var fs = new Fs("ra", new Dictionary<string, OpenRA.Manifest>(), System.Array.Empty<IPackageLoader>());
			Assert.IsTrue(loader.TryParsePackage(ms, "test.zip", fs, out var root));
			try
			{
				Assert.That(root, Is.InstanceOf<ZipFileLoader.ReadOnlyZipFile>());
				var zipRoot = (ZipFileLoader.ReadOnlyZipFile)root;
				var ra = zipRoot.OpenSubfolder("ra");
				Assert.IsNotNull(ra);
				CollectionAssert.Contains(ra.Contents.ToList(), "maps");

				var maps = ra.OpenPackage("maps", fs);
				Assert.IsNotNull(maps, "maps package should open (implicit directory from nested file paths)");
				var mapEntries = maps.Contents.ToList();
				CollectionAssert.Contains(mapEntries, "desert-shellmap");

				var mapPkg = maps.OpenPackage("desert-shellmap", fs);
				Assert.IsNotNull(mapPkg);
				Assert.AreEqual("ra/maps/desert-shellmap", mapPkg.Name.Replace('\\', '/'));

				// MapPreview.PackageName is the inner ZipFolder's full path; reopen must not double-prefix.
				var reopened = maps.OpenPackage(mapPkg.Name, fs);
				Assert.IsNotNull(reopened, "OpenPackage(full inner path) must reopen after DisposePackage-style lazy reload");
				Assert.IsNotNull(reopened.GetStream("map.yaml"));
			}
			finally
			{
				root.Dispose();
			}
		}
	}
}
