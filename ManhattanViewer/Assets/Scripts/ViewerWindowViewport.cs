using System;
using System.Runtime.InteropServices;
using HeadTracked.Display;
using UnityEngine;

namespace Manhattan.Viewer
{
    /// <summary>Optional Windows client-to-panel mapping. Never changes physical calibration or the observer rig.</summary>
    [DefaultExecutionOrder(-50)]
    public sealed class ViewerWindowViewport : MonoBehaviour
    {
        private HeadTrackedDisplay display;
        private bool panelMapping;
        private IntPtr window, calibratedMonitor;
        public bool MappingValid { get; private set; }
        public Rect MonitorPixels { get; private set; }
        public Rect ClientPixels { get; private set; }
        public string Status { get; private set; } = "Measured render surface mode. Recheck rulers after resizing the window.";

        public void Configure(HeadTrackedDisplay target) { display = target; Refresh(); }
        public void SetPanelMapping(bool value)
        {
            if (panelMapping == value) return;
            panelMapping = value; calibratedMonitor = IntPtr.Zero;
            if (display != null) display.DisplayViewport = PhysicalScreenViewport.Full;
            Refresh();
        }

        /// <summary>Device pixels with top-left origin; independent of render resolution and UI scaling.</summary>
        public static bool TryNormalize(Rect monitor, Rect client, out Rect viewport)
        {
            viewport = default;
            if (!ValidPixels(monitor) || !ValidPixels(client) || client.xMin < monitor.xMin ||
                client.yMin < monitor.yMin || client.xMax > monitor.xMax || client.yMax > monitor.yMax) return false;
            viewport = new Rect((client.xMin - monitor.xMin) / monitor.width,
                (monitor.yMax - client.yMax) / monitor.height, client.width / monitor.width, client.height / monitor.height);
            return PhysicalScreenViewport.IsValid(viewport);
        }

        private static bool ValidPixels(Rect r) => float.IsFinite(r.x) && float.IsFinite(r.y) &&
            float.IsFinite(r.width) && float.IsFinite(r.height) && r.width > 0 && r.height > 0;

        public void Refresh()
        {
            MappingValid = false;
            if (display == null) return;
            if (!panelMapping)
            {
                display.DisplayViewport = PhysicalScreenViewport.Full;
                MappingValid = true;
                Status = "Measured render surface mode. Recheck rulers after resizing the window.";
                return;
            }
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (display.GetComponent<Camera>().targetTexture != null)
            { Status = "Panel mapping unavailable for a render texture. Previous aperture retained."; return; }
            try
            {
                if (window == IntPtr.Zero || !IsWindow(window))
                {
                    using (var process = System.Diagnostics.Process.GetCurrentProcess()) window = process.MainWindowHandle;
                }
                if (window == IntPtr.Zero || IsIconic(window))
                { Status = "Panel mapping awaiting a visible player window. Previous aperture retained."; return; }
                // Scope all coordinate reads to physical pixels; restore Unity's thread context immediately.
                IntPtr previous = SetThreadDpiAwarenessContext(new IntPtr(-4));
                if (previous == IntPtr.Zero)
                { Status = "Physical-pixel DPI context unavailable. Previous aperture retained."; return; }
                try
                {
                    Status = "Physical client area unavailable. Previous aperture retained.";
                    if (!GetClientRect(window, out NativeRect client)) return;
                    var topLeft = new NativePoint { x = client.left, y = client.top };
                    var bottomRight = new NativePoint { x = client.right, y = client.bottom };
                    IntPtr monitor = MonitorFromWindow(window, 0);
                    var info = new MonitorInfo { size = Marshal.SizeOf<MonitorInfo>() };
                    if (monitor == IntPtr.Zero || !ClientToScreen(window, ref topLeft) ||
                        !ClientToScreen(window, ref bottomRight) || !GetMonitorInfo(monitor, ref info)) return;
                    if (calibratedMonitor != IntPtr.Zero && calibratedMonitor != monitor)
                    { Status = "Return to the calibrated monitor. Re-measure before enabling mapping on another panel."; return; }
                    MonitorPixels = info.monitor.ToRect();
                    ClientPixels = new Rect(topLeft.x, topLeft.y, bottomRight.x - topLeft.x, bottomRight.y - topLeft.y);
                    if (!TryNormalize(MonitorPixels, ClientPixels, out Rect viewport))
                    { Status = "Keep the whole player window on one panel. Previous aperture retained."; return; }
                    calibratedMonitor = monitor;
                    display.DisplayViewport = viewport; MappingValid = true;
                    Status = "Full panel mode: physical player client area and camera rect mapped to panel.";
                }
                finally { SetThreadDpiAwarenessContext(previous); }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            { Status = "Windows panel mapping unavailable. Use measured render surface mode."; }
#else
            Status = "Full panel mapping requires a Windows Player. Use measured render surface mode here.";
#endif
        }

        private void Update() => Refresh();

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect
        {
            public int left, top, right, bottom;
            public Rect ToRect() => new Rect(left, top, right - left, bottom - top);
        }
        [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo
        { public int size; public NativeRect monitor, work; public uint flags; }
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hwnd, ref NativePoint point);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
#endif
    }
}
