#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version.
 */
#endregion

using System.Runtime.InteropServices.JavaScript;

[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]

namespace OpenRA.Platforms.Browser
{
	internal static partial class WebGLInterop
	{
		const string Module = "js/openra-gl.js";

		[JSImport("init", Module)]
		internal static partial bool Init(string canvasId);

		[JSImport("resizeBackingStore", Module)]
		internal static partial void ResizeBackingStore(int width, int height);

		[JSImport("glGetString", Module)]
		internal static partial string GlGetString(int name);

		[JSImport("glGetStringi", Module)]
		internal static partial string GlGetStringi(int name, int index);

		[JSImport("glGetIntegerv", Module)]
		internal static partial int GlGetIntegerParam(int pname);

		[JSImport("glGetError", Module)]
		internal static partial int GlGetError();

		[JSImport("glEnable", Module)]
		internal static partial void GlEnable(int cap);

		[JSImport("glDisable", Module)]
		internal static partial void GlDisable(int cap);

		[JSImport("glViewport", Module)]
		internal static partial void GlViewport(int x, int y, int w, int h);

		[JSImport("glClear", Module)]
		internal static partial void GlClear(int mask);

		[JSImport("glClearColor", Module)]
		internal static partial void GlClearColor(float r, float g, float b, float a);

		[JSImport("glFlush", Module)]
		internal static partial void GlFlush();

		[JSImport("glFinish", Module)]
		internal static partial void GlFinish();

		[JSImport("genBuffer", Module)]
		internal static partial int GenBuffer();

		[JSImport("glBindBuffer", Module)]
		internal static partial void GlBindBuffer(int target, int id);

		[JSImport("glBufferDataSized", Module)]
		internal static partial void GlBufferDataSized(int target, int size, int usage);

		[JSImport("glBufferData", Module)]
		internal static partial void GlBufferData(int target, int usage, byte[] data);

		[JSImport("glBufferSubData", Module)]
		internal static partial void GlBufferSubData(int target, int offset, byte[] data);

		[JSImport("glDeleteBuffer", Module)]
		internal static partial void GlDeleteBuffer(int id);

		[JSImport("genVao", Module)]
		internal static partial int GenVao();

		[JSImport("glBindVertexArray", Module)]
		internal static partial void GlBindVertexArray(int id);

		[JSImport("genTexture", Module)]
		internal static partial int GenTexture();

		[JSImport("glDeleteTexture", Module)]
		internal static partial void GlDeleteTexture(int id);

		[JSImport("glBindTexture", Module)]
		internal static partial void GlBindTexture(int target, int id);

		[JSImport("glActiveTexture", Module)]
		internal static partial void GlActiveTexture(int unit);

		[JSImport("glTexParameteri", Module)]
		internal static partial void GlTexParameteri(int target, int pname, int param);

		[JSImport("glTexParameterf", Module)]
		internal static partial void GlTexParameterf(int target, int pname, float param);

		[JSImport("glTexImage2DEmpty", Module)]
		internal static partial void GlTexImage2DEmpty(int target, int level, int internalFormat, int width, int height, int border, int format, int type);

		[JSImport("glTexImage2D", Module)]
		internal static partial void GlTexImage2D(int target, int level, int internalFormat, int width, int height, int border, int format, int type, byte[] data);

		[JSImport("glTexImage2DFloatFromBytes", Module)]
		internal static partial void GlTexImage2DFloatFromBytes(
			int target,
			int level,
			int internalFormat,
			int width,
			int height,
			int border,
			int format,
			int type,
			byte[] data);

		[JSImport("glCopyTexImage2D", Module)]
		internal static partial void GlCopyTexImage2D(int target, int level, int internalFormat, int x, int y, int width, int height, int border);

		[JSImport("glCreateShader", Module)]
		internal static partial int GlCreateShader(int type);

		[JSImport("glShaderSource", Module)]
		internal static partial void GlShaderSource(int shaderId, string source);

		[JSImport("glCompileShader", Module)]
		internal static partial void GlCompileShader(int shaderId);

		[JSImport("glGetShaderCompileStatus", Module)]
		internal static partial int GlGetShaderCompileStatus(int shaderId);

		[JSImport("glGetShaderInfoLogLength", Module)]
		internal static partial int GlGetShaderInfoLogLength(int shaderId);

		[JSImport("glGetShaderInfoLog", Module)]
		internal static partial string GlGetShaderInfoLog(int shaderId);

		[JSImport("glCreateProgram", Module)]
		internal static partial int GlCreateProgram();

		[JSImport("glAttachShader", Module)]
		internal static partial void GlAttachShader(int programId, int shaderId);

		[JSImport("glLinkProgram", Module)]
		internal static partial void GlLinkProgram(int programId);

		[JSImport("glGetProgramLinkStatus", Module)]
		internal static partial int GlGetProgramLinkStatus(int programId);

		[JSImport("glGetProgramInfoLogLength", Module)]
		internal static partial int GlGetProgramInfoLogLength(int programId);

		[JSImport("glGetProgramInfoLog", Module)]
		internal static partial string GlGetProgramInfoLog(int programId);

		[JSImport("glGetActiveUniformCount", Module)]
		internal static partial int GlGetActiveUniformCount(int programId);

		[JSImport("glUseProgram", Module)]
		internal static partial void GlUseProgram(int programId);

		[JSImport("glBindAttribLocation", Module)]
		internal static partial void GlBindAttribLocation(int programId, int index, string name);

		[JSImport("glGetUniformLocation", Module)]
		internal static partial int GlGetUniformLocation(int programId, string name);

		[JSImport("glGetActiveUniformName", Module)]
		internal static partial string GlGetActiveUniformName(int programId, int index);

		[JSImport("glGetActiveUniformSize", Module)]
		internal static partial int GlGetActiveUniformSize(int programId, int index);

		[JSImport("glGetActiveUniformType", Module)]
		internal static partial int GlGetActiveUniformType(int programId, int index);

		[JSImport("glUniform1i", Module)]
		internal static partial void GlUniform1i(int locId, int v);

		[JSImport("glUniform1f", Module)]
		internal static partial void GlUniform1f(int locId, float v);

		[JSImport("glUniform2f", Module)]
		internal static partial void GlUniform2f(int locId, float a, float b);

		[JSImport("glUniform3f", Module)]
		internal static partial void GlUniform3f(int locId, float a, float b, float c);

		[JSImport("glUniform1fv", Module)]
		internal static partial void GlUniform1fv(int locId, byte[] data);

		[JSImport("glUniform2fv", Module)]
		internal static partial void GlUniform2fv(int locId, byte[] data);

		[JSImport("glUniform3fv", Module)]
		internal static partial void GlUniform3fv(int locId, byte[] data);

		[JSImport("glUniform4fv", Module)]
		internal static partial void GlUniform4fv(int locId, byte[] data);

		[JSImport("glUniformMatrix4fv", Module)]
		internal static partial void GlUniformMatrix4fv(int locId, bool transpose, byte[] data);

		[JSImport("glDrawArrays", Module)]
		internal static partial void GlDrawArrays(int mode, int first, int count);

		[JSImport("glDrawElements", Module)]
		internal static partial void GlDrawElements(int mode, int count, int type, int offset);

		[JSImport("glVertexAttribPointer", Module)]
		internal static partial void GlVertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset);

		[JSImport("glVertexAttribIPointer", Module)]
		internal static partial void GlVertexAttribIPointer(int index, int size, int type, int stride, int offset);

		[JSImport("glEnableVertexAttribArray", Module)]
		internal static partial void GlEnableVertexAttribArray(int index);

		[JSImport("glDisableVertexAttribArray", Module)]
		internal static partial void GlDisableVertexAttribArray(int index);

		[JSImport("glBlendEquation", Module)]
		internal static partial void GlBlendEquation(int mode);

		[JSImport("glBlendEquationSeparate", Module)]
		internal static partial void GlBlendEquationSeparate(int a, int b);

		[JSImport("glBlendFunc", Module)]
		internal static partial void GlBlendFunc(int a, int b);

		[JSImport("glDepthFunc", Module)]
		internal static partial void GlDepthFunc(int func);

		[JSImport("glScissor", Module)]
		internal static partial void GlScissor(int x, int y, int w, int h);

		[JSImport("glReadPixelsTo", Module)]
		internal static partial byte[] GlReadPixelsTo(int x, int y, int w, int h, int format, int type);

		[JSImport("genFramebuffer", Module)]
		internal static partial int GenFramebuffer();

		[JSImport("glDeleteFramebuffer", Module)]
		internal static partial void GlDeleteFramebuffer(int id);

		[JSImport("glBindFramebuffer", Module)]
		internal static partial void GlBindFramebuffer(int target, int id);

		[JSImport("glFramebufferTexture2D", Module)]
		internal static partial void GlFramebufferTexture2D(int target, int attachment, int textarget, int texId, int level);

		[JSImport("glCheckFramebufferStatus", Module)]
		internal static partial int GlCheckFramebufferStatus(int target);

		[JSImport("genRenderbuffer", Module)]
		internal static partial int GenRenderbuffer();

		[JSImport("glDeleteRenderbuffer", Module)]
		internal static partial void GlDeleteRenderbuffer(int id);

		[JSImport("glBindRenderbuffer", Module)]
		internal static partial void GlBindRenderbuffer(int target, int id);

		[JSImport("glRenderbufferStorage", Module)]
		internal static partial void GlRenderbufferStorage(int target, int fmt, int w, int h);

		[JSImport("glFramebufferRenderbuffer", Module)]
		internal static partial void GlFramebufferRenderbuffer(int target, int attachment, int rbTarget, int rbId);

		[JSImport("glIsTexture", Module)]
		internal static partial bool GlIsTexture(int id);

		[JSImport("getExtensionCount", Module)]
		internal static partial int GetExtensionCount();

		[JSImport("glGetViewportArray", Module)]
		internal static partial int[] GlGetViewportArray();

		[JSImport("glGetFramebufferBindingId", Module)]
		internal static partial int GlGetFramebufferBindingId();
	}
}
