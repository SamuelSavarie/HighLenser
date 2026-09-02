using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HighLenser.Mac;

public sealed class MainWindow : Window
{
    private static readonly IBrush Cyan = Brush.Parse("#70F0FF");
    private static readonly IBrush Muted = Brush.Parse("#99A5B6");
    private static readonly IBrush Panel = Brush.Parse("#121C30");
    private readonly TextBox _answer = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 18, Foreground = Brush.Parse("#F4F4F6"), Padding = new Thickness(8, 18), Text = "Press Start, then highlight text or code. Explanations run locally through Ollama." };
    private readonly TextBox _followUp = new() { PlaceholderText = "Ask a question or request more information…", Height = 58, FontSize = 15, Padding = new Thickness(16, 14), Background = Panel, Foreground = Brushes.White, BorderBrush = Brush.Parse("#315E78") };
    private readonly TextBlock _status = new() { Text = "Stopped", Foreground = Muted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "Standard", "In Depth", "Study Notes" }, SelectedIndex = 0, Width = 260, Height = 50, FontSize = 18, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ComboBox _tabs = new() { MinWidth = 360, Height = 46, PlaceholderText = "Saved explanations" };
    private readonly Button _start = HudButton("START", true);
    private readonly Ellipse _dot = new() { Width = 10, Height = 10, Fill = Brush.Parse("#7D818C") };
    private readonly OllamaClient _ollama = new();
    private readonly MacSelectionWatcher _watcher = new();
    private readonly List<SavedAnswer> _saved = new();
    private CancellationTokenSource? _request;
    private string _source = "";
    private bool _running;

    public MainWindow()
    {
        Title = "HighLenser"; Width = 920; Height = 780; MinWidth = 420; MinHeight = 350; MaxWidth = 1600; MaxHeight = 1200;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Background = Brushes.Transparent; Topmost = true; WindowDecorations = Avalonia.Controls.WindowDecorations.None;
        _start.Click += (_, _) => ToggleWatcher();
        _followUp.KeyDown += async (_, e) => { if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(_followUp.Text)) { e.Handled = true; await AskFollowUpAsync(); } };
        _tabs.SelectionChanged += (_, _) => { if (_tabs.SelectedItem is SavedAnswer item) { _source = item.Source; _answer.Text = item.Answer; } };
        _watcher.SelectionReady += async (_, text) => { _source = text; await ExplainAsync(text); };

        var shell = new Border {
            CornerRadius = new CornerRadius(28), BorderBrush = Brush.Parse("#365B86"), BorderThickness = new Thickness(1.5), Padding = new Thickness(26),
            Background = new LinearGradientBrush { StartPoint = new RelativePoint(0,0,RelativeUnit.Relative), EndPoint = new RelativePoint(1,1,RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#FC0A0F1C"),0), new GradientStop(Color.Parse("#FC141230"),.62), new GradientStop(Color.Parse("#FC081A2B"),1) } }
        };
        var root = new Grid { Width = 860, Height = 720, RowDefinitions = RowDefinitions.Parse("Auto,Auto,*,Auto,Auto,Auto") };
        shell.Child = new Viewbox { Stretch = Stretch.Fill, StretchDirection = StretchDirection.Both, Child = root }; Content = shell;

        var header = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("*,Auto"), Cursor = new Cursor(StandardCursorType.SizeAll) };
        header.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e); };
        header.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { _dot, new TextBlock { Text = "H I G H  //  L E N S E R", Foreground = Cyan, FontSize = 14, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center } } });
        var minimize = HudButton("−"); minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var close = HudButton("×"); close.Click += (_, _) => Close();
        var windowButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { minimize, close } }; Grid.SetColumn(windowButtons, 1); header.Children.Add(windowButtons); root.Children.Add(header);

        var tabsRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), Margin = new Thickness(0,28,0,18) };
        tabsRow.Children.Add(new TextBlock { Text = "SAVED TABS", Foreground = Muted, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,18,0) });
        Grid.SetColumn(_tabs,1); tabsRow.Children.Add(_tabs);
        var save = HudButton("SAVE TAB"); save.Margin = new Thickness(18,0,0,0); save.Click += (_, _) => SaveCurrent(); Grid.SetColumn(save,2); tabsRow.Children.Add(save);
        Grid.SetRow(tabsRow,1); root.Children.Add(tabsRow);

        var answerFrame = new Border { BorderBrush = Brush.Parse("#18273C"), BorderThickness = new Thickness(0,1), Background = Brush.Parse("#100B1424"), Padding = new Thickness(0,10), Child = _answer };
        Grid.SetRow(answerFrame,2); root.Children.Add(answerFrame);

        var modeRow = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), Margin = new Thickness(0,18,0,14) };
        modeRow.Children.Add(new TextBlock { Text = "SUMMARY LEVEL", Foreground = Muted, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(_mode,1); modeRow.Children.Add(_mode);
        var explore = HudButton("EXPLORE SELECTED"); explore.Click += async (_, _) => await ExplainAsync(string.IsNullOrWhiteSpace(_answer.SelectedText) ? _source : _answer.SelectedText); Grid.SetColumn(explore,2); modeRow.Children.Add(explore);
        Grid.SetRow(modeRow,3); root.Children.Add(modeRow);

        Grid.SetRow(_followUp,4); root.Children.Add(_followUp);
        var footer = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("Auto,*,Auto"), Margin = new Thickness(0,14,0,0) };
        footer.Children.Add(_start); Grid.SetColumn(_status,1); _status.Margin = new Thickness(18,0); footer.Children.Add(_status);
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var smaller = HudButton("A−"); smaller.Click += (_, _) => _answer.FontSize = Math.Max(12, _answer.FontSize - 1);
        var larger = HudButton("A+"); larger.Click += (_, _) => _answer.FontSize = Math.Min(30, _answer.FontSize + 1);
        var hudSmaller = HudButton("HUD−"); hudSmaller.Click += (_, _) => ResizeHud(-120, -95);
        var hudLarger = HudButton("HUD+"); hudLarger.Click += (_, _) => ResizeHud(120, 95);
        var access = HudButton("ACCESS"); access.Click += (_, _) => OpenAccessibilitySettings();
        tools.Children.Add(smaller); tools.Children.Add(larger); tools.Children.Add(hudSmaller); tools.Children.Add(hudLarger); tools.Children.Add(access); Grid.SetColumn(tools,2); footer.Children.Add(tools);
        Grid.SetRow(footer,5); root.Children.Add(footer);
        Opened += async (_, _) =>
        {
            if (!OllamaSetupWindow.IsOllamaInstalled())
            {
                var setup = new OllamaSetupWindow();
                bool ready = await setup.ShowDialog<bool>(this);
                _status.Text = ready ? "Ollama detected — ready to investigate" : "Ollama required for explanations";
            }
        };
        Closed += (_, _) => { _request?.Cancel(); _watcher.Dispose(); };
    }

    private static Button HudButton(string text, bool accent = false) => new() { Content = text, Padding = new Thickness(14,8), FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = accent ? Brushes.White : Brush.Parse("#DDFBFF"), Background = accent ? Brush.Parse("#7C5CFC") : Panel, BorderBrush = Brush.Parse("#315373"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };

    private void ToggleWatcher()
    {
        _running = !_running;
        if (_running) { _watcher.Start(); _start.Content = "STOP"; _start.Background = Brush.Parse("#CA4B56"); _dot.Fill = Brush.Parse("#5FE08A"); _status.Text = _watcher.IsTrusted ? "Watching for highlighted text" : "Accessibility permission required — use ACCESS"; }
        else { _watcher.Stop(); _start.Content = "START"; _start.Background = Brush.Parse("#7C5CFC"); _dot.Fill = Brush.Parse("#7D818C"); _status.Text = "Stopped"; }
    }

    private void ResizeHud(double widthChange, double heightChange)
    {
        Width = Math.Clamp(Width + widthChange, MinWidth, MaxWidth);
        Height = Math.Clamp(Height + heightChange, MinHeight, MaxHeight);
    }

    private async Task ExplainAsync(string text)
    {
        text = text.Trim(); if (text.Length < 2) { _status.Text = "Highlight some text first"; return; }
        if (text.Length > 12_000) { _status.Text = "Selection is too long"; return; }
        _source = text; _request?.Cancel(); _request = new CancellationTokenSource(); _status.Text = "Searching for the clearest explanation…"; _dot.Fill = Brush.Parse("#F3D84A");
        try { _answer.Text = await _ollama.ExplainAsync(text, _mode.SelectedItem?.ToString() ?? "Standard", _request.Token); _status.Text = "Ready to investigate"; _dot.Fill = Brush.Parse("#5FE08A"); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _answer.Text = ex.Message; _status.Text = "Could not explain this selection"; _dot.Fill = Brush.Parse("#CA4B56"); }
    }

    private async Task AskFollowUpAsync()
    {
        var question = _followUp.Text!.Trim(); _followUp.Text = "";
        await ExplainAsync($"Original text:\n{_source}\n\nCurrent explanation:\n{_answer.Text}\n\nFollow-up request:\n{question}");
    }

    private void SaveCurrent()
    {
        if (string.IsNullOrWhiteSpace(_answer.Text)) return;
        var title = _source.Replace('\n',' ').Trim(); if (title.Length > 42) title = title[..42] + "…"; if (title.Length == 0) title = "Saved explanation";
        var item = new SavedAnswer(title, _source, _answer.Text); _saved.Insert(0,item); _tabs.ItemsSource = null; _tabs.ItemsSource = _saved; _tabs.SelectedItem = item; _status.Text = "Tab saved";
    }

    private static void OpenAccessibilitySettings()
    {
        if (!OperatingSystem.IsMacOS()) return;
        Process.Start(new ProcessStartInfo { FileName = "open", Arguments = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility", UseShellExecute = false });
    }

    private sealed record SavedAnswer(string Title, string Source, string Answer) { public override string ToString() => Title; }
}
