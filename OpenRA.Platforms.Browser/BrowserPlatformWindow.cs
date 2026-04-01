#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Browser
{
	/// <summary>HTML canvas element id used for WebGL2 (set before <see cref="Game.Initialize"/>).</summary>
	public static class BrowserWindowSettings
	{
		public static string CanvasElementId { get; set; } = "game-canvas";
	}

	sealed class BrowserPlatformWindow : ThreadAffine, IPlatformWindow
	{
		readonly BrowserInput input;
		readonly object syncObject = new();
		Size windowSize;
		Size surfaceSize;
		float windowScale = 1f;
		float scaleModifier;
		int2? lockedMousePosition;
		bool disposed;
		readonly GLProfile profile;
		readonly GLProfile[] supportedProfiles;

		public IGraphicsContext Context { get; }

		public Size NativeWindowSize
		{
			get
			{
				lock (syncObject)
					return windowSize;
			}
		}

		public Size EffectiveWindowSize
		{
			get
			{
				lock (syncObject)
					return new Size((int)(windowSize.Width / scaleModifier), (int)(windowSize.Height / scaleModifier));
			}
		}

		public float NativeWindowScale
		{
			get
			{
				lock (syncObject)
					return windowScale;
			}
		}

		public float EffectiveWindowScale
		{
			get
			{
				lock (syncObject)
					return windowScale * scaleModifier;
			}
		}

		public Size SurfaceSize
		{
			get
			{
				lock (syncObject)
					return surfaceSize;
			}
		}

		public int CurrentDisplay => 0;
		public int DisplayCount => 1;
		public bool HasInputFocus { get; internal set; } = true;
		public bool IsSuspended { get; internal set; }

		public GLProfile GLProfile
		{
			get
			{
				lock (syncObject)
					return profile;
			}
		}

		public GLProfile[] SupportedGLProfiles
		{
			get
			{
				lock (syncObject)
					return supportedProfiles;
			}
		}

		public event Action<float, float, float, float> OnWindowScaleChanged = (_, _, _, _) => { };

		public BrowserPlatformWindow(Size requestEffectiveWindowSize, WindowMode windowMode,
			float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile requestProfile)
		{
			this.scaleModifier = scaleModifier;
			profile = GLProfile.Embedded;
			supportedProfiles = new[] { GLProfile.Embedded };

			if (!WebGLInterop.Init(BrowserWindowSettings.CanvasElementId))
				throw new InvalidOperationException("WebGL2 init failed for canvas #" + BrowserWindowSettings.CanvasElementId);

			var w = requestEffectiveWindowSize.Width > 0 ? requestEffectiveWindowSize.Width : 1280;
			var h = requestEffectiveWindowSize.Height > 0 ? requestEffectiveWindowSize.Height : 720;
			lock (syncObject)
			{
				windowSize = new Size(w, h);
				surfaceSize = new Size(w, h);
			}

			WebGLInterop.ResizeBackingStore(w, h);

			var ctx = new BrowserGraphicsContext(this);
			ctx.InitializeOpenGL();
			Context = ctx;
			Context.SetVSyncEnabled(false);
			input = new BrowserInput();

			Console.WriteLine($"Browser canvas: {w}x{h} (WebGL2)");
		}

		public void ApplyCanvasSize(int width, int height)
		{
			if (width <= 0 || height <= 0)
				return;

			float oldNative, oldEff, newNative, newEff;
			lock (syncObject)
			{
				oldNative = windowScale;
				oldEff = windowScale * scaleModifier;
				windowSize = new Size(width, height);
				surfaceSize = new Size(width, height);
				windowScale = 1f;
				newNative = windowScale;
				newEff = windowScale * scaleModifier;
			}

			WebGLInterop.ResizeBackingStore(width, height);
			OnWindowScaleChanged(oldNative, oldEff, newNative, newEff);
		}

		internal void WindowSizeChanged() { }

		public IHardwareCursor CreateHardwareCursor(string name, Size size, byte[] data, int2 hotspot, bool pixelDouble)
		{
			return null;
		}

		public void SetHardwareCursor(IHardwareCursor cursor)
		{
		}

		public void SetWindowTitle(string title)
		{
			Console.WriteLine("Window: " + title);
		}

		public void SetRelativeMouseMode(bool mode)
		{
			if (mode)
				lockedMousePosition = int2.Zero;
			else
				lockedMousePosition = null;
		}

		public void SetScaleModifier(float scale)
		{
			var oldScaleModifier = scaleModifier;
			scaleModifier = scale;
			float ws;
			lock (syncObject)
				ws = windowScale;
			OnWindowScaleChanged(ws, ws * oldScaleModifier, ws, ws * scaleModifier);
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			Context?.Dispose();
		}

		public void GrabWindowMouseFocus() { }

		public void ReleaseWindowMouseFocus() { }

		public void PumpInput(IInputHandler inputHandler)
		{
			VerifyThreadAffinity();
			input.PumpInput(this, inputHandler, lockedMousePosition);
		}

		public string GetClipboardText() => BrowserInput.GetClipboardText();

		public bool SetClipboardText(string text) => BrowserInput.SetClipboardText(text);
	}
}
