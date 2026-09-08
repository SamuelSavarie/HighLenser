using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SelectionLens;

public sealed class MascotWindow : Window
{
    public event EventHandler? RestoreRequested;

    public MascotWindow()
    {
        Width = 104; Height = 104; AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        var image = new Image { Width = 86, Height = 86, Stretch = Stretch.Uniform,
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/mascot-idle.png", UriKind.Absolute)),
            ToolTip = "Open HighLenser" };
        var button = new Button { Content = image, Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand, Padding = new Thickness(0), ToolTip = "Open HighLenser" };
        button.Click += (_, _) => RestoreRequested?.Invoke(this, EventArgs.Empty);
        Content = button;
    }
}
