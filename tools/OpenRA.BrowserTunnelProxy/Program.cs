using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using OpenRA.BrowserTunnelProxy;

var builder = WebApplication.CreateBuilder(args);
var settings = TunnelSettings.Load(builder.Configuration);
var tlsCert = LoadTlsCertificate(settings);

if (settings.RateLimitWebSocketPerMinute > 0)
{
	builder.Services.AddRateLimiter(options =>
	{
		options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
		options.AddPolicy("websocket_tunnel", context =>
			RateLimitPartition.GetFixedWindowLimiter(
				context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
				_ => new FixedWindowRateLimiterOptions
				{
					AutoReplenishment = true,
					PermitLimit = settings.RateLimitWebSocketPerMinute,
					QueueLimit = 0,
					Window = TimeSpan.FromMinutes(1)
				}));
	});
}

builder.WebHost.ConfigureKestrel((_, kestrel) =>
{
	foreach (var url in settings.ListenUrls)
		ConfigureListenEndpoint(kestrel, new Uri(url), tlsCert);
});

var app = builder.Build();
app.Urls.Clear();
app.UseWebSockets();

if (settings.RateLimitWebSocketPerMinute > 0)
	app.UseRateLimiter();

SemaphoreSlim? concurrency = settings.MaxConcurrent > 0
	? new SemaphoreSlim(settings.MaxConcurrent, settings.MaxConcurrent)
	: null;

// Do not use MapGet("/"): a WebSocket handshake is also GET / with Upgrade: websocket;
// that route would return 200 text and break the tunnel. Health text is served from
// HandleTunnelAsync for non-WebSocket GET /.
var tunnel = app.Map("/{**path}", HandleTunnelAsync);
if (settings.RateLimitWebSocketPerMinute > 0)
	tunnel.RequireRateLimiting("websocket_tunnel");

app.Logger.LogInformation(
	"Tunnel listening on {Urls}; TCP default {Host}:{Port}; maxConcurrent={Max}; idleTimeout={Idle}s; rateLimit={Rate}/min/IP; tls={Tls}; tokenRoutes={Routes}; edgeToken={Edge}",
	string.Join("; ", settings.ListenUrls),
	settings.TargetHost,
	settings.TargetPort,
	settings.MaxConcurrent,
	settings.IdleTimeoutSeconds,
	settings.RateLimitWebSocketPerMinute,
	tlsCert != null ? "on" : "off",
	settings.TokenRoutes.Count,
	settings.EdgeToken != null ? "set" : "off");

app.Run();

async Task HandleTunnelAsync(HttpContext ctx)
{
	if (!ctx.WebSockets.IsWebSocketRequest)
	{
		var path = ctx.Request.Path.Value ?? "";
		if (HttpMethods.IsGet(ctx.Request.Method) &&
			(path == "/" || path.Length == 0))
		{
			ctx.Response.ContentType = "text/plain; charset=utf-8";
			await ctx.Response.WriteAsync(
				"OpenRA browser tunnel (WebSocket → TCP). " +
				$"Default TCP target {settings.TargetHost}:{settings.TargetPort}. " +
				"See docs/browser-multiplayer/02-proxy-and-infrastructure.md and README in this folder.",
				ctx.RequestAborted).ConfigureAwait(false);
			return;
		}

		ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
		await ctx.Response.WriteAsync("Expected WebSocket upgrade.", ctx.RequestAborted).ConfigureAwait(false);
		return;
	}

	var resolved = TryResolveTunnelTarget(ctx, settings);
	if (!resolved.Ok)
	{
		ctx.Response.StatusCode = resolved.StatusCode;
		await ctx.Response.WriteAsync(resolved.Error ?? "Forbidden", ctx.RequestAborted).ConfigureAwait(false);
		return;
	}

	if (concurrency != null && !await concurrency.WaitAsync(0, ctx.RequestAborted).ConfigureAwait(false))
	{
		ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
		await ctx.Response.WriteAsync("MAX_CONCURRENT tunnel limit reached.", ctx.RequestAborted).ConfigureAwait(false);
		return;
	}

	try
	{
		// Connect to the dedicated server *before* completing the WebSocket handshake (101).
		// Otherwise the browser shows a successful connect then an immediate drop when TCP fails.
		using var tcp = new TcpClient { NoDelay = true };
		try
		{
			await tcp.ConnectAsync(settings.TargetHost, resolved.Port, ctx.RequestAborted).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			app.Logger.LogWarning(ex, "TCP connect to {Host}:{Port} failed (WebSocket not upgraded)", settings.TargetHost, resolved.Port);
			ctx.Response.StatusCode = StatusCodes.Status502BadGateway;
			ctx.Response.ContentType = "text/plain; charset=utf-8";
			await ctx.Response.WriteAsync(
				$"Cannot reach OpenRA dedicated at {settings.TargetHost}:{resolved.Port}. " +
				"Start the dedicated server (e.g. ./run-dedicated-server.sh) or set TARGET_HOST/TARGET_PORT to match its listen port.",
				ctx.RequestAborted).ConfigureAwait(false);
			return;
		}

		var clientIp = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
		app.Logger.LogInformation(
			"Tunnel: upstream TCP connected for client {Client} -> {Host}:{Port}",
			clientIp, settings.TargetHost, resolved.Port);

		using var ws = await ctx.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
		app.Logger.LogInformation("Tunnel: WebSocket 101 sent; pumping for client {Client}", clientIp);

		await using var stream = tcp.GetStream();
		using var sessionCts = CancellationTokenSource.CreateLinkedTokenSource(ctx.RequestAborted);
		var ct = sessionCts.Token;
		ActivityClock? clock = null;
		Task? idleTask = null;
		if (settings.IdleTimeoutSeconds > 0)
		{
			clock = new ActivityClock();
			idleTask = RunIdleTimeoutAsync(sessionCts, clock, TimeSpan.FromSeconds(settings.IdleTimeoutSeconds));
		}

		var wsToTcp = PumpWebSocketToTcpAsync(ws, stream, clock, ct);
		var tcpToWs = PumpTcpToWebSocketAsync(stream, ws, clock, ct);

		try
		{
			await Task.WhenAny(wsToTcp, tcpToWs).ConfigureAwait(false);
		}
		finally
		{
			sessionCts.Cancel();
			try
			{
				await Task.WhenAll(wsToTcp, tcpToWs).ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
				// Expected.
			}

			if (idleTask != null)
			{
				try
				{
					await idleTask.ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					// Expected.
				}
			}

			// Kestrel often logs "the application aborted the connection" here; that is normal when either
			// leg closes (browser navigated away, game error UI, dedicated dropped TCP, etc.).
			app.Logger.LogInformation(
				"Tunnel: session ended for client {Client} (ws.state={WsState}, tcp.connected={TcpConnected})",
				clientIp, ws.State, tcp.Connected);
		}
	}
	finally
	{
		concurrency?.Release();
	}
}

static (bool Ok, int Port, int StatusCode, string? Error) TryResolveTunnelTarget(HttpContext ctx, TunnelSettings settings)
{
	if (settings.EdgeToken != null)
	{
		var header = ctx.Request.Headers["X-OpenRA-Tunnel-Token"].FirstOrDefault();
		var queryToken = ctx.Request.Query["token"].FirstOrDefault();
		if (header != settings.EdgeToken && queryToken != settings.EdgeToken)
			return (false, 0, StatusCodes.Status401Unauthorized, "Invalid or missing tunnel token (header X-OpenRA-Tunnel-Token or query token).");
	}

	if (settings.TokenRoutes.Count > 0)
	{
		var path = ctx.Request.Path.Value ?? "";
		const string prefix = "/play/";
		if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			return (false, 0, StatusCodes.Status404NotFound, "This proxy requires path /play/{room}.");

		var rest = path.Substring(prefix.Length).TrimStart('/');
		var token = rest.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
		if (string.IsNullOrEmpty(token) || !settings.TokenRoutes.TryGetValue(token, out var port))
			return (false, 0, StatusCodes.Status404NotFound, "Unknown room token.");

		return (true, port, 0, null);
	}

	return (true, settings.TargetPort, 0, null);
}

static X509Certificate2? LoadTlsCertificate(TunnelSettings settings)
{
	if (string.IsNullOrEmpty(settings.TlsCertPath))
		return null;

	if (!string.IsNullOrEmpty(settings.TlsKeyPath))
		return X509Certificate2.CreateFromPemFile(settings.TlsCertPath, settings.TlsKeyPath);

	if (settings.TlsCertPath.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
		|| settings.TlsCertPath.EndsWith(".p12", StringComparison.OrdinalIgnoreCase))
		return X509CertificateLoader.LoadPkcs12FromFile(settings.TlsCertPath, ReadOnlySpan<char>.Empty);

	throw new InvalidOperationException("TLS_KEY (or a .pfx in TLS_CERT) is required with TLS_CERT for PEM public key.");
}

static void ConfigureListenEndpoint(KestrelServerOptions options, Uri uri, X509Certificate2? tlsCert)
{
	var port = uri.Port > 0 ? uri.Port : (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80);
	var address = ResolveListenAddress(uri.Host);

	options.Listen(address, port, listen =>
	{
		if (uri.Scheme == Uri.UriSchemeHttps)
		{
			if (tlsCert == null)
				throw new InvalidOperationException($"HTTPS URL '{uri}' requires TLS_CERT/TLS_KEY (or a .pfx in TLS_CERT).");
			listen.UseHttps(https => https.ServerCertificate = tlsCert);
		}
	});
}

static IPAddress ResolveListenAddress(string host)
{
	if (string.IsNullOrEmpty(host) || host is "*" or "+")
		return IPAddress.Any;

	if (host == "0.0.0.0")
		return IPAddress.Any;

	if (host is "[::]" or "::")
		return IPAddress.IPv6Any;

	if (host is "localhost" or "127.0.0.1")
		return IPAddress.Loopback;

	if (host == "[::1]")
		return IPAddress.IPv6Loopback;

	if (host.Length > 2 && host[0] == '[' && host[^1] == ']'
		&& IPAddress.TryParse(host.AsSpan(1, host.Length - 2), out var bracketed))
		return bracketed;

	if (IPAddress.TryParse(host, out var literal))
		return literal;

	return Dns.GetHostAddresses(host).FirstOrDefault() ?? IPAddress.Any;
}

static async Task RunIdleTimeoutAsync(CancellationTokenSource sessionCts, ActivityClock clock, TimeSpan maxIdle)
{
	var thresholdMs = (long)maxIdle.TotalMilliseconds;
	var ct = sessionCts.Token;
	try
	{
		while (!ct.IsCancellationRequested)
		{
			await Task.Delay(1000, ct).ConfigureAwait(false);
			if (clock.IdleMilliseconds > thresholdMs)
			{
				sessionCts.Cancel();
				return;
			}
		}
	}
	catch (OperationCanceledException)
	{
		// Session ended or idle shutdown.
	}
}

static async Task PumpWebSocketToTcpAsync(WebSocket ws, Stream tcp, ActivityClock? clock, CancellationToken ct)
{
	var buffer = new byte[65536];
	try
	{
		while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
		{
			var result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
			if (result.MessageType == WebSocketMessageType.Close)
				break;

			if (result.MessageType == WebSocketMessageType.Text)
				throw new InvalidOperationException("Text frames are not supported for the OpenRA tunnel.");

			while (true)
			{
				if (result.Count > 0)
				{
					await tcp.WriteAsync(buffer.AsMemory(0, result.Count), ct).ConfigureAwait(false);
					clock?.Touch();
				}

				if (result.EndOfMessage)
					break;

				result = await ws.ReceiveAsync(buffer, ct).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close)
					return;
				if (result.MessageType == WebSocketMessageType.Text)
					throw new InvalidOperationException("Text frames are not supported for the OpenRA tunnel.");
			}
		}
	}
	catch (OperationCanceledException)
	{
		// Normal shutdown.
	}
}

static async Task PumpTcpToWebSocketAsync(Stream tcp, WebSocket ws, ActivityClock? clock, CancellationToken ct)
{
	var buffer = new byte[65536];
	try
	{
		while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
		{
			var read = await tcp.ReadAsync(buffer, ct).ConfigureAwait(false);
			if (read == 0)
				break;

			clock?.Touch();
			await ws.SendAsync(buffer.AsMemory(0, read), WebSocketMessageType.Binary, endOfMessage: true, ct).ConfigureAwait(false);
		}
	}
	catch (OperationCanceledException)
	{
		// Normal shutdown.
	}
}

sealed class ActivityClock
{
	long _lastTicks = Environment.TickCount64;

	public void Touch() => Interlocked.Exchange(ref _lastTicks, Environment.TickCount64);

	public long IdleMilliseconds => Environment.TickCount64 - Volatile.Read(ref _lastTicks);
}
