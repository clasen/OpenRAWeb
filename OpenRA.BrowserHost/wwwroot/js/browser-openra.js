// Create and resume AudioContext synchronously inside a user gesture (Chrome autoplay policy).
// WASM/setTimeout cannot unlock audio; the game may queue PCM/voices until this runs.
(function () {
  if (window.__openraAudioUnlockInstalled) return;
  window.__openraAudioUnlockInstalled = true;
  window.__openraAudioCtx = window.__openraAudioCtx || null;
  function unlockOnce() {
    var AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    var ctx = window.__openraAudioCtx;
    if (!ctx || ctx.state === 'closed') {
      ctx = new AC();
      window.__openraAudioCtx = ctx;
    }
    var done = function () {
      var cb = window.__openraOnAudioUnlocked;
      if (typeof cb === 'function') {
        try { cb(ctx); } catch (e) { console.error(e); }
      }
    };
    try {
      var p = ctx.resume();
      if (p !== undefined && typeof p.then === 'function') p.then(done, done);
      else done();
    } catch (e) {
      done();
    }
  }
  document.addEventListener('pointerdown', unlockOnce, { capture: true, passive: true });
  document.addEventListener('touchstart', unlockOnce, { capture: true, passive: true });
  document.addEventListener('keydown', unlockOnce, { capture: true });
})();

// Map KeyboardEvent → OpenRA Keycode (SDL 2 / OpenRA.Game.Input.Keycode).
// DOM legacy keyCode uses 65–90 for A–Z; OpenRA expects lowercase ASCII 97–122 for letter keys.
(function () {
  var M = 1 << 30;
  window.__openRaKeycodeFromEvent = function (e) {
    if (!e) return 0;
    var code = e.code;
    if (code) {
      if (code.length === 4 && code.indexOf('Key') === 0)
        return code.charCodeAt(3) | 0x20;
      if (code.length === 6 && code.indexOf('Digit') === 0)
        return code.charCodeAt(5);
      if (code.length === 11 && code.indexOf('Numpad') === 0) {
        var ch = code.charAt(6);
        if (ch >= '0' && ch <= '9') {
          var n = ch.charCodeAt(0) - 48;
          if (n === 0) return 98 | M;
          return (88 + n) | M;
        }
        switch (code) {
          case 'NumpadDecimal': return 99 | M;
          case 'NumpadDivide': return 84 | M;
          case 'NumpadMultiply': return 85 | M;
          case 'NumpadSubtract': return 86 | M;
          case 'NumpadAdd': return 87 | M;
          case 'NumpadEnter': return 88 | M;
          default: break;
        }
      }
      switch (code) {
        case 'ArrowLeft': return 80 | M;
        case 'ArrowUp': return 82 | M;
        case 'ArrowRight': return 79 | M;
        case 'ArrowDown': return 81 | M;
        case 'Escape': return 27;
        case 'Backspace': return 8;
        case 'Tab': return 9;
        case 'Enter': return 13;
        case 'Space': return 32;
        case 'Minus': return 45;
        case 'Equal': return 61;
        case 'BracketLeft': return 91;
        case 'BracketRight': return 93;
        case 'Backslash': return 92;
        case 'Semicolon': return 59;
        case 'Quote': return 39;
        case 'Comma': return 44;
        case 'Period': return 46;
        case 'Slash': return 47;
        case 'Backquote': return 96;
        case 'IntlBackslash': return 92;
        case 'CapsLock': return 57 | M;
        case 'Insert': return 73 | M;
        case 'Delete': return 127;
        case 'Home': return 74 | M;
        case 'End': return 77 | M;
        case 'PageUp': return 75 | M;
        case 'PageDown': return 78 | M;
        case 'ShiftLeft': return 225 | M;
        case 'ShiftRight': return 229 | M;
        case 'ControlLeft': return 224 | M;
        case 'ControlRight': return 228 | M;
        case 'AltLeft': return 226 | M;
        case 'AltRight': return 230 | M;
        case 'MetaLeft':
        case 'OSLeft': return 227 | M;
        case 'MetaRight':
        case 'OSRight': return 231 | M;
        case 'ContextMenu': return 101 | M;
        case 'ScrollLock': return 71 | M;
        case 'Pause': return 72 | M;
        case 'PrintScreen': return 70 | M;
        case 'F1': return 58 | M;
        case 'F2': return 59 | M;
        case 'F3': return 60 | M;
        case 'F4': return 61 | M;
        case 'F5': return 62 | M;
        case 'F6': return 63 | M;
        case 'F7': return 64 | M;
        case 'F8': return 65 | M;
        case 'F9': return 66 | M;
        case 'F10': return 67 | M;
        case 'F11': return 68 | M;
        case 'F12': return 69 | M;
        default: break;
      }
    }
    var kc = e.keyCode || e.which;
    if (kc >= 65 && kc <= 90) return kc + 32;
    if (kc >= 48 && kc <= 57) return kc;
    if (kc >= 37 && kc <= 40) {
      switch (kc) {
        case 37: return 80 | M;
        case 38: return 82 | M;
        case 39: return 79 | M;
        case 40: return 81 | M;
      }
    }
    if (kc === 27 || kc === 8 || kc === 9 || kc === 13 || kc === 32) return kc;
    if (kc === 16 || kc === 17 || kc === 18 || kc === 91 || kc === 93) {
      switch (kc) {
        case 16: return (e.location === 1 ? 225 : 229) | M;
        case 17: return (e.location === 1 ? 224 : 228) | M;
        case 18: return (e.location === 1 ? 226 : 230) | M;
        case 91: return 227 | M;
        case 93: return 231 | M;
      }
    }
    return kc | 0;
  };
})();

// rAF game loop + input → OpenRA.BrowserHost.BrowserAppInterop
window.browserOpenRA = {
  start: function (canvasId) {
    const asm = 'OpenRA.BrowserHost';
    const c = document.getElementById(canvasId);
    if (!c) return;

    function modFlags(e) {
      return (e.altKey ? 1 : 0) | (e.ctrlKey ? 2 : 0) | (e.metaKey ? 4 : 0) | (e.shiftKey ? 8 : 0);
    }

    function invoke(kind, x, y, dx, dy, btn, mods, kc, del, txt) {
      DotNet.invokeMethodAsync(asm, 'EnqueueInput', kind, x | 0, y | 0, dx | 0, dy | 0, btn | 0, mods | 0, kc | 0, del | 0, txt || '');
    }

    function shouldSendKeysToGame(e) {
      var t = e.target;
      if (!t) return true;
      var tag = t.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return false;
      if (t.isContentEditable) return false;
      return true;
    }

    function onWindowKey(ev) {
      if (!shouldSendKeysToGame(ev)) return;
      var k = window.__openRaKeycodeFromEvent(ev);
      if (!k) return;
      invoke(ev.type === 'keydown' ? 'keydown' : 'keyup', 0, 0, 0, 0, 0, modFlags(ev), k, ev.repeat ? 1 : 0, null);
      ev.preventDefault();
    }

    window.addEventListener('keydown', onWindowKey, true);
    window.addEventListener('keyup', onWindowKey, true);

    function syncCanvasSize() {
      var dpr = window.devicePixelRatio || 1;
      var cssW = c.clientWidth;
      var cssH = c.clientHeight;
      var w = Math.round(cssW * dpr);
      var h = Math.round(cssH * dpr);
      if (w < 320 || h < 240) return;
      if (c.width !== w || c.height !== h) {
        c.width = w;
        c.height = h;
        invoke('resize', w, h, cssW, cssH, 0, 0, 0, 0, null);
      }
    }
    syncCanvasSize();
    window.addEventListener('resize', function () { syncCanvasSize(); });

    var lastX = (c.width / 2) | 0;
    var lastY = (c.height / 2) | 0;

    var pendingMove = null;
    function flushPendingMove() {
      if (!pendingMove) return;
      var p = pendingMove;
      pendingMove = null;
      invoke('move', p.x, p.y, p.dx, p.dy, 0, p.mods, 0, 0, null);
    }

    c.addEventListener('mousemove', function (e) {
      const r = c.getBoundingClientRect();
      const sx = c.width / r.width;
      const sy = c.height / r.height;
      const x = (e.clientX - r.left) * sx;
      const y = (e.clientY - r.top) * sy;
      lastX = x | 0;
      lastY = y | 0;
      const mdx = e.movementX * sx;
      const mdy = e.movementY * sy;
      if (!pendingMove)
        pendingMove = { x: x, y: y, dx: mdx, dy: mdy, mods: modFlags(e) };
      else {
        pendingMove.dx += mdx;
        pendingMove.dy += mdy;
        pendingMove.x = x;
        pendingMove.y = y;
        pendingMove.mods = modFlags(e);
      }
    });
    c.addEventListener('contextmenu', function (e) {
      e.preventDefault();
    });

    c.addEventListener('mousedown', function (e) {
      if (e.button === 2)
        e.preventDefault();
      const r = c.getBoundingClientRect();
      const sx = c.width / r.width;
      const sy = c.height / r.height;
      const x = (e.clientX - r.left) * sx;
      const y = (e.clientY - r.top) * sy;
      lastX = x | 0;
      lastY = y | 0;
      invoke('down', x, y, 0, 0, e.button, modFlags(e), 0, 0, null);
      c.focus();
    });
    c.addEventListener('mouseup', function (e) {
      if (e.button === 2)
        e.preventDefault();
      const r = c.getBoundingClientRect();
      const sx = c.width / r.width;
      const sy = c.height / r.height;
      invoke('up', (e.clientX - r.left) * sx, (e.clientY - r.top) * sy, 0, 0, e.button, modFlags(e), 0, 0, null);
    });
    c.addEventListener('wheel', function (e) {
      e.preventDefault();
      const r = c.getBoundingClientRect();
      const sx = c.width / r.width;
      const sy = c.height / r.height;
      invoke('wheel', (e.clientX - r.left) * sx, (e.clientY - r.top) * sy, 0, 0, 0, modFlags(e), 0, Math.sign(e.deltaY), null);
    }, { passive: false });

    var shell = c.closest('.openra-host-shell');
    if (shell)
      shell.addEventListener('mousedown', function () { try { c.focus(); } catch (ex) { } });

    c.addEventListener('focus', function () { invoke('focus', 0, 0, 0, 0, 0, 0, 0, 0, null); });
    c.addEventListener('blur', function () { invoke('blur', 0, 0, 0, 0, 0, 0, 0, 0, null); });

    window.addEventListener('mouseup', function (e) {
      if (e.target === c) return;
      invoke('up', lastX, lastY, 0, 0, e.button, modFlags(e), 0, 0, null);
    });

    try { c.focus(); } catch (e0) { }

    function looper() {
      flushPendingMove();
      DotNet.invokeMethodAsync(asm, 'BrowserFrame').then(function (cont) {
        if (cont) requestAnimationFrame(looper);
      }).catch(function (err) {
        console.error('OpenRA BrowserFrame failed (stops render loop):', err);
      });
    }
    requestAnimationFrame(looper);
  }
};
