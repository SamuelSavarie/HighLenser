using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SelectionLens;

public sealed class SnapshotOverlayWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Rectangle _selection = new() { Stroke = Brushes.Cyan, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(38, 40, 231, 255)) };
    private Point _start;

    public Int32Rect SelectedPixels { get; private set; }

    public SnapshotOverlayWindow()
    {
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(75, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        Content = _canvas;
        _canvas.Children.Add(_selection);
        _selection.Visibility = Visibility.Collapsed;
        MouseLeftButtonDown += StartSelection;
        MouseMove += UpdateSelection;
        MouseLeftButtonUp += FinishSelection;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
    }

    private void StartSelection(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(this);
        _selection.Visibility = Visibility.Visible;
        CaptureMouse();
        Draw(_start);
    }

    private void UpdateSelection(object sender, MouseEventArgs e)
    {
        if (IsMouseCaptured) Draw(e.GetPosition(this));
    }

    private void Draw(Point current)
    {
        Canvas.SetLeft(_selection, Math.Min(_start.X, current.X));
        Canvas.SetTop(_selection, Math.Min(_start.Y, current.Y));
        _selection.Width = Math.Abs(current.X - _start.X);
        _selection.Height = Math.Abs(current.Y - _start.Y);
    }

    private void FinishSelection(object sender, MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured) return;
        Point end = e.GetPosition(this);
        Point a = PointToScreen(_start);
        Point b = PointToScreen(end);
        ReleaseMouseCapture();
        int x = (int)Math.Round(Math.Min(a.X, b.X));
        int y = (int)Math.Round(Math.Min(a.Y, b.Y));
        int width = (int)Math.Round(Math.Abs(b.X - a.X));
        int height = (int)Math.Round(Math.Abs(b.Y - a.Y));
        if (width < 12 || height < 12) { DialogResult = false; Close(); return; }
        SelectedPixels = new Int32Rect(x, y, width, height);
        DialogResult = true;
        Close();
    }
}
