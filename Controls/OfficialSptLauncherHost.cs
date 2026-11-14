using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using SptLauncherWpf.Services;

namespace SptLauncherWpf.Controls
{
    /// <summary>
    /// Hosts an existing top-level HWND as a child of this WPF control.
    /// </summary>
    public sealed class OfficialSptLauncherHost : HwndHost
    {
        private const uint WsChild = 0x40000000;
        private const uint WsVisible = 0x10000000;
        private const uint WsClipChildren = 0x02000000;

        private nint _host;
        private nint _child;

        public nint ChildHwnd => _child;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern nint CreateWindowEx(
            int dwExStyle,
            string lpClassName,
            string lpWindowName,
            uint dwStyle,
            int x,
            int y,
            int nWidth,
            int nHeight,
            nint hWndParent,
            nint hMenu,
            nint hInstance,
            nint lpParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(nint hWnd);

        public void Attach(nint childHwnd)
        {
            _child = childHwnd;
            FitChild();
        }

        public void ClearChild()
        {
            _child = 0;
        }

        public void FitChild()
        {
            if (_child == 0 || _host == 0)
            {
                return;
            }

            var source = PresentationSource.FromVisual(this);
            var dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            var dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            var width = Math.Max(1, (int)Math.Round(ActualWidth * dpiX));
            var height = Math.Max(1, (int)Math.Round(ActualHeight * dpiY));
            NativeWindowEmbed.MoveWindow(_child, 0, 0, width, height, true);
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            _host = CreateWindowEx(
                0,
                "static",
                "",
                WsChild | WsVisible | WsClipChildren,
                0,
                0,
                1,
                1,
                hwndParent.Handle,
                0,
                0,
                0);
            return new HandleRef(this, _host);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            _child = 0;
            if (hwnd.Handle != 0)
            {
                DestroyWindow(hwnd.Handle);
            }

            _host = 0;
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            FitChild();
        }
    }
}
