#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System.Runtime.InteropServices.JavaScript;

namespace OpenRA.Platforms.Browser
{
	internal static partial class WebAudioInterop
	{
		const string Module = "js/openra-audio.js";

		[JSImport("setMasterGain", Module)]
		internal static partial void SetMasterGain(float v);

		[JSImport("createPcmBuffer", Module)]
		internal static partial int CreatePcmBuffer(byte[] data, int channels, int sampleBits, int sampleRate);

		[JSImport("disposeBuffer", Module)]
		internal static partial void DisposeBuffer(int bufferId);

		[JSImport("playVoice", Module)]
		internal static partial int PlayVoice(int bufferId, bool loop, float volume);

		[JSImport("stopVoice", Module)]
		internal static partial void StopVoice(int voiceId);

		[JSImport("stopAllVoices", Module)]
		internal static partial void StopAllVoices();

		[JSImport("pauseVoice", Module)]
		internal static partial void PauseVoice(int voiceId, bool paused);

		[JSImport("setVoiceGain", Module)]
		internal static partial void SetVoiceGain(int voiceId, float gain);

		[JSImport("setVoiceLooping", Module)]
		internal static partial void SetVoiceLooping(int voiceId, bool loop);

		[JSImport("getVoiceGain", Module)]
		internal static partial float GetVoiceGain(int voiceId);

		[JSImport("getVoiceComplete", Module)]
		internal static partial bool GetVoiceComplete(int voiceId);

		[JSImport("getVoiceSeekSeconds", Module)]
		internal static partial double GetVoiceSeekSeconds(int voiceId);
	}
}
