// WebGL2 bridge — numeric IDs map to WebGL objects (no raw wasm heap).
let gl;
let canvas;

const buffers = new Map();
const textures = new Map();
const programs = new Map();
const shaders = new Map();
const framebuffers = new Map();
const renderbuffers = new Map();
const vaos = new Map();
const uniformLocs = new Map();
let nextId = 1;
function nid() { return nextId++; }
function get(m, id) { return id === 0 ? null : (m.get(id) ?? null); }

let currentFramebufferId = 0;

export function init(canvasId) {
  canvas = document.getElementById(canvasId);
  if (!canvas) return false;
  gl = canvas.getContext('webgl2', {
    alpha: false, antialias: false, premultipliedAlpha: false,
    preserveDrawingBuffer: true, stencil: true, depth: true
  });
  return gl != null;
}

export function resizeBackingStore(width, height) {
  if (!canvas || !gl) return;
  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }
  gl.viewport(0, 0, width, height);
}

export function glGetString(name) {
  try { const v = gl.getParameter(name); return v == null ? '' : String(v); }
  catch { return ''; }
}

export function glGetStringi(name, index) {
  try {
    if (name === 0x1F03) {
      const exts = gl.getSupportedExtensions() || [];
      return exts[index] || '';
    }
    const v = gl.getIndexedParameter(name, index);
    return v == null ? '' : String(v);
  } catch { return ''; }
}

export function glGetIntegerv(pname) {
  try { return gl.getParameter(pname) | 0; } catch { return 0; }
}

export function glGetError() {
  try { return gl.getError(); } catch { return 0; }
}

export function glEnable(x) { gl.enable(x); }
export function glDisable(x) { gl.disable(x); }
export function glViewport(x, y, w, h) { gl.viewport(x, y, w, h); }
export function glClear(mask) { gl.clear(mask); }
export function glClearColor(r, g, b, a) { gl.clearColor(r, g, b, a); }
export function glFlush() { gl.flush(); }
export function glFinish() { gl.finish(); }

export function genBuffer() {
  const b = gl.createBuffer();
  const id = nid();
  buffers.set(id, b);
  return id;
}

export function glBindBuffer(target, id) { gl.bindBuffer(target, get(buffers, id)); }

export function glBufferDataSized(target, size, usage) {
  gl.bufferData(target, size, usage);
}

export function glBufferData(target, usage, data) {
  gl.bufferData(target, data, usage);
}

export function glBufferSubData(target, offset, data) {
  gl.bufferSubData(target, offset, data);
}

export function glDeleteBuffer(id) {
  const b = buffers.get(id);
  if (b) gl.deleteBuffer(b);
  buffers.delete(id);
}

export function genVao() {
  const v = gl.createVertexArray();
  const id = nid();
  vaos.set(id, v);
  return id;
}

export function glBindVertexArray(id) { gl.bindVertexArray(get(vaos, id)); }

export function genTexture() {
  const t = gl.createTexture();
  const id = nid();
  textures.set(id, t);
  return id;
}

export function glDeleteTexture(id) {
  const t = textures.get(id);
  if (t) gl.deleteTexture(t);
  textures.delete(id);
}

export function glBindTexture(target, id) { gl.bindTexture(target, get(textures, id)); }
export function glActiveTexture(unit) { gl.activeTexture(unit); }
export function glTexParameteri(target, pname, param) { gl.texParameteri(target, pname, param); }
export function glTexParameterf(target, pname, param) { gl.texParameterf(target, pname, param); }

export function glTexImage2DEmpty(target, level, internalFormat, width, height, border, format, type) {
  gl.texImage2D(target, level, internalFormat, width, height, border, format, type, null);
}

export function glTexImage2D(target, level, internalFormat, width, height, border, format, type, data) {
  gl.texImage2D(target, level, internalFormat, width, height, border, format, type, data);
}

/** Float RGBA texels: C# marshals as byte[]; WebGL FLOAT requires a Float32Array. */
export function glTexImage2DFloatFromBytes(target, level, internalFormat, width, height, border, format, type, bytes) {
  const floatCount = width * height * 4;
  const u8 = bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
  const f32 = new Float32Array(floatCount);
  const dv = new DataView(u8.buffer, u8.byteOffset, u8.byteLength);
  const limit = Math.min(floatCount, u8.byteLength >> 2);
  for (let i = 0; i < limit; i++)
    f32[i] = dv.getFloat32(i * 4, true);
  gl.texImage2D(target, level, internalFormat, width, height, border, format, type, f32);
}

export function glCopyTexImage2D(target, level, internalFormat, x, y, width, height, border) {
  gl.copyTexImage2D(target, level, internalFormat, x, y, width, height, border);
}

export function glCreateShader(type) {
  const s = gl.createShader(type);
  const id = nid();
  shaders.set(id, s);
  return id;
}

export function glShaderSource(shaderId, source) {
  gl.shaderSource(get(shaders, shaderId), source);
}

export function glCompileShader(shaderId) { gl.compileShader(get(shaders, shaderId)); }

export function glGetShaderCompileStatus(shaderId) {
  return gl.getShaderParameter(get(shaders, shaderId), gl.COMPILE_STATUS) ? 1 : 0;
}

export function glGetShaderInfoLogLength(shaderId) {
  const log = gl.getShaderInfoLog(get(shaders, shaderId)) || '';
  return log.length + 1;
}

export function glGetShaderInfoLog(shaderId) {
  return gl.getShaderInfoLog(get(shaders, shaderId)) || '';
}

export function glCreateProgram() {
  const p = gl.createProgram();
  const id = nid();
  programs.set(id, p);
  return id;
}

export function glAttachShader(programId, shaderId) {
  gl.attachShader(get(programs, programId), get(shaders, shaderId));
}

export function glLinkProgram(programId) { gl.linkProgram(get(programs, programId)); }

export function glGetProgramLinkStatus(programId) {
  return gl.getProgramParameter(get(programs, programId), gl.LINK_STATUS) ? 1 : 0;
}

export function glGetProgramInfoLogLength(programId) {
  const log = gl.getProgramInfoLog(get(programs, programId)) || '';
  return log.length + 1;
}

export function glGetProgramInfoLog(programId) {
  return gl.getProgramInfoLog(get(programs, programId)) || '';
}

export function glGetActiveUniformCount(programId) {
  return gl.getProgramParameter(get(programs, programId), gl.ACTIVE_UNIFORMS);
}

export function glUseProgram(programId) { gl.useProgram(get(programs, programId)); }

export function glBindAttribLocation(programId, index, name) {
  gl.bindAttribLocation(get(programs, programId), index, name);
}

export function glGetUniformLocation(programId, name) {
  const loc = gl.getUniformLocation(get(programs, programId), name);
  if (!loc) return -1;
  const id = nid();
  uniformLocs.set(id, loc);
  return id;
}

export function glGetActiveUniformName(programId, index) {
  const info = gl.getActiveUniform(get(programs, programId), index);
  return info ? info.name : '';
}

export function glGetActiveUniformSize(programId, index) {
  const info = gl.getActiveUniform(get(programs, programId), index);
  return info ? info.size : 0;
}

export function glGetActiveUniformType(programId, index) {
  const info = gl.getActiveUniform(get(programs, programId), index);
  return info ? info.type : 0;
}

export function glUniform1i(locId, v) { gl.uniform1i(get(uniformLocs, locId), v); }
export function glUniform1f(locId, v) { gl.uniform1f(get(uniformLocs, locId), v); }
export function glUniform2f(locId, a, b) { gl.uniform2f(get(uniformLocs, locId), a, b); }
export function glUniform3f(locId, a, b, c) { gl.uniform3f(get(uniformLocs, locId), a, b, c); }

function floatsFromBytes(data) {
  const u8 = new Uint8Array(data);
  return new Float32Array(u8.buffer, u8.byteOffset, u8.byteLength / 4);
}

export function glUniform1fv(locId, data) {
  gl.uniform1fv(get(uniformLocs, locId), floatsFromBytes(data));
}
export function glUniform2fv(locId, data) {
  gl.uniform2fv(get(uniformLocs, locId), floatsFromBytes(data));
}
export function glUniform3fv(locId, data) {
  gl.uniform3fv(get(uniformLocs, locId), floatsFromBytes(data));
}
export function glUniform4fv(locId, data) {
  gl.uniform4fv(get(uniformLocs, locId), floatsFromBytes(data));
}
export function glUniformMatrix4fv(locId, transpose, data) {
  gl.uniformMatrix4fv(get(uniformLocs, locId), transpose, floatsFromBytes(data));
}

export function glDrawArrays(mode, first, count) { gl.drawArrays(mode, first, count); }
export function glDrawElements(mode, count, type, offset) {
  gl.drawElements(mode, count, type, offset);
}

export function glVertexAttribPointer(index, size, type, normalized, stride, offset) {
  gl.vertexAttribPointer(index, size, type, normalized, stride, offset);
}

export function glVertexAttribIPointer(index, size, type, stride, offset) {
  gl.vertexAttribIPointer(index, size, type, stride, offset);
}

export function glEnableVertexAttribArray(i) { gl.enableVertexAttribArray(i); }
export function glDisableVertexAttribArray(i) { gl.disableVertexAttribArray(i); }

export function glBlendEquation(m) { gl.blendEquation(m); }
export function glBlendEquationSeparate(a, b) { gl.blendEquationSeparate(a, b); }
export function glBlendFunc(a, b) { gl.blendFunc(a, b); }
export function glDepthFunc(f) { gl.depthFunc(f); }
export function glScissor(x, y, w, h) { gl.scissor(x, y, w, h); }

export function glReadPixelsTo(x, y, w, h, format, type) {
  const row = w * 4;
  const out = new Uint8Array(row * h);
  gl.readPixels(x, y, w, h, format, type, out);
  return out;
}

export function genFramebuffer() {
  const f = gl.createFramebuffer();
  const id = nid();
  framebuffers.set(id, f);
  return id;
}

export function glDeleteFramebuffer(id) {
  const f = framebuffers.get(id);
  if (f) gl.deleteFramebuffer(f);
  framebuffers.delete(id);
}

export function glBindFramebuffer(target, id) {
  gl.bindFramebuffer(target, get(framebuffers, id));
  if (target === 0x8D40)
    currentFramebufferId = id;
}

export function glGetFramebufferBindingId() { return currentFramebufferId; }

export function glFramebufferTexture2D(target, attachment, textarget, texId, level) {
  gl.framebufferTexture2D(target, attachment, textarget, get(textures, texId), level);
}

export function glCheckFramebufferStatus(target) { return gl.checkFramebufferStatus(target); }

export function genRenderbuffer() {
  const r = gl.createRenderbuffer();
  const id = nid();
  renderbuffers.set(id, r);
  return id;
}

export function glDeleteRenderbuffer(id) {
  const r = renderbuffers.get(id);
  if (r) gl.deleteRenderbuffer(r);
  renderbuffers.delete(id);
}

export function glBindRenderbuffer(target, id) { gl.bindRenderbuffer(target, get(renderbuffers, id)); }

export function glRenderbufferStorage(target, fmt, w, h) {
  gl.renderbufferStorage(target, fmt, w, h);
}

export function glFramebufferRenderbuffer(target, attachment, rbTarget, rbId) {
  gl.framebufferRenderbuffer(target, attachment, rbTarget, get(renderbuffers, rbId));
}

export function glIsTexture(id) { return gl.isTexture(get(textures, id)); }

export function getExtensionCount() {
  return (gl.getSupportedExtensions() || []).length;
}

export function glGetViewportArray() {
  const v = gl.getParameter(gl.VIEWPORT);
  return new Int32Array([
    Math.round(v[0]), Math.round(v[1]), Math.round(v[2]), Math.round(v[3])
  ]);
}
