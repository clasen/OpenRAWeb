#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version.
 */
#endregion

using System;
using System.Text;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Browser
{
	sealed class BrowserInput
	{
		MouseButton lastButtonBits = MouseButton.None;

		public static string GetClipboardText() => string.Empty;
		public static bool SetClipboardText(string text) => false;

		static Modifiers MakeModifiers(int raw)
		{
			return ((raw & 1) != 0 ? Modifiers.Alt : 0)
				| ((raw & 2) != 0 ? Modifiers.Ctrl : 0)
				| ((raw & 4) != 0 ? Modifiers.Meta : 0)
				| ((raw & 8) != 0 ? Modifiers.Shift : 0);
		}

		static MouseButton MakeButton(int b) =>
			b == 0 ? MouseButton.Left : b == 1 ? MouseButton.Middle : b == 2 ? MouseButton.Right : 0;

		static int2 EventPosition(BrowserPlatformWindow device, int x, int y)
		{
			if (device.EffectiveWindowSize != device.SurfaceSize)
			{
				var s = 1 / device.EffectiveWindowScale;
				return new int2((int)(Math.Sign(x) / 2f + x * s), (int)(Math.Sign(y) / 2f + y * s));
			}

			return new int2(x, y);
		}

		public void PumpInput(BrowserPlatformWindow device, IInputHandler inputHandler, int2? lockedMousePosition)
		{
			MouseInput? pendingMotion = null;
			while (BrowserInputQueue.TryDequeue(out var ev))
			{
				switch (ev.Kind)
				{
					case BrowserInputKind.Focus:
						device.HasInputFocus = true;
						break;
					case BrowserInputKind.Blur:
						device.HasInputFocus = false;
						break;
					case BrowserInputKind.Resize:
						device.ApplyCanvasSize(ev.X, ev.Y);
						break;
					case BrowserInputKind.MouseMove:
					{
						var mods = MakeModifiers(ev.Mods);
						inputHandler.ModifierKeys(mods);
						var input = lockedMousePosition ?? new int2(ev.X, ev.Y);
						var pos = EventPosition(device, input.X, input.Y);
						var delta = lockedMousePosition == null
							? EventPosition(device, ev.Dx, ev.Dy)
							: new int2(ev.X, ev.Y) - lockedMousePosition.Value;
						pendingMotion = new MouseInput(MouseInputEvent.Move, lastButtonBits, pos, delta, mods, 0);
						break;
					}

					case BrowserInputKind.MouseDown:
					case BrowserInputKind.MouseUp:
					{
						var mods = MakeModifiers(ev.Mods);
						inputHandler.ModifierKeys(mods);
						if (pendingMotion != null)
						{
							inputHandler.OnMouseInput(pendingMotion.Value);
							pendingMotion = null;
						}

						var button = MakeButton(ev.Button);
						if (ev.Kind == BrowserInputKind.MouseDown)
							lastButtonBits |= button;
						else
							lastButtonBits &= ~button;

						var input = lockedMousePosition ?? new int2(ev.X, ev.Y);
						var pos = EventPosition(device, input.X, input.Y);
						var evt = ev.Kind == BrowserInputKind.MouseDown ? MouseInputEvent.Down : MouseInputEvent.Up;
						var tap = ev.Kind == BrowserInputKind.MouseDown
							? MultiTapDetection.DetectFromMouse((byte)(ev.Button + 1), pos)
							: MultiTapDetection.InfoFromMouse((byte)(ev.Button + 1));
						inputHandler.OnMouseInput(new MouseInput(evt, button, pos, int2.Zero, mods, tap));
						break;
					}

					case BrowserInputKind.MouseWheel:
					{
						var mods = MakeModifiers(ev.Mods);
						inputHandler.ModifierKeys(mods);
						var pos = EventPosition(device, ev.X, ev.Y);
						inputHandler.OnMouseInput(new MouseInput(MouseInputEvent.Scroll, MouseButton.None, pos, new int2(0, ev.Delta), mods, 0));
						break;
					}

					case BrowserInputKind.TextInput:
						if (!string.IsNullOrEmpty(ev.Text))
							inputHandler.OnTextInput(ev.Text);
						break;

					case BrowserInputKind.KeyDown:
					case BrowserInputKind.KeyUp:
					{
						var mods = MakeModifiers(ev.Mods);
						inputHandler.ModifierKeys(mods);
						var keyCode = (Keycode)ev.KeyCode;
						var type = ev.Kind == BrowserInputKind.KeyDown ? KeyInputEvent.Down : KeyInputEvent.Up;
						var tapCount = ev.Kind == BrowserInputKind.KeyDown
							? MultiTapDetection.DetectFromKeyboard(keyCode, mods)
							: MultiTapDetection.InfoFromKeyboard(keyCode, mods);
						inputHandler.OnKeyInput(new KeyInput
						{
							Event = type,
							Key = keyCode,
							Modifiers = mods,
							UnicodeChar = ev.KeyCode > 0 && ev.KeyCode < 65536 ? (char)ev.KeyCode : '?',
							MultiTapCount = tapCount,
							IsRepeat = ev.Kind == BrowserInputKind.KeyDown && ev.Delta != 0
						});
						break;
					}
				}
			}

			if (pendingMotion != null)
				inputHandler.OnMouseInput(pendingMotion.Value);
		}
	}
}
