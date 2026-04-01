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
using System.Runtime.InteropServices;
using System.Text;

namespace OpenRA.Platforms.Browser
{
	static partial class OpenGL
	{
		static byte[] FloatsToBytes(float[] floats)
		{
			var bytes = new byte[floats.Length * 4];
			Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
			return bytes;
		}

		public static void Initialize()
		{
			try
			{
				glEnable = cap => WebGLInterop.GlEnable(cap);
				glDisable = cap => WebGLInterop.GlDisable(cap);
				glGetError = () => WebGLInterop.GlGetError();
				glGetIntegerv = delegate(int pname, out int param)
				{
					if (pname == GL_FRAMEBUFFER_BINDING)
						param = WebGLInterop.GlGetFramebufferBindingId();
					else if (pname == GL_NUM_EXTENSIONS)
						param = WebGLInterop.GetExtensionCount();
					else
						param = WebGLInterop.GlGetIntegerParam(pname);
					return 0;
				};
			}
			catch (Exception e)
			{
				throw new InvalidProgramException("Failed to initialize WebGL bindings.", e);
			}

			if (!DetectGLFeatures())
			{
				WriteGraphicsLog("Unsupported WebGL: " + glGetString(GL_VERSION));
				throw new InvalidProgramException("WebGL Version Error: See graphics.log for details.");
			}

			glDebugMessageCallback = null;
			glDebugMessageInsert = null;

			Console.WriteLine("WebGL renderer: " + glGetString(GL_RENDERER));
			Console.WriteLine("WebGL version: " + glGetString(GL_VERSION));

			try
			{
				glFlush = WebGLInterop.GlFlush;
				glViewport = WebGLInterop.GlViewport;
				glClear = WebGLInterop.GlClear;
				glClearColor = WebGLInterop.GlClearColor;
				glFinish = WebGLInterop.GlFinish;

				glCreateProgram = () => (uint)WebGLInterop.GlCreateProgram();
				glUseProgram = p => WebGLInterop.GlUseProgram((int)p);
				glGetProgramiv = delegate(uint program, int pname, out int param)
				{
					if (pname == GL_LINK_STATUS)
						param = WebGLInterop.GlGetProgramLinkStatus((int)program);
					else if (pname == GL_INFO_LOG_LENGTH)
						param = WebGLInterop.GlGetProgramInfoLogLength((int)program);
					else if (pname == GL_ACTIVE_UNIFORMS)
						param = WebGLInterop.GlGetActiveUniformCount((int)program);
					else
						param = 0;
				};

				glCreateShader = t => (uint)WebGLInterop.GlCreateShader(t);
				glShaderSource = (shader, count, str, _) =>
				{
					if (count != 1 || str == null || str.Length < 1)
						throw new NotSupportedException("Browser WebGL: multi-string shader source not supported.");
					WebGLInterop.GlShaderSource((int)shader, str[0]);
				};
				glCompileShader = s => WebGLInterop.GlCompileShader((int)s);
				glGetShaderiv = delegate(uint shader, int pname, out int param)
				{
					if (pname == GL_COMPILE_STATUS)
						param = WebGLInterop.GlGetShaderCompileStatus((int)shader);
					else if (pname == GL_INFO_LOG_LENGTH)
						param = WebGLInterop.GlGetShaderInfoLogLength((int)shader);
					else
						param = 0;
					return 0;
				};
				glAttachShader = (p, s) => WebGLInterop.GlAttachShader((int)p, (int)s);
				glGetShaderInfoLog = delegate(uint shader, int maxLength, out int length, StringBuilder infoLog)
				{
					var log = WebGLInterop.GlGetShaderInfoLog((int)shader);
					infoLog.Clear();
					infoLog.Append(log);
					length = log.Length;
				};
				glLinkProgram = p => WebGLInterop.GlLinkProgram((int)p);
				glGetProgramInfoLog = delegate(uint program, int maxLength, out int length, StringBuilder infoLog)
				{
					var log = WebGLInterop.GlGetProgramInfoLog((int)program);
					infoLog.Clear();
					infoLog.Append(log);
					length = log.Length;
				};
				glGetUniformLocation = (program, name) => WebGLInterop.GlGetUniformLocation((int)program, name);
				glGetActiveUniform = delegate(uint program, int index, int bufSize, out int length, out int size, out int type, StringBuilder name)
				{
					var n = WebGLInterop.GlGetActiveUniformName((int)program, index);
					name.Clear();
					name.Append(n);
					length = Math.Min(n.Length, bufSize - 1);
					size = WebGLInterop.GlGetActiveUniformSize((int)program, index);
					type = WebGLInterop.GlGetActiveUniformType((int)program, index);
				};

				glUniform1i = (loc, v) => WebGLInterop.GlUniform1i(loc, v);
				glUniform1f = (loc, v) => WebGLInterop.GlUniform1f(loc, v);
				glUniform2f = WebGLInterop.GlUniform2f;
				glUniform3f = WebGLInterop.GlUniform3f;
				glUniform1fv = (loc, count, ptr) =>
				{
					var f = new float[count];
					Marshal.Copy(ptr, f, 0, count);
					WebGLInterop.GlUniform1fv(loc, FloatsToBytes(f));
				};
				glUniform2fv = (loc, count, ptr) =>
				{
					var f = new float[count * 2];
					Marshal.Copy(ptr, f, 0, f.Length);
					WebGLInterop.GlUniform2fv(loc, FloatsToBytes(f));
				};
				glUniform3fv = (loc, count, ptr) =>
				{
					var f = new float[count * 3];
					Marshal.Copy(ptr, f, 0, f.Length);
					WebGLInterop.GlUniform3fv(loc, FloatsToBytes(f));
				};
				glUniform4fv = (loc, count, ptr) =>
				{
					var f = new float[count * 4];
					Marshal.Copy(ptr, f, 0, f.Length);
					WebGLInterop.GlUniform4fv(loc, FloatsToBytes(f));
				};
				glUniformMatrix4fv = (loc, count, transpose, ptr) =>
				{
					var f = new float[count * 16];
					Marshal.Copy(ptr, f, 0, f.Length);
					WebGLInterop.GlUniformMatrix4fv(loc, transpose, FloatsToBytes(f));
				};

				glGenBuffers = delegate(int n, out uint buffers)
				{
					if (n != 1)
						throw new NotSupportedException();
					buffers = (uint)WebGLInterop.GenBuffer();
				};
				glBindBuffer = (t, b) => WebGLInterop.GlBindBuffer(t, (int)b);
				glBufferData = (target, size, data, usage) =>
				{
					if (data == IntPtr.Zero)
						WebGLInterop.GlBufferDataSized(target, size.ToInt32(), usage);
					else
					{
						var n = size.ToInt32();
						var arr = new byte[n];
						Marshal.Copy(data, arr, 0, n);
						WebGLInterop.GlBufferData(target, usage, arr);
					}
				};
				glBufferSubData = (target, offset, size, data) =>
				{
					var n = size.ToInt32();
					var arr = new byte[n];
					Marshal.Copy(data, arr, 0, n);
					WebGLInterop.GlBufferSubData(target, offset.ToInt32(), arr);
				};
				glDeleteBuffers = delegate(int n, ref uint buffers)
				{
					if (n != 1)
						throw new NotSupportedException();
					WebGLInterop.GlDeleteBuffer((int)buffers);
				};

				glBindAttribLocation = (program, index, name) => WebGLInterop.GlBindAttribLocation((int)program, index, name);
				glVertexAttribPointer = (index, size, type, normalized, stride, pointer) =>
					WebGLInterop.GlVertexAttribPointer(index, size, type, normalized, stride, pointer.ToInt32());
				glVertexAttribIPointer = (index, size, type, stride, pointer) =>
					WebGLInterop.GlVertexAttribIPointer(index, size, type, stride, pointer.ToInt32());
				glEnableVertexAttribArray = WebGLInterop.GlEnableVertexAttribArray;
				glDisableVertexAttribArray = WebGLInterop.GlDisableVertexAttribArray;
				glDrawArrays = WebGLInterop.GlDrawArrays;
				glDrawElements = (mode, count, type, indices) =>
					WebGLInterop.GlDrawElements(mode, count, type, indices.ToInt32());

				glBlendEquation = WebGLInterop.GlBlendEquation;
				glBlendEquationSeparate = WebGLInterop.GlBlendEquationSeparate;
				glBlendFunc = WebGLInterop.GlBlendFunc;
				glDepthFunc = WebGLInterop.GlDepthFunc;
				glScissor = WebGLInterop.GlScissor;

				glReadPixels = (x, y, w, h, format, type, ptr) =>
				{
					var bytes = WebGLInterop.GlReadPixelsTo(x, y, w, h, format, type);
					if (bytes != null && bytes.Length > 0)
						Marshal.Copy(bytes, 0, ptr, bytes.Length);
				};

				glGenTextures = delegate(int n, out uint tex)
				{
					if (n != 1)
						throw new NotSupportedException();
					tex = (uint)WebGLInterop.GenTexture();
				};
				glDeleteTextures = delegate(int n, ref uint tex)
				{
					if (n != 1)
						throw new NotSupportedException();
					WebGLInterop.GlDeleteTexture((int)tex);
				};
				glIsTexture = t => WebGLInterop.GlIsTexture((int)t);
				glBindTexture = (target, tex) => WebGLInterop.GlBindTexture(target, (int)tex);
				glActiveTexture = WebGLInterop.GlActiveTexture;
				glTexImage2D = (target, level, internalFormat, width, height, border, format, type, pixels) =>
				{
					if (pixels == IntPtr.Zero)
						WebGLInterop.GlTexImage2DEmpty(target, level, internalFormat, width, height, border, format, type);
					else if (type == GL_FLOAT)
					{
						var byteCount = width * height * 4 * sizeof(float);
						var arr = new byte[byteCount];
						Marshal.Copy(pixels, arr, 0, byteCount);
						WebGLInterop.GlTexImage2DFloatFromBytes(target, level, internalFormat, width, height, border, format, type, arr);
					}
					else
					{
						var bpp = 4;
						var n = width * height * bpp;
						var arr = new byte[n];
						Marshal.Copy(pixels, arr, 0, n);
						WebGLInterop.GlTexImage2D(target, level, internalFormat, width, height, border, format, type, arr);
					}
				};
				glCopyTexImage2D = WebGLInterop.GlCopyTexImage2D;
				glTexParameteri = WebGLInterop.GlTexParameteri;
				glTexParameterf = WebGLInterop.GlTexParameterf;

				glGetTexImage = null;
				glBindFragDataLocation = null;

				glGenVertexArrays = delegate(int n, out uint arrays)
				{
					if (n != 1)
						throw new NotSupportedException();
					arrays = (uint)WebGLInterop.GenVao();
				};
				glBindVertexArray = id => WebGLInterop.GlBindVertexArray((int)id);
				glGenFramebuffers = delegate(int n, out uint fb)
				{
					if (n != 1)
						throw new NotSupportedException();
					fb = (uint)WebGLInterop.GenFramebuffer();
				};
				glBindFramebuffer = (t, id) => WebGLInterop.GlBindFramebuffer(t, (int)id);
				glFramebufferTexture2D = (target, attachment, textarget, tex, level) =>
					WebGLInterop.GlFramebufferTexture2D(target, attachment, textarget, (int)tex, level);
				glDeleteFramebuffers = delegate(int n, ref uint fb)
				{
					if (n != 1)
						throw new NotSupportedException();
					WebGLInterop.GlDeleteFramebuffer((int)fb);
				};
				glGenRenderbuffers = delegate(int n, out uint rb)
				{
					if (n != 1)
						throw new NotSupportedException();
					rb = (uint)WebGLInterop.GenRenderbuffer();
				};
				glBindRenderbuffer = (t, id) => WebGLInterop.GlBindRenderbuffer(t, (int)id);
				glRenderbufferStorage = WebGLInterop.GlRenderbufferStorage;
				glDeleteRenderbuffers = delegate(int n, ref uint rb)
				{
					if (n != 1)
						throw new NotSupportedException();
					WebGLInterop.GlDeleteRenderbuffer((int)rb);
				};
				glFramebufferRenderbuffer = (target, attachment, rbTarget, rb) =>
					WebGLInterop.GlFramebufferRenderbuffer(target, attachment, rbTarget, (int)rb);
				glCheckFramebufferStatus = WebGLInterop.GlCheckFramebufferStatus;
			}
			catch (Exception e)
			{
				WriteGraphicsLog($"Failed to initialize WebGL bindings.\nInner exception was: {e}");
				throw new InvalidProgramException("Failed to initialize WebGL. See graphics.log for details.", e);
			}
		}

		public static bool DetectGLFeatures()
		{
			try
			{
				Version = glGetString(GL_VERSION);
				Profile = GLProfile.Embedded;
				Features = GLFeatures.ESReadFormatBGRA;
				return !string.IsNullOrEmpty(Version) && Version.Contains("WebGL", StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}
	}
}
