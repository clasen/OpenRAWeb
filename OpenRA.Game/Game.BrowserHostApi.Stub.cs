#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

#if !OPENRA_BROWSER
using System;

namespace OpenRA
{
	public static partial class Game
	{
		public static void BrowserInitialize(string[] args)
		{
			throw new PlatformNotSupportedException(
				"BrowserInitialize requires OpenRA.Game built with OpenRaBrowserBuild=true (OPENRA_BROWSER).");
		}

		public static bool BrowserTick() => false;

		public static RunStatus BrowserShutdown() => state;
	}
}
#endif
