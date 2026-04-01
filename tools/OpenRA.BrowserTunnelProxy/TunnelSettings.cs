using System.Collections.Frozen;
using Microsoft.Extensions.Configuration;

namespace OpenRA.BrowserTunnelProxy;

/// <summary>Configuration per <see href="../../docs/browser-multiplayer/02-proxy-and-infrastructure.md">02-proxy-and-infrastructure</see>.</summary>
sealed class TunnelSettings
{
	public string TargetHost { get; init; } = "127.0.0.1";
	public int TargetPort { get; init; } = 1234;
	public IReadOnlyList<string> ListenUrls { get; init; } = ["http://127.0.0.1:8787"];
	public string? TlsCertPath { get; init; }
	public string? TlsKeyPath { get; init; }
	public int MaxConcurrent { get; init; }
	public int IdleTimeoutSeconds { get; init; }
	public int RateLimitWebSocketPerMinute { get; init; }
	/// <summary>When set, clients must send this value in <c>X-OpenRA-Tunnel-Token</c> or query <c>token</c>.</summary>
	public string? EdgeToken { get; init; }
	/// <summary>When non-empty, WebSocket path must be <c>/play/&lt;token&gt;</c> and token maps to a TCP port.</summary>
	public FrozenDictionary<string, int> TokenRoutes { get; init; } = FrozenDictionary<string, int>.Empty;

	public static TunnelSettings Load(IConfiguration cfg)
	{
		var listenRaw = cfg["LISTEN"] ?? cfg["ASPNETCORE_URLS"] ?? "http://127.0.0.1:8787";
		var urls = listenRaw
			.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(NormalizeListenEntry)
			.ToArray();

		var routes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var section = cfg.GetSection("TOKEN_ROUTES");
		foreach (var child in section.GetChildren())
		{
			if (string.IsNullOrEmpty(child.Key))
				continue;
			if (int.TryParse(child.Value, out var p) && p > 0 && p <= 65535)
				routes[child.Key] = p;
		}

		return new TunnelSettings
		{
			TargetHost = cfg["TARGET_HOST"] ?? "127.0.0.1",
			TargetPort = int.TryParse(cfg["TARGET_PORT"], out var tp) ? tp : 1234,
			ListenUrls = urls,
			TlsCertPath = FirstNonEmpty(cfg["TLS_CERT"], cfg["TLS_CERT_FILE"]),
			TlsKeyPath = FirstNonEmpty(cfg["TLS_KEY"], cfg["TLS_KEY_FILE"]),
			MaxConcurrent = int.TryParse(cfg["MAX_CONCURRENT"], out var mc) ? Math.Max(0, mc) : 0,
			IdleTimeoutSeconds = int.TryParse(cfg["IDLE_TIMEOUT"], out var idle) ? Math.Max(0, idle) : 0,
			RateLimitWebSocketPerMinute = int.TryParse(cfg["RATE_LIMIT_PER_MINUTE"], out var rl) ? Math.Max(0, rl) : 0,
			EdgeToken = string.IsNullOrWhiteSpace(cfg["EDGE_TOKEN"]) ? null : cfg["EDGE_TOKEN"]!.Trim(),
			TokenRoutes = routes.Count > 0 ? routes.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase) : FrozenDictionary<string, int>.Empty
		};
	}

	static string? FirstNonEmpty(params string?[] values) =>
		values.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim();

	/// <summary>Expand <c>:8787</c> to <c>http://0.0.0.0:8787</c> and bare <c>host:port</c> to <c>http://…</c>.</summary>
	static string NormalizeListenEntry(string entry)
	{
		entry = entry.Trim();
		if (entry.Length == 0)
			return "http://127.0.0.1:8787";
		if (entry.StartsWith(':'))
			return "http://0.0.0.0" + entry;
		if (!entry.Contains("://", StringComparison.Ordinal))
			return "http://" + entry;
		return entry;
	}
}
