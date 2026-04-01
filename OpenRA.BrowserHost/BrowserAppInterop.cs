using Microsoft.JSInterop;
using OpenRA;
using OpenRA.Platforms.Browser;

namespace OpenRA.BrowserHost;

public static class BrowserAppInterop
{
	[JSInvokable]
	public static bool BrowserFrame() => Game.BrowserTick();

	[JSInvokable]
	public static void EnqueueInput(string kind, int x, int y, int dx, int dy, int button, int mods, int keyCode, int delta, string text)
	{
		var k = kind switch
		{
			"move" => BrowserInputKind.MouseMove,
			"down" => BrowserInputKind.MouseDown,
			"up" => BrowserInputKind.MouseUp,
			"wheel" => BrowserInputKind.MouseWheel,
			"keydown" => BrowserInputKind.KeyDown,
			"keyup" => BrowserInputKind.KeyUp,
			"text" => BrowserInputKind.TextInput,
			"focus" => BrowserInputKind.Focus,
			"blur" => BrowserInputKind.Blur,
			"resize" => BrowserInputKind.Resize,
			_ => BrowserInputKind.MouseMove
		};

		BrowserInputQueue.Enqueue(new BrowserInputEnvelope(k, x, y, dx, dy, button, mods, keyCode, delta, text));
	}
}
