#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software.
 */
#endregion

#if OPENRA_BROWSER
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using OpenRA.Graphics;
using OpenRA.Support;
using OpenRA.Widgets;

namespace OpenRA
{
	public static partial class Game
	{
		static long browserModInitStartMs;
		static long browserModInitLastMs;

		static partial void LogBrowserModInit(string mod, string stage)
		{
			var now = RunTime;
			if (stage == "begin")
			{
				browserModInitStartMs = now;
				browserModInitLastMs = now;
				Console.WriteLine($"[browser-init] {mod}: begin");
				return;
			}

			var deltaMs = now - browserModInitLastMs;
			var totalMs = now - browserModInitStartMs;
			browserModInitLastMs = now;
			Console.WriteLine($"[browser-init] {mod}: {stage}  +{deltaMs}ms since prev  (total {totalMs}ms)");
		}

		/// <summary>True when browser mod loading aborted (e.g. missing external assets); host UI can show a message.</summary>
		public static bool BrowserModLoadAborted { get; private set; }

		/// <summary>True when <see cref="BrowserCompleteMapLoadAsync"/> must run to finish <see cref="InitializeMod"/> (map contents deferred).</summary>
		public static bool BrowserMapContentsLoadPending { get; private set; }

		static Arguments browserPendingInitArgs;
		static string browserPendingModId;

		/// <summary>Absolute site origin (e.g. Blazor <c>NavigationManager.BaseUri</c>) so support assets load via HTTP.</summary>
		public static string WasmHttpOrigin { get; set; }

		/// <summary>
		/// Optional absolute base URL for <c>/support/Content/…</c> fetches only (trailing slash optional).
		/// When null or empty, <see cref="WasmHttpOrigin"/> is used. Set for a CDN or separate static host; that origin must send CORS
		/// <c>Access-Control-Allow-Origin</c> for this app’s origin if cross-domain.
		/// </summary>
		public static string WasmSupportAssetsBaseUrl { get; set; }

		const int BrowserMapLoadChunkSize = 4;

		/// <summary>Completes deferred map scan after <see cref="BrowserInitialize"/>; call from Blazor with <c>await</c>.</summary>
		public static async Task BrowserCompleteMapLoadAsync(CancellationToken cancellationToken = default)
		{
			if (!BrowserMapContentsLoadPending || ModData == null)
				return;

			BrowserMapContentsLoadPending = false;
			var mod = browserPendingModId;
			var args = browserPendingInitArgs;
			browserPendingModId = null;
			browserPendingInitArgs = null;

			try
			{
				LogBrowserModInit(mod, "LoadMaps contents (chunked)");
				using (new PerfTimer("LoadMaps.Contents"))
					await ModData.MapCache.LoadMapContentsInChunksAsync(BrowserMapLoadChunkSize, cancellationToken);
				LogBrowserModInit(mod, "LoadMaps done");
				InitializeModAfterLoadMaps(mod, args);
			}
			catch (Exception e)
			{
				BrowserModLoadAborted = true;
				Console.WriteLine("[browser] BrowserCompleteMapLoadAsync failed: " + e);
				Log.Write("debug", e);
				Exit();
			}
		}

		static long browserNextLogic;
		static long browserNextRender;
		static long browserForcedNextRender;
		static bool browserRenderBeforeNextTick;

		static long browserPerfWindowStartMs;
		static double browserPerfAccumWallMs;
		static double browserPerfAccumLogicMs;
		static double browserPerfAccumRenderMs;
		static double browserPerfAccumSuspendMs;
		static int browserPerfFrameCount;
		static int browserPerfLogicCount;
		static int browserPerfRenderCount;
		static int browserPerfSuspendCount;
		static int browserPerfIterTotal;
		static double browserPerfMaxFrameMs;

		static void BrowserPerfResetWindow()
		{
			browserPerfWindowStartMs = Environment.TickCount64;
			browserPerfAccumWallMs = 0;
			browserPerfAccumLogicMs = 0;
			browserPerfAccumRenderMs = 0;
			browserPerfAccumSuspendMs = 0;
			browserPerfFrameCount = 0;
			browserPerfLogicCount = 0;
			browserPerfRenderCount = 0;
			browserPerfSuspendCount = 0;
			browserPerfIterTotal = 0;
			browserPerfMaxFrameMs = 0;
		}

		/// <summary>Initialize engine (same as desktop) without blocking game loop. Call once after the WebGL canvas exists.</summary>
		public static void BrowserInitialize(string[] args)
		{
			var initSw = Stopwatch.StartNew();
			Initialize(new Arguments(args));
			initSw.Stop();
			Console.WriteLine("[browser perf] init to map-prepare totalMs=" + initSw.ElapsedMilliseconds +
				(BrowserMapContentsLoadPending ? " (map contents loading async next)" : ""));

			GC.Collect();

			if (Settings.Graphics.MaxFramerate < 1)
			{
				Settings.Graphics.MaxFramerate = new GraphicSettings().MaxFramerate;
				Settings.Graphics.CapFramerate = false;
			}

			browserNextLogic = RunTime;
			browserNextRender = RunTime;
			browserForcedNextRender = RunTime;
			browserRenderBeforeNextTick = false;
			BrowserPerfResetWindow();
		}

		/// <summary>Run scheduling for one animation frame. Returns false when the game has exited.</summary>
		public static bool BrowserTick()
		{
			if (state != RunStatus.Running)
				return false;

			var frameSw = Stopwatch.StartNew();

			const int MaxLogicTicksBehind = 250;
			const int MinReplayFps = 10;
			// Fewer inner iterations per rAF than desktop: spreads logic/render catch-up across frames
			// and reduces Chrome "long handler" violations on a single animation frame.
			const int MaxIterations = 5;

			for (var iter = 0; iter < MaxIterations && state == RunStatus.Running; iter++)
			{
				browserPerfIterTotal++;
				var logicInterval = Ui.Timestep;
				var logicWorld = worldRenderer?.World;

				if (logicWorld != null && (!logicWorld.IsReplay || logicWorld.ReplayTimestep != 0))
					logicInterval = logicWorld == OrderManager?.World ? OrderManager.SuggestedTimestep : logicWorld.Timestep;

				var renderInterval = logicInterval;
				if (!Settings.Graphics.CapFramerateToGameFps)
				{
					var maxFramerate = Settings.Graphics.CapFramerate ? Settings.Graphics.MaxFramerate.Clamp(1, 1000) : 1000;
					renderInterval = 1000 / maxFramerate;
				}

				if (OrderManager?.World != null && OrderManager.World.IsLoadingGameSave)
				{
					logicInterval = 1;
					renderInterval = 200;
				}

				var now = RunTime;
				if (now - browserNextLogic > MaxLogicTicksBehind)
					browserNextLogic = now;

				var nextUpdate = Math.Min(browserNextLogic, browserNextRender);
				if (now < nextUpdate)
					return true;

				var forceRender = browserRenderBeforeNextTick || now >= browserForcedNextRender;
				var isTimeToRender = now >= browserNextRender;

				if (now >= browserNextLogic && !browserRenderBeforeNextTick)
				{
					browserNextLogic += logicInterval;
					var sw = Stopwatch.StartNew();
					LogicTick();
					sw.Stop();
					browserPerfAccumLogicMs += sw.Elapsed.TotalMilliseconds;
					browserPerfLogicCount++;
					if (OrderManager?.World != null && !OrderManager.World.IsLoadingGameSave && !OrderManager.World.IsReplay)
						browserRenderBeforeNextTick = true;
				}

				var haveSomeTimeUntilNextLogic = now < browserNextLogic;
				if (!Renderer.WindowIsSuspended && ((isTimeToRender && haveSomeTimeUntilNextLogic) || forceRender))
				{
					browserNextRender = now + renderInterval;
					var maxRenderInterval = Math.Max(1000 / MinReplayFps, renderInterval);
					browserForcedNextRender = now + maxRenderInterval;
					var sw = Stopwatch.StartNew();
					RenderTick();
					sw.Stop();
					browserPerfAccumRenderMs += sw.Elapsed.TotalMilliseconds;
					browserPerfRenderCount++;
					browserRenderBeforeNextTick = false;
				}

				if (Renderer.WindowIsSuspended && isTimeToRender)
				{
					browserNextRender = now + renderInterval;
					var sw = Stopwatch.StartNew();
					Renderer.Window.PumpInput(new NullInputHandler());
					sw.Stop();
					browserPerfAccumSuspendMs += sw.Elapsed.TotalMilliseconds;
					browserPerfSuspendCount++;
					browserRenderBeforeNextTick = false;
				}
			}

			frameSw.Stop();
			var wallMs = frameSw.Elapsed.TotalMilliseconds;
			browserPerfAccumWallMs += wallMs;
			browserPerfFrameCount++;
			if (wallMs > browserPerfMaxFrameMs)
				browserPerfMaxFrameMs = wallMs;

			var wallNow = Environment.TickCount64;
			if (wallNow - browserPerfWindowStartMs >= 1000)
			{
				var modId = ModData?.Manifest?.Id ?? "(null)";
				var widgetCount = Ui.Root?.Children?.Count ?? 0;
				Console.WriteLine(
					"[browser perf] mod=" + modId + " widgets=" + widgetCount + " suspended=" + Renderer.WindowIsSuspended +
					" frames=" + browserPerfFrameCount + " maxFrameMs=" + browserPerfMaxFrameMs.ToString("F1") +
					" sumWallMs=" + browserPerfAccumWallMs.ToString("F1") +
					" logicMs=" + browserPerfAccumLogicMs.ToString("F1") + " (" + browserPerfLogicCount + ")" +
					" renderMs=" + browserPerfAccumRenderMs.ToString("F1") + " (" + browserPerfRenderCount + ")" +
					" suspendPumpMs=" + browserPerfAccumSuspendMs.ToString("F1") + " (" + browserPerfSuspendCount + ")" +
					" innerIters=" + browserPerfIterTotal);
				BrowserPerfResetWindow();
			}

			return state == RunStatus.Running;
		}

		/// <summary>Dispose engine state after <see cref="BrowserTick"/> returns false.</summary>
		public static RunStatus BrowserShutdown()
		{
			try
			{
				OrderManager?.Dispose();
			}
			finally
			{
				worldRenderer?.Dispose();
				ModData?.Dispose();
				ChromeProvider.Deinitialize();
				Sound?.Dispose();
				Renderer?.Dispose();
				OnQuit();
			}

			return state;
		}
	}
}
#endif
