using Microsoft.Maui;
using Microsoft.Maui.Hosting;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using Windows.Graphics;
using WinRT.Interop;
using Window = Microsoft.UI.Xaml.Window;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WifiCheckApp.WinUI
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : MauiWinUIApplication
    {
        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            base.OnLaunched(args);

            try
            {
                var window = Microsoft.Maui.Controls.Application.Current?.Windows[0]?.Handler?.PlatformView as Window;
                if (window is null) return;

                IntPtr hWnd = WindowNative.GetWindowHandle(window);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = AppWindow.GetFromWindowId(windowId);

                appWindow.Resize(new SizeInt32
                {
                    Width = 473,
                    Height = 600
                });

            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Lỗi resize window: " + ex.Message);
            }
        }
    }

}
