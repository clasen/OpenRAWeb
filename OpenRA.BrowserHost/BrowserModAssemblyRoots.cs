#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License.
 */
#endregion

namespace OpenRA.BrowserHost
{
	/// <summary>
	/// Ensures mod assemblies are part of the Blazor app (trimmer roots + loaded before <see cref="Game.BrowserInitialize"/>).
	/// </summary>
	static class BrowserModAssemblyRoots
	{
		internal static void Touch()
		{
			_ = typeof(OpenRA.Mods.Common.PlayerExtensions);
			_ = typeof(OpenRA.Mods.Cnc.Util);
		}
	}
}
