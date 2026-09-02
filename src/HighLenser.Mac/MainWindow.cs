using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HighLenser.Mac;

public sealed class MainWindow : Window
{
    private readonly TextBox _source = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 120, Watermark = "Highlight text in another app, or paste it here…" };
    private readonly TextBox _answer = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, MinHeight = 300 };
    private readonly TextBlock _status = new() { Text = "Stopped", Foreground = Brush.Parse("#91A0B5") };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Standard", "In Depth", "Study Notes" }, SelectedIndex = 0, MinWidth = 150 };
    private readonly Button _start = new() { Content = "Start watching", MinWidth = 130 };
    private readonly OllamaClient _ollama = new();
    private readonly MacSelectionWatcher _watcher = new();
    private CancellationTokenSource? _request;
    private bool _running;

    public MainWindow()
    {
        Title = "HighLenser";
        Width = 760;
        Height = 760;
        MinWidth = 560;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush.Parse("#071018");
        Topmost = true;

        var explain = new Button { Content = "Explain now", MinWidth = 120 };
        var access = new Button { Content = "Open Accessibility Settings" };
        _start.Click += (_, _) => ToggleWatcher();
        explain.Click += async (_, _) => await ExplainAsync(_source.Text ?? "");
        access.Click += (_, _) => OpenAccessibilitySettings();
        _watcher.SelectionReady += async (_, text) =>
        {
            _source.Text = text;
            await ExplainAsync(text);
        };

        var root = new Grid { RowDefinitions = RowDefinitions.Parse("Auto,Auto,Auto,*,Auto"), Margin = new Thickness(28) };
        root.Children.Add(new StackPanel
        {
            Spacing = 5,
            Children =
            {
                new TextBlock { Text = "HIGH // LENSER", FontSize = 13, FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#34E8FF") },
                new TextBlock { Text = "Highlight it. Understand it.", FontSize = 30, FontWeight = FontWeight.Bold, Foreground = Brushes.White },
                new TextBlock { Text = "Private explanations powered by Ollama on your Mac.", FontSize = 15, Foreground = Brush.Parse("#AAB7C8") }
            }
        });

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 22, 0, 14), Children = { _start, explain, _mode } };
        Grid.SetRow(controls, 1); root.Children.Add(controls);
        var sourcePanel = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "SELECTED CONTENT", FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#FFD84A") }, _source } };
        Grid.SetRow(sourcePanel, 2); root.Children.Add(sourcePanel);
        var answerPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 18, 0, 0), Children = { new TextBlock { Text = "EXPLANATION", FontWeight = FontWeight.Bold, Foreground = Brush.Parse("#8B6CFF") }, _answer } };
        Grid.SetRow(answerPanel, 3); root.Children.Add(answerPanel);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 14, 0, 0), Children = { _status, access } };
        Grid.SetRow(footer, 4); root.Children.Add(footer);
        Content = root;
        Closed += (_, _) => { _request?.Cancel(); _watcher.Dispose(); };
    }

    private void ToggleWatcher()
    {
        _running = !_running;
        if (_running)
        {
            _watcher.Start();
            _start.Content = "Stop watching";
            _status.Text = _watcher.IsTrusted ? "Watching highlighted text" : "Accessibility permission required — paste text or open Settings";
        }
        else
        {
            _watcher.Stop();
            _start.Content = "Start watching";
            _status.Text = "Stopped";
        }
    }

    private async Task ExplainAsync(string text)
    {
        text = text.Trim();
        if (text.Length < 2) { _status.Text = "Highlight or paste some text first"; return; }
        if (text.Length > 12_000) { _status.Text = "Selection is too long (12,000 character limit)"; return; }
        _request?.Cancel();
        _request = new CancellationTokenSource();
        _status.Text = "HighLenser is thinking…";
        _answer.Text = "";
        try
        {
            _answer.Text = await _ollama.ExplainAsync(text, _mode.SelectedItem?.ToString() ?? "Standard", _request.Token);
            _status.Text = "Ready";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _answer.Text = ex.Message; _status.Text = "Could not explain this selection"; }
    }

    private static void OpenAccessibilitySettings()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
            UseShellExecute = false
        });
    }
}
