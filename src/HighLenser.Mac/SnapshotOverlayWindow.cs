using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;

namespace HighLenser.Mac;

public sealed class SnapshotOverlayWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Rectangle _selection = new() { Stroke = Brush.Parse("#28E7FF"), StrokeThickness = 2, Fill = Brush.Parse("#2628E7FF"), IsVisible = false };
    private Point _start;
    private bool _dragging;

    public PixelRect SelectedPixels { get; private set; }

    public SnapshotOverlayWindow()
    {
        WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        CanResize = false;
        Topmost = true;
        ShowInTaskbar = false;
        Background = Brush.Parse("#4B000000");
        Cursor = new Cursor(StandardCursorType.Cross);
        Content = _canvas;
        _canvas.Children.Add(_selection);
        Opened += (_, _) =>
        {
            var screen = Screens.Primary;
            if (screen is null) return;
            Position = screen.Bounds.Position;
            Width = screen.Bounds.Width / screen.Scaling;
            Height = screen.Bounds.Height / screen.Scaling;
        };
        PointerPressed += StartSelection;
        PointerMoved += UpdateSelection;
        PointerReleased += FinishSelection;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(false); };
    }

    private void StartSelection(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _start = e.GetPosition(this);
        _dragging = true;
        _selection.IsVisible = true;
        e.Pointer.Capture(this);
        Draw(_start);
    }

    private void UpdateSelection(object? sender, PointerEventArgs e)
    {
        if (_dragging) Draw(e.GetPosition(this));
    }

    private void Draw(Point current)
    {
        Canvas.SetLeft(_selection, Math.Min(_start.X, current.X));
        Canvas.SetTop(_selection, Math.Min(_start.Y, current.Y));
        _selection.Width = Math.Abs(current.X - _start.X);
        _selection.Height = Math.Abs(current.Y - _start.Y);
    }

    private void FinishSelection(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        Point end = e.GetPosition(this);
        int x = Position.X + (int)Math.Round(Math.Min(_start.X, end.X) * RenderScaling);
        int y = Position.Y + (int)Math.Round(Math.Min(_start.Y, end.Y) * RenderScaling);
        int width = (int)Math.Round(Math.Abs(end.X - _start.X) * RenderScaling);
        int height = (int)Math.Round(Math.Abs(end.Y - _start.Y) * RenderScaling);
        if (width < 12 || height < 12) { Close(false); return; }
        SelectedPixels = new PixelRect(x, y, width, height);
        Close(true);
    }
}
