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

namespace OpenRA.Platforms.Browser
{
	abstract class ThreadAffine
	{
		protected ThreadAffine()
		{
			SetThreadAffinity();
		}

		protected void SetThreadAffinity()
		{
			// Reserved for future use; see VerifyThreadAffinity.
		}

		protected void VerifyThreadAffinity()
		{
			// WebAssembly: JS rAF / interop callbacks can run with a different CurrentManagedThreadId than
			// Blazor's first-chance init thread even though execution is logically single-threaded.
			// Throwing here stops the rAF loop after the first frame → black canvas.
		}
	}
}
