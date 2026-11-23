using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace BorderlessWindow
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "espi.gmsde.borderless";
        public const string Name = "Borderless Window";
        public const string Version = "1.1.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> borderless;
        internal static ConfigEntry<int> width, height, posX, posY;
        internal static ConfigEntry<KeyboardShortcut> toggleKey;
        internal static ConfigEntry<bool> center;

        private void Awake()
        {
            Log = Logger;
            borderless = Config.Bind("Window", "Enabled", true, "Strip the title bar/border in Window mode.");
            width = Config.Bind("Window", "Width", 0, "Window width in px. 0 = keep game resolution.");
            height = Config.Bind("Window", "Height", 0, "Window height in px. 0 = keep game resolution.");
            center = Config.Bind("Window", "Center", true, "Center on the window's current monitor. Overrides X/Y.");
            posX = Config.Bind("Window", "X", 0, "Left edge in desktop px (primary monitor top-left is 0,0). Used when Center = false.");
            posY = Config.Bind("Window", "Y", 0, "Top edge in desktop px. Used when Center = false.");
            toggleKey = Config.Bind("Window", "ToggleKey", new KeyboardShortcut(KeyCode.F10), "Toggle borderless on/off.");

            // game kills the BepInEx manager object, so run on our own
            var go = new GameObject("BorderlessWindowRunner") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<Runner>();
            Config.SettingChanged += (_, __) => runner.Refresh();
        }
    }

    internal class Runner : MonoBehaviour
    {
        private IntPtr hwnd;
        private bool warnedNoWindow;
        private int origStyle;
        private bool applied;
        private bool busy;
        private float nextCheck;
        private readonly uint pid = GetPid();

        private static uint GetPid()
        {
            using (var p = Process.GetCurrentProcess())
                return (uint)p.Id;
        }

        private void Awake()
        {
            Plugin.Log.LogInfo("Runner started");
        }

        private void Update()
        {
            if (Plugin.toggleKey.Value.IsDown())
            {
                Plugin.borderless.Value = !Plugin.borderless.Value; // fires Refresh
                Plugin.Log.LogInfo($"Borderless {(Plugin.borderless.Value ? "on" : "off")}");
            }

            // game/Unity re-adds the frame on resolution or mode changes
            if (Time.unscaledTime < nextCheck || busy)
                return;
            nextCheck = Time.unscaledTime + 0.5f;

            if (!FindWindow())
            {
                if (!warnedNoWindow)
                    Plugin.Log.LogWarning("Game window not found yet");
                warnedNoWindow = true;
                return;
            }

            bool want = Plugin.borderless.Value && Screen.fullScreenMode == FullScreenMode.Windowed;
            bool hasFrame = (Native.GetWindowLong(hwnd, Native.GWL_STYLE) & Native.WS_CAPTION) != 0;

            if (want && (hasFrame || !applied))
                StartCoroutine(Apply());
            else if (!want && applied)
                Restore();
        }

        internal void Refresh()
        {
            applied = false;
            nextCheck = 0f;
        }

        private IEnumerator Apply()
        {
            busy = true;
            try
            {
                int w = Plugin.width.Value > 0 ? Plugin.width.Value : Screen.width;
                int h = Plugin.height.Value > 0 ? Plugin.height.Value : Screen.height;

                if (Screen.width != w || Screen.height != h)
                {
                    Screen.SetResolution(w, h, FullScreenMode.Windowed);
                    yield return null;
                    yield return null;
                }

                int style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
                if ((style & Native.WS_CAPTION) != 0)
                    origStyle = style;

                style &= ~Native.WS_OVERLAPPEDWINDOW;
                style |= Native.WS_POPUP;
                Native.SetWindowLong(hwnd, Native.GWL_STYLE, style);
                int x = Plugin.posX.Value, y = Plugin.posY.Value;
                if (Plugin.center.Value)
                {
                    var mi = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                    if (Native.GetMonitorInfo(Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST), ref mi))
                    {
                        var m = mi.rcMonitor;
                        x = m.Left + (m.Right - m.Left - w) / 2;
                        y = m.Top + (m.Bottom - m.Top - h) / 2;
                    }
                }

                Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, w, h, Native.SWP_REFRAME);

                applied = true;
                Plugin.Log.LogInfo($"Applied {w}x{h} at {x},{y}");
            }
            finally
            {
                busy = false;
            }
        }

        private void Restore()
        {
            applied = false;
            if (Screen.fullScreenMode != FullScreenMode.Windowed)
                return;

            // origStyle is 0 if we never saw a captioned window; fall back to a stock framed style
            int style = origStyle != 0
                ? origStyle
                : (Native.GetWindowLong(hwnd, Native.GWL_STYLE) & ~Native.WS_POPUP) | Native.WS_OVERLAPPEDWINDOW;
            Native.SetWindowLong(hwnd, Native.GWL_STYLE, style);
            var r = new Native.RECT { Left = 0, Top = 0, Right = Screen.width, Bottom = Screen.height };
            Native.AdjustWindowRect(ref r, style, false);
            Native.GetWindowRect(hwnd, out var cur);
            Native.SetWindowPos(hwnd, IntPtr.Zero, cur.Left, cur.Top, r.Right - r.Left, r.Bottom - r.Top, Native.SWP_REFRAME);
        }

        private bool FindWindow()
        {
            if (hwnd != IntPtr.Zero && Native.IsWindow(hwnd))
                return true;

            hwnd = IntPtr.Zero;
            var cls = new StringBuilder(64);
            Native.EnumWindows((h, _) =>
            {
                Native.GetWindowThreadProcessId(h, out uint p);
                if (p != pid)
                    return true;
                cls.Length = 0;
                Native.GetClassName(h, cls, cls.Capacity);
                if (cls.ToString() != "UnityWndClass")
                    return true;
                hwnd = h;
                return false;
            }, IntPtr.Zero);

            return hwnd != IntPtr.Zero;
        }
    }

    internal static class Native
    {
        public const int GWL_STYLE = -16;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_OVERLAPPEDWINDOW = 0x00CF0000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const uint SWP_NOZORDER = 0x0004;
        public const uint SWP_FRAMECHANGED = 0x0020;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const uint SWP_REFRAME = SWP_FRAMECHANGED | SWP_NOZORDER | SWP_SHOWWINDOW;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        public const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder buf, int max);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr hWnd, int idx, int val);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll")] public static extern bool AdjustWindowRect(ref RECT r, int style, bool menu);
    }
}
