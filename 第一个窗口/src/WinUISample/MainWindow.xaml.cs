using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Scighost.WinUILib.Helpers;
using System;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using Windows.Graphics;
using Windows.Storage;
using WinRT.Interop;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WinUISample;

/// <summary>
/// An empty window that can be used on its own or navigated to within a Frame.
/// </summary>
public sealed partial class MainWindow : Window
{


    private IntPtr hwnd;

    private AppWindow appWindow;

    private AppWindowTitleBar titleBar;

    private SystemBackdropHelper backdropHelper;

    public MainWindow()
    {
        this.InitializeComponent();

        // 设置云母或亚克力背景
        backdropHelper = new SystemBackdropHelper(this);
        backdropHelper.TrySetMica(fallbackToAcrylic: true);

        // 窗口句柄
        hwnd = WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
        appWindow = AppWindow.GetFromWindowId(id);

        // 初始化窗口大小和位置
        this.Closed += MainWindow_Closed;
        if (ApplicationData.Current.LocalSettings.Values["IsMainWindowMaximum"] is true)
        {
            // 最大化
            User32.ShowWindow(hwnd, ShowWindowCommand.SW_SHOWMAXIMIZED);
        }
        else if (ApplicationData.Current.LocalSettings.Values["MainWindowRect"] is ulong value)
        {
            var rect = new WindowRect(value);
            // 屏幕区域
            var area = DisplayArea.GetFromWindowId(windowId: id, DisplayAreaFallback.Primary);
            // 若窗口在屏幕范围之内
            if (rect.Left > 0 && rect.Top > 0 && rect.Right < area.WorkArea.Width && rect.Bottom < area.WorkArea.Height)
            {
                appWindow.MoveAndResize(rect.ToRectInt32());
            }
        }

        // 自定义标题栏
        if (AppWindowTitleBar.IsCustomizationSupported())
        {
            // 不支持时 titleBar 为 null
            titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            // 标题栏按键背景色设置为透明
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            // 获取系统缩放率
            var scale = (float)User32.GetDpiForWindow(hwnd) / 96;
            // 48 这个值是应用标题栏的高度，不是唯一的，根据自己的 UI 设计而定
            titleBar.SetDragRectangles(new RectInt32[] { new RectInt32((int)(48 * scale), 0, 10000, (int)(48 * scale)) });
        }
        else
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
        }

    }



    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        // 保存窗口状态
        var wpl = new User32.WINDOWPLACEMENT();
        if (User32.GetWindowPlacement(hwnd, ref wpl))
        {
            ApplicationData.Current.LocalSettings.Values["IsMainWindowMaximum"] = wpl.showCmd == ShowWindowCommand.SW_MAXIMIZE;
            var p = appWindow.Position;
            var s = appWindow.Size;
            var rect = new WindowRect(p.X, p.Y, s.Width, s.Height);
            ApplicationData.Current.LocalSettings.Values["MainWindowRect"] = rect.Value;
        }
    }



    /// <summary>
    /// RectInt32 和 ulong 相互转换
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    private struct WindowRect
    {
        [FieldOffset(0)]
        public short X;
        [FieldOffset(2)]
        public short Y;
        [FieldOffset(4)]
        public short Width;
        [FieldOffset(6)]
        public short Height;
        [FieldOffset(0)]
        public ulong Value;

        public int Left => X;
        public int Top => Y;
        public int Right => X + Width;
        public int Bottom => Y + Height;

        public WindowRect(int x, int y, int width, int height)
        {
            X = (short)x;
            Y = (short)y;
            Width = (short)width;
            Height = (short)height;
        }

        public WindowRect(ulong value)
        {
            Value = value;
        }

        public RectInt32 ToRectInt32()
        {
            return new RectInt32(X, Y, Width, Height);
        }
    }


}

