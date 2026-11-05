using System.Windows;

namespace HackerOverlay
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var win = new OverlayWindow();
            win.Show();
        }
    }
}
