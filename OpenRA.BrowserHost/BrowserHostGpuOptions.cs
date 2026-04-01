using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenRA.BrowserHost;

/// <summary>
/// Internal canvas size and <c>Graphics.*</c> launch args for better FPS in Wasm (query string overrides).
/// </summary>
	public readonly struct BrowserHostGpuOptions
	{
		public int CanvasWidth { get; init; }
		public int CanvasHeight { get; init; }
		public IReadOnlyList<string> GraphicsLaunchArgs { get; init; }
		/// <summary>Optional <c>wss://…</c> (or <c>ws://…</c>) passed to the engine as <c>Browser.TunnelUrl=</c> for auto-join.</summary>
		public string? BrowserTunnelJoinUrl { get; init; }

		/// <summary>Optional proxy <c>EDGE_TOKEN</c> as <c>Browser.TunnelEdgeToken=</c> (header <c>X-OpenRA-Tunnel-Token</c>).</summary>
		public string? BrowserTunnelEdgeToken { get; init; }

		/// <summary>Engine <c>Game.Mod=</c> (query <c>mod=</c>); default <c>ra</c> so <c>wwwroot/support/Content/ra/v2</c> matches <c>copy-ra-content-to-browser.sh</c>. Use <c>?mod=cnc</c> with <c>copy-cnc-content-to-browser.sh</c>.</summary>
		public string BrowserGameMod { get; init; }

	/// <summary>Default internal resolution (16:9, fewer pixels than 720p for fill-rate).</summary>
	public const int DefaultCanvasWidth = 1024;
	public const int DefaultCanvasHeight = 576;

	/// <summary>
	/// Parse <paramref name="navigationUri"/> query. See <c>HTTP-CACHE-NOTES.txt</c> for URL recipes.
	/// Support assets can use <c>supportOrigin=</c> (handled in <c>OpenRAHost</c>, not here).
	/// </summary>
	public static BrowserHostGpuOptions Resolve(string navigationUri)
	{
		var q = ParseQuery(navigationUri);

		var w = DefaultCanvasWidth;
		var h = DefaultCanvasHeight;

		if (q.TryGetValue("res", out var res))
		{
			switch (res.Trim().ToLowerInvariant())
			{
				case "540":
				case "low":
					w = 960;
					h = 540;
					break;
				case "576":
				case "balanced":
					w = 1024;
					h = 576;
					break;
				case "720":
				case "hd":
				case "high":
					w = 1280;
					h = 720;
					break;
				case "900":
					w = 1600;
					h = 900;
					break;
				case "1080":
				case "fhd":
					w = 1920;
					h = 1080;
					break;
			}
		}

		if (q.TryGetValue("w", out var ws) && q.TryGetValue("h", out var hs)
			&& int.TryParse(ws, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wp)
			&& int.TryParse(hs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var hp)
			&& wp >= 320 && hp >= 240)
		{
			w = Math.Clamp(wp, 320, 3840);
			h = Math.Clamp(hp, 240, 2160);
		}

		var hadExplicitSize = q.ContainsKey("res")
			|| (q.ContainsKey("w") && q.ContainsKey("h"));

		if (QueryFlagTrue(q, "perf") && !hadExplicitSize)
		{
			w = 960;
			h = 540;
		}

		var graphics = new List<string>();

		var viewport = "Close";
		if (q.TryGetValue("viewport", out var vps) && vps.Length > 0)
		{
			var v = vps.Trim();
			if (Enum.TryParse<OpenRA.WorldViewport>(v, true, out var wv))
				viewport = wv.ToString();
		}

		graphics.Add($"Graphics.ViewportDistance={viewport}");
		graphics.Add($"Graphics.WindowedSize={w},{h}");

		AppendVSyncArgs(q, graphics);
		AppendFrameLimitArgs(q, graphics);

		if (QueryFlagTrue(q, "perf") && !q.ContainsKey("uiScale"))
			graphics.Add("Graphics.UIScale=0.9");

		if (q.TryGetValue("uiScale", out var uis) &&
			float.TryParse(uis, NumberStyles.Float, CultureInfo.InvariantCulture, out var uf) &&
			uf is > 0.5f and < 2f)
			graphics.Add($"Graphics.UIScale={uf}");

		string? tunnelJoin = null;
		if (q.TryGetValue("tunnel", out var tunnelRaw) && !string.IsNullOrWhiteSpace(tunnelRaw))
			tunnelJoin = tunnelRaw.Trim();

		string? tunnelEdge = null;
		if (q.TryGetValue("tunnelToken", out var edgeRaw) && !string.IsNullOrWhiteSpace(edgeRaw))
			tunnelEdge = edgeRaw.Trim();

		// Keep default in sync with run-dedicated-server.sh (no Game.Mod= arg). Use ?mod=cnc for Tiberian Dawn + wwwroot Content/cnc.
		var gameMod = "ra";
		if (q.TryGetValue("mod", out var modRaw) && !string.IsNullOrWhiteSpace(modRaw))
		{
			var m = modRaw.Trim().ToLowerInvariant();
			if (m.Length is >= 1 and <= 32 && m.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
				gameMod = m;
		}

		return new BrowserHostGpuOptions
		{
			CanvasWidth = w,
			CanvasHeight = h,
			GraphicsLaunchArgs = graphics,
			BrowserTunnelJoinUrl = tunnelJoin,
			BrowserTunnelEdgeToken = tunnelEdge,
			BrowserGameMod = gameMod
		};
	}

	static void AppendVSyncArgs(Dictionary<string, string> q, List<string> graphics)
	{
		if (!q.TryGetValue("vsync", out var vs))
			return;

		vs = vs.Trim();
		if (IsFalsey(vs))
			graphics.Add("Graphics.VSync=False");
		else if (IsTruthy(vs) || (int.TryParse(vs, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n != 0))
			graphics.Add("Graphics.VSync=True");
	}

	/// <summary>
	/// Priority: <c>capTick</c>/<c>totick</c> → 1 render per sim tick; else <c>maxFps</c>/<c>fps</c>; else <c>uncapped</c>.
	/// </summary>
	static void AppendFrameLimitArgs(Dictionary<string, string> q, List<string> graphics)
	{
		if (QueryFlagTrue(q, "capTick") || QueryFlagTrue(q, "capToTick") || QueryFlagTrue(q, "totick"))
		{
			graphics.Add("Graphics.CapFramerateToGameFps=True");
			return;
		}

		if (TryParseFpsQuery(q, "maxFps", out var uncapA, out var limitA))
		{
			ApplyFpsLimit(graphics, uncapA, limitA);
			return;
		}

		if (TryParseFpsQuery(q, "fps", out var uncapB, out var limitB))
		{
			ApplyFpsLimit(graphics, uncapB, limitB);
			return;
		}

		if (QueryFlagTrue(q, "uncapped") || QueryFlagTrue(q, "nolimit"))
			ApplyFpsLimit(graphics, true, 0);
	}

	static void ApplyFpsLimit(List<string> graphics, bool uncapped, int maxFps)
	{
		graphics.Add("Graphics.CapFramerateToGameFps=False");
		if (uncapped || maxFps <= 0)
			graphics.Add("Graphics.CapFramerate=False");
		else
		{
			graphics.Add("Graphics.CapFramerate=True");
			graphics.Add($"Graphics.MaxFramerate={Math.Clamp(maxFps, 1, 300)}");
		}
	}

	static bool TryParseFpsQuery(Dictionary<string, string> q, string key, out bool uncapped, out int maxFps)
	{
		uncapped = false;
		maxFps = 0;
		if (!q.TryGetValue(key, out var raw))
			return false;

		raw = raw.Trim();
		if (raw.Length == 0)
			return false;

		var low = raw.ToLowerInvariant();
		if (low is "uncapped" or "off" or "none" or "unlimited")
		{
			uncapped = true;
			return true;
		}

		if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
		{
			if (n <= 0)
				uncapped = true;
			else
				maxFps = n;
			return true;
		}

		return false;
	}

	static Dictionary<string, string> ParseQuery(string uri)
	{
		var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		if (string.IsNullOrEmpty(uri))
			return d;

		var q = uri.IndexOf('?', StringComparison.Ordinal);
		if (q < 0 || q >= uri.Length - 1)
			return d;

		foreach (var part in uri[(q + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
		{
			var eq = part.IndexOf('=');
			if (eq <= 0)
				continue;

			var key = Uri.UnescapeDataString(part[..eq]).Trim();
			var val = eq < part.Length - 1 ? Uri.UnescapeDataString(part[(eq + 1)..]).Trim() : "";
			if (key.Length > 0)
				d[key] = val;
		}

		return d;
	}

	static bool QueryFlagTrue(Dictionary<string, string> q, string name) =>
		q.TryGetValue(name, out var v) && IsTruthy(v);

	static bool IsTruthy(string v)
	{
		v = v.Trim();
		return v is "1" or "true" or "yes" or "on";
	}

	static bool IsFalsey(string v)
	{
		v = v.Trim();
		return v is "0" or "false" or "no" or "off";
	}
}
