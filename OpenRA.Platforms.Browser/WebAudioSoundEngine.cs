#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using OpenRA.Primitives;

namespace OpenRA.Platforms.Browser
{
	sealed class WebAudioSoundBuffer : ISoundSource
	{
		internal int BufferId { get; }
		internal int SampleRate { get; }
		bool disposed;

		public WebAudioSoundBuffer(int bufferId, int sampleRate)
		{
			BufferId = bufferId;
			SampleRate = sampleRate;
		}

		public void Dispose()
		{
			if (disposed)
				return;
			disposed = true;
			WebAudioInterop.DisposeBuffer(BufferId);
		}
	}

	sealed class WebAudioSound : ISound
	{
		readonly WebAudioSoundEngine engine;
		internal readonly int VoiceId;
		readonly int ownedBufferId;
		bool unregistered;
		bool releasedOwnedBuffer;

		internal bool IsDetached => unregistered;

		public WebAudioSound(WebAudioSoundEngine engine, int voiceId, int ownedBufferId = 0)
		{
			this.engine = engine;
			VoiceId = voiceId;
			this.ownedBufferId = ownedBufferId;
			engine.RegisterVoice(this);
		}

		internal void ReleaseOwnedBufferIfNeeded()
		{
			if (releasedOwnedBuffer || ownedBufferId == 0)
				return;
			WebAudioInterop.DisposeBuffer(ownedBufferId);
			releasedOwnedBuffer = true;
		}

		internal void DetachFromEngineStopAll()
		{
			ReleaseOwnedBufferIfNeeded();
			unregistered = true;
		}

		internal void StopInternal()
		{
			if (unregistered)
				return;
			WebAudioInterop.StopVoice(VoiceId);
			ReleaseOwnedBufferIfNeeded();
			engine.UnregisterVoice(this);
			unregistered = true;
		}

		public float Volume
		{
			get => unregistered ? float.NaN : WebAudioInterop.GetVoiceGain(VoiceId);
			set
			{
				if (unregistered)
					return;
				WebAudioInterop.SetVoiceGain(VoiceId, value);
			}
		}

		public float SeekPosition =>
			unregistered ? float.NaN : (float)WebAudioInterop.GetVoiceSeekSeconds(VoiceId);

		public bool Complete
		{
			get
			{
				if (unregistered)
					return true;
				if (!WebAudioInterop.GetVoiceComplete(VoiceId))
					return false;
				ReleaseOwnedBufferIfNeeded();
				engine.UnregisterVoice(this);
				unregistered = true;
				return true;
			}
		}

		public void SetPosition(WPos pos) { }
	}

	sealed class WebAudioSoundEngine : ISoundEngine
	{
		public bool Dummy => false;

		float volume = 1f;
		readonly Dictionary<int, WebAudioSound> voiceSounds = new();

		public SoundDevice[] AvailableDevices()
		{
			return new[] { new SoundDevice(null, "Web Audio") };
		}

		internal void RegisterVoice(WebAudioSound s) => voiceSounds[s.VoiceId] = s;

		internal void UnregisterVoice(WebAudioSound s) => voiceSounds.Remove(s.VoiceId);

		public ISoundSource AddSoundSourceFromMemory(byte[] data, int channels, int sampleBits, int sampleRate)
		{
			var id = WebAudioInterop.CreatePcmBuffer(data, channels, sampleBits, sampleRate);
			return new WebAudioSoundBuffer(id, sampleRate);
		}

		public ISound Play2D(ISoundSource soundSource, bool loop, bool relative, WPos pos, float vol, bool attenuateVolume)
		{
			if (soundSource == null)
			{
				Log.Write("sound", "Attempt to Play2D a null `ISoundSource`");
				return null;
			}

			if (soundSource is not WebAudioSoundBuffer wb)
				return null;

			var voiceId = WebAudioInterop.PlayVoice(wb.BufferId, loop, vol);
			if (voiceId == 0)
				return null;

			return new WebAudioSound(this, voiceId);
		}

		public ISound Play2DStream(Stream stream, int channels, int sampleBits, int sampleRate, bool loop, bool relative, WPos pos, float vol)
		{
			using (stream)
			{
				var ms = new MemoryStream();
				stream.CopyTo(ms);
				var data = ms.ToArray();
				var bufferId = WebAudioInterop.CreatePcmBuffer(data, channels, sampleBits, sampleRate);
				var voiceId = WebAudioInterop.PlayVoice(bufferId, loop, vol);
				if (voiceId == 0)
				{
					WebAudioInterop.DisposeBuffer(bufferId);
					return null;
				}

				return new WebAudioSound(this, voiceId, bufferId);
			}
		}

		public float Volume
		{
			get => volume;
			set => WebAudioInterop.SetMasterGain(volume = value);
		}

		public void PauseSound(ISound sound, bool paused)
		{
			if (sound is not WebAudioSound w || w.IsDetached)
				return;
			if (WebAudioInterop.GetVoiceComplete(w.VoiceId))
				return;
			WebAudioInterop.PauseVoice(w.VoiceId, paused);
		}

		public void SetAllSoundsPaused(bool paused)
		{
			foreach (var kv in voiceSounds)
				WebAudioInterop.PauseVoice(kv.Key, paused);
		}

		public void SetSoundVolume(float vol, ISound music, ISound video)
		{
			var mw = music as WebAudioSound;
			var vw = video as WebAudioSound;
			foreach (var kv in voiceSounds)
			{
				if (kv.Value == mw || kv.Value == vw)
					continue;
				WebAudioInterop.SetVoiceGain(kv.Key, vol);
			}
		}

		public void StopSound(ISound sound)
		{
			(sound as WebAudioSound)?.StopInternal();
		}

		public void StopAllSounds()
		{
			WebAudioInterop.StopAllVoices();
			foreach (var s in voiceSounds.Values)
				s.DetachFromEngineStopAll();
			voiceSounds.Clear();
		}

		public void SetListenerPosition(WPos position) { }

		public void SetSoundLooping(bool looping, ISound sound)
		{
			if (sound is not WebAudioSound w || w.IsDetached)
				return;
			if (WebAudioInterop.GetVoiceComplete(w.VoiceId))
				return;
			WebAudioInterop.SetVoiceLooping(w.VoiceId, looping);
		}

		public void SetSoundPosition(ISound sound, WPos position) { }

		public void Dispose()
		{
			WebAudioInterop.StopAllVoices();
			foreach (var s in voiceSounds.Values)
				s.DetachFromEngineStopAll();
			voiceSounds.Clear();
		}
	}
}
