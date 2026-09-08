using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SelectionLens;

public sealed class MascotWindow : Window
{
    public MascotWindow()
    {
        Width = 104; Height = 104; AllowsTransparency = true; Background = Brushes.Transparent;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Topmost = true; ShowInTaskbar = false;
        var image = new Image { Width = 92, Height = 92, Stretch = Stretch.Uniform, Cursor = Cursors.Hand,
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/mascot-idle.png", UriKind.Absolute)),
            ToolTip = "Open HighLenser" };
        image.MouseLeftButtonUp += (_, _) => { DialogResult = true; Close(); };
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        Content = image;
    }
}
