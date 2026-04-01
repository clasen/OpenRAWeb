#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version.
 */
#endregion

using System.Collections.Concurrent;

namespace OpenRA.Platforms.Browser
{
	public enum BrowserInputKind : byte
	{
		MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp, TextInput,
		Focus, Blur, Resize
	}

	public readonly struct BrowserInputEnvelope
	{
		public readonly BrowserInputKind Kind;
		public readonly int X, Y, Dx, Dy, Button, Mods, KeyCode, Delta;
		public readonly string Text;

		public BrowserInputEnvelope(BrowserInputKind kind, int x = 0, int y = 0, int dx = 0, int dy = 0,
			int button = 0, int mods = 0, int keyCode = 0, int delta = 0, string text = null)
		{
			Kind = kind;
			X = x;
			Y = y;
			Dx = dx;
			Dy = dy;
			Button = button;
			Mods = mods;
			KeyCode = keyCode;
			Delta = delta;
			Text = text;
		}
	}

	public static class BrowserInputQueue
	{
		static readonly ConcurrentQueue<BrowserInputEnvelope> Queue = new();

		public static void Enqueue(in BrowserInputEnvelope e) => Queue.Enqueue(e);

		internal static bool TryDequeue(out BrowserInputEnvelope e) => Queue.TryDequeue(out e);
	}
}
