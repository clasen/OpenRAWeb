// Web Audio bridge for OpenRA browser build — PCM buffers + voices (interop from WebAudioInterop).
// AudioContext MUST be created in a user gesture (see browser-openra.js). Until then we only queue raw PCM and pending plays.

let audioCtx = null;
let masterGain = null;
let pendingMasterGain = 1;
let nextBufferId = 1;
let nextVoiceId = 1;
/** @type {Map<number, { bytes: Uint8Array, channels: number, sampleBits: number, sampleRate: number }>} */
const rawBuffers = new Map();
/** @type {Map<number, AudioBuffer>} */
const buffers = new Map();
/** @type {Map<number, object>} */
const voices = new Map();

function ensureMasterGain() {
  if (!audioCtx || masterGain) return;
  masterGain = audioCtx.createGain();
  masterGain.gain.value = Math.max(0, Math.min(1, pendingMasterGain));
  masterGain.connect(audioCtx.destination);
}

function ensureRunningFlushHook(ctx) {
  if (!ctx || ctx.__openraRunningFlush) return;
  ctx.__openraRunningFlush = true;
  ctx.addEventListener('statechange', () => {
    if (ctx.state === 'running') flushAfterUnlock();
  });
}

function attachContext(ctx) {
  if (!ctx || audioCtx === ctx) {
    if (ctx) {
      audioCtx = ctx;
      ensureRunningFlushHook(ctx);
      ensureMasterGain();
      flushAfterUnlock();
    }
    return;
  }
  audioCtx = ctx;
  ensureRunningFlushHook(ctx);
  ensureMasterGain();
  flushAfterUnlock();
}

function flushAfterUnlock() {
  if (!audioCtx) return;
  for (const id of [...rawBuffers.keys()])
    tryRealizeBuffer(id);
  if (masterGain)
    masterGain.gain.value = Math.max(0, Math.min(1, pendingMasterGain));
  for (const [vid, v] of [...voices]) {
    if (v.pendingPlay && !v.stopped)
      activatePendingVoice(vid, v);
  }
}

window.__openraOnAudioUnlocked = function (ctx) {
  attachContext(ctx);
};

if (window.__openraAudioCtx)
  attachContext(window.__openraAudioCtx);

function tryRealizeBuffer(id) {
  if (!audioCtx || !rawBuffers.has(id)) return;
  const r = rawBuffers.get(id);
  const buf = pcmToAudioBuffer(audioCtx, r.bytes, r.channels, r.sampleBits, r.sampleRate);
  rawBuffers.delete(id);
  buffers.set(id, buf);
}

function pcmToAudioBuffer(ctx, bytes, channels, sampleBits, sampleRate) {
  const b = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  const bytesPerSample = sampleBits / 8;
  const bytesPerFrame = channels * bytesPerSample;
  const samples = Math.floor(b.byteLength / bytesPerFrame);
  const buf = ctx.createBuffer(channels, samples, sampleRate);
  if (sampleBits === 16) {
    const dv = new DataView(b.buffer, b.byteOffset, b.byteLength);
    for (let c = 0; c < channels; c++) {
      const ch = buf.getChannelData(c);
      for (let i = 0; i < samples; i++)
        ch[i] = dv.getInt16((i * channels + c) * 2, true) / 32768;
    }
  } else if (sampleBits === 8) {
    for (let c = 0; c < channels; c++) {
      const ch = buf.getChannelData(c);
      for (let i = 0; i < samples; i++) {
        const u = b[i * channels + c];
        ch[i] = (u - 128) / 128;
      }
    }
  } else {
    throw new Error('openra-audio: unsupported sampleBits ' + sampleBits);
  }
  return buf;
}

function stopVoiceInternal(vid) {
  const v = voices.get(vid);
  if (!v) return;
  try {
    if (v.source) v.source.stop(0);
  } catch (_) { /* already stopped */ }
  v.source = null;
  v.stopped = true;
  v.complete = !v.loop;
}

function startPlayingVoice(bufferId, loop, volume, audioBuffer) {
  ensureMasterGain();
  const ctx = audioCtx;
  const source = ctx.createBufferSource();
  const gain = ctx.createGain();
  gain.gain.value = Math.max(0, Math.min(1, volume));
  source.buffer = audioBuffer;
  source.loop = loop;
  source.connect(gain);
  gain.connect(masterGain);

  const vid = nextVoiceId++;
  const startedAt = ctx.currentTime;
  const state = {
    pendingPlay: false,
    bufferId,
    buffer: audioBuffer,
    source,
    gainNode: gain,
    loop,
    startedAt,
    offset: 0,
    paused: false,
    stopped: false,
    complete: false,
    manualComplete: false
  };
  source.onended = () => {
    if (state.stopped || state.paused) return;
    if (!state.loop) {
      state.complete = true;
      state.source = null;
      voices.delete(vid);
    }
  };
  source.start(startedAt);
  voices.set(vid, state);
  return vid;
}

function activatePendingVoice(vid, v) {
  if (v.stopped) return;
  tryRealizeBuffer(v.bufferId);
  const audioBuffer = buffers.get(v.bufferId);
  if (!audioBuffer || !audioCtx) return;
  ensureMasterGain();
  const ctx = audioCtx;
  v.pendingPlay = false;
  v.buffer = audioBuffer;

  if (v.paused) {
    const gain = ctx.createGain();
    gain.gain.value = Math.max(0, Math.min(1, v.volume));
    gain.connect(masterGain);
    v.gainNode = gain;
    v.source = null;
    v.offset = 0;
    v.startedAt = ctx.currentTime;
    return;
  }

  const source = ctx.createBufferSource();
  const gain = ctx.createGain();
  gain.gain.value = Math.max(0, Math.min(1, v.volume));
  source.buffer = audioBuffer;
  source.loop = v.loop;
  source.connect(gain);
  gain.connect(masterGain);
  v.source = source;
  v.gainNode = gain;
  v.startedAt = ctx.currentTime;
  v.offset = 0;
  source.onended = () => {
    if (v.stopped || v.paused) return;
    if (!v.loop) {
      v.complete = true;
      v.source = null;
      voices.delete(vid);
    }
  };
  source.start(v.startedAt);
}

function pauseVoiceImpl(voiceId, v, paused) {
  if (!v || v.stopped || v.pendingPlay) return;
  const ctx = audioCtx;
  if (!ctx) return;

  if (paused) {
    if (v.paused) return;
    let pos = v.offset;
    if (v.source) {
      try {
        pos += ctx.currentTime - v.startedAt;
      } catch (_) { }
      try { v.source.stop(0); } catch (_) { }
      v.source = null;
    }
    const dur = v.buffer.duration;
    if (!v.loop && pos >= dur - 0.001)
      v.complete = true;
    else
      v.offset = Math.min(pos, dur);
    v.paused = true;
  } else {
    if (!v.paused) return;
    if (v.complete && !v.loop) return;
    v.paused = false;
    const source = ctx.createBufferSource();
    const gain = v.gainNode;
    source.buffer = v.buffer;
    source.loop = v.loop;
    source.connect(gain);
    v.source = source;
    v.startedAt = ctx.currentTime;
    source.onended = () => {
      if (v.stopped || v.paused) return;
      if (!v.loop) {
        v.complete = true;
        v.source = null;
        voices.delete(voiceId);
      }
    };
    const off = Math.max(0, v.offset);
    source.start(v.startedAt, off);
    v.offset = off;
  }
}

export function setMasterGain(v) {
  pendingMasterGain = v;
  if (masterGain)
    masterGain.gain.value = Math.max(0, Math.min(1, v));
}

export function createPcmBuffer(data, channels, sampleBits, sampleRate) {
  const id = nextBufferId++;
  const u8 = data instanceof Uint8Array ? new Uint8Array(data) : new Uint8Array(data);
  rawBuffers.set(id, { bytes: u8, channels, sampleBits, sampleRate });
  if (audioCtx)
    tryRealizeBuffer(id);
  return id;
}

export function disposeBuffer(bufferId) {
  if (!bufferId) return;
  for (const [vid, v] of [...voices]) {
    if (v.bufferId === bufferId) {
      stopVoiceInternal(vid);
      voices.delete(vid);
    }
  }
  rawBuffers.delete(bufferId);
  buffers.delete(bufferId);
}

export function playVoice(bufferId, loop, volume) {
  if (!audioCtx) {
    const vid = nextVoiceId++;
    voices.set(vid, {
      pendingPlay: true,
      bufferId,
      loop,
      volume,
      stopped: false,
      manualComplete: false,
      complete: false,
      paused: false
    });
    return vid;
  }

  tryRealizeBuffer(bufferId);
  const audioBuffer = buffers.get(bufferId);
  if (!audioBuffer) return 0;

  if (audioCtx.state !== 'running') {
    const vid = nextVoiceId++;
    voices.set(vid, {
      pendingPlay: true,
      bufferId,
      loop,
      volume,
      stopped: false,
      manualComplete: false,
      complete: false,
      paused: false
    });
    const ctx = audioCtx;
    try {
      const pr = ctx.resume();
      if (pr !== undefined && typeof pr.then === 'function')
        pr.then(() => flushAfterUnlock(), () => {});
    } catch (_) { /* resume may throw outside a gesture */ }
    const once = () => {
      ctx.removeEventListener('statechange', once);
      if (ctx.state === 'running')
        flushAfterUnlock();
    };
    ctx.addEventListener('statechange', once);
    return vid;
  }

  return startPlayingVoice(bufferId, loop, volume, audioBuffer);
}

export function stopVoice(voiceId) {
  if (!voiceId) return;
  const v = voices.get(voiceId);
  if (!v) return;
  v.manualComplete = true;
  if (v.pendingPlay) {
    v.stopped = true;
    voices.delete(voiceId);
    return;
  }
  stopVoiceInternal(voiceId);
  voices.delete(voiceId);
}

export function stopAllVoices() {
  for (const vid of [...voices.keys()])
    stopVoice(vid);
}

export function pauseVoice(voiceId, paused) {
  const v = voices.get(voiceId);
  if (!v || v.stopped) return;
  if (v.pendingPlay) {
    v.paused = paused;
    return;
  }
  pauseVoiceImpl(voiceId, v, paused);
}

export function setVoiceGain(voiceId, gain) {
  const v = voices.get(voiceId);
  if (!v) return;
  const g = Math.max(0, Math.min(1, gain));
  if (v.pendingPlay) {
    v.volume = g;
    return;
  }
  if (v.gainNode) v.gainNode.gain.value = g;
}

export function setVoiceLooping(voiceId, loop) {
  const v = voices.get(voiceId);
  if (!v) return;
  v.loop = loop;
  if (v.source) v.source.loop = loop;
}

export function getVoiceGain(voiceId) {
  const v = voices.get(voiceId);
  if (!v) return 0;
  if (v.pendingPlay) return v.volume;
  if (!v.gainNode) return 0;
  return v.gainNode.gain.value;
}

export function getVoiceComplete(voiceId) {
  const v = voices.get(voiceId);
  if (!v) return true;
  if (v.manualComplete) return true;
  return v.complete;
}

export function getVoiceSeekSeconds(voiceId) {
  const v = voices.get(voiceId);
  if (!v) return 0;
  if (v.pendingPlay) return 0;
  const ctx = audioCtx;
  if (!ctx) return 0;
  if (v.paused || !v.source)
    return v.offset;
  let t = v.offset + (ctx.currentTime - v.startedAt);
  const dur = v.buffer.duration;
  if (!v.loop && t > dur) t = dur;
  return t;
}
