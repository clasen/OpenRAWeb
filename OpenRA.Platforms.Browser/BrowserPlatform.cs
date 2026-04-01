#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Browser
{
	public sealed class BrowserPlatform : IPlatform
	{
		public IPlatformWindow CreateWindow(
			Size size, WindowMode windowMode, float scaleModifier, int vertexBatchSize, int indexBatchSize, int videoDisplay, GLProfile profile)
		{
			return new BrowserPlatformWindow(size, windowMode, scaleModifier, vertexBatchSize, indexBatchSize, videoDisplay, profile);
		}

		public ISoundEngine CreateSound(string device) => new WebAudioSoundEngine();

		public IFont CreateFont(byte[] data)
		{
			if (BrowserBakedFontStore.TryGetBlob(data, out var blob) &&
				BakedFreeTypeFontBlob.TryParse(blob, out var parsed, out _) &&
				BakedFreeTypeFontBlob.TtfMatchesBlob(data, parsed))
				return new BakedTrueTypeFont(data, parsed);

			return new ManagedTrueTypeFont(data);
		}
	}
}
