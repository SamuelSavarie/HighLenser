using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SelectionLens;

public partial class MainWindow : Window
{
    private readonly SelectionWatcher _watcher = new();
    private readonly OllamaExplainer _explainer = new();
    private CancellationTokenSource? _requestCts;
    private bool _reallyClosing;
    private bool _monitoring;
    private bool _sidePanelOpen;
    private string _lastSourceSelection = "";
    private string _lastExplanation = "";
    private readonly List<SavedTab> _savedTabs = new();
    private bool _windowLoaded;
    private bool _suppressModeChange;
    private bool _suppressTabSelection;
    private bool _savingTab;
    private bool _quizActive;
    private bool _quizBusy;
    private CancellationTokenSource? _quizCts;
    private QuizQuestionData? _quizQuestion;
    private string _quizSource = "";
    private readonly List<QuizQuestionData> _quizQuestions = new();
    private int _quizIndex = -1;
    private bool _quizLimited;
    private int _quizQuestionNumber;
    private int _quizAnswered;
    private int _quizCorrect;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += OnClosing;
        _watcher.SelectionReady += OnSelectionReady;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionBottomLeft();
        _watcher.Start();
        _watcher.Pause();
        SetMonitoringUi(false);
        SetText(ExplanationBox, "Press Start, then highlight text or code. Explanations run locally through Ollama.");
        _savedTabs.AddRange(SavedTabsStore.Load());
        RefreshSavedTabs();
        _windowLoaded = true;
        SetMascot("idle", "Ready for the next case.");
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, -20);
        SetWindowLong(handle, -20, style | 0x00000080); // tool-window appearance
    }

    private async void OnSelectionReady(object? sender, string selection)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            _requestCts?.Cancel();
            _requestCts = new CancellationTokenSource();
            StatusDot.Fill = Brushes.Gold;
            StatusText.Text = "Explaining…";
            SetText(ExplanationBox, "Thinking…");
            SetMascot("searching", "Searching for clues in your selection…");

            try
            {
                _lastSourceSelection = selection;
                _lastExplanation = await _explainer.ExplainAsync(selection, AppSettings.LoadModel(), GetSummaryMode(), _requestCts.Token);
                SetText(ExplanationBox, _lastExplanation);
                ClearSavedTabSelection();
                StatusText.Text = $"Explained {selection.Length:N0} characters";
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(95, 224, 138));
                SetMascot("idle", "Case solved. Here’s what I found.");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                SetText(ExplanationBox, ex.Message);
                StatusText.Text = "Could not explain selection";
                StatusDot.Fill = Brushes.IndianRed;
                SetMascot("sad", "I couldn’t crack that clue. Check that Ollama is running.");
            }
        });
    }

    private void PositionBottomLeft()
    {
        Left = SystemParameters.WorkArea.Left + 16;
        Top = SystemParameters.WorkArea.Bottom - Height - 16;
    }

    private void DragArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        _watcher.Pause();
        new SettingsWindow { Owner = this }.ShowDialog();
        if (_monitoring) _watcher.Resume();
        SetMonitoringUi(_monitoring);
    }

    private void StartStop_Click(object sender, RoutedEventArgs e)
    {
        _monitoring = !_monitoring;
        if (_monitoring) _watcher.Resume(); else _watcher.Pause();
        SetMonitoringUi(_monitoring);
    }

    private void SetMonitoringUi(bool running)
    {
        StartStopButton.Content = running ? "STOP" : "START";
        StartStopButton.Background = new SolidColorBrush(running ? Color.FromRgb(202, 75, 86) : Color.FromRgb(124, 92, 252));
        StatusText.Text = running ? "Watching for highlighted text" : "Stopped";
        StatusDot.Fill = new SolidColorBrush(running ? Color.FromRgb(95, 224, 138) : Color.FromRgb(125, 129, 140));
    }

    private void Smaller_Click(object sender, RoutedEventArgs e)
    {
        ExplanationBox.FontSize = Math.Max(10, ExplanationBox.FontSize - 1);
        SideExplanationBox.FontSize = Math.Max(10, SideExplanationBox.FontSize - 1);
    }
    private void Larger_Click(object sender, RoutedEventArgs e)
    {
        ExplanationBox.FontSize = Math.Min(28, ExplanationBox.FontSize + 1);
        SideExplanationBox.FontSize = Math.Min(28, SideExplanationBox.FontSize + 1);
    }
    private void HudSmaller_Click(object sender, RoutedEventArgs e) => ResizeHud(-120, -95);
    private void HudLarger_Click(object sender, RoutedEventArgs e) => ResizeHud(120, 95);

    private string GetSummaryMode() => (SummaryModeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Standard";

    private async void SummaryModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowLoaded || _suppressModeChange || string.IsNullOrWhiteSpace(_lastSourceSelection)) return;

        StatusText.Text = "Changing summary format…";
        SetMascot("searching", "Reorganizing the evidence…");
        _requestCts?.Cancel();
        _requestCts = new CancellationTokenSource();
        try
        {
            _lastExplanation = await _explainer.ExplainAsync(_lastSourceSelection, AppSettings.LoadModel(), GetSummaryMode(), _requestCts.Token);
            SetText(ExplanationBox, _lastExplanation);
            ClearSavedTabSelection();
            StatusText.Text = "Summary format changed";
            SetMascot("idle", "Your new format is ready.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private async void SaveTab_Click(object sender, RoutedEventArgs e)
    {
        if (_savingTab) return;
        if (string.IsNullOrWhiteSpace(_lastSourceSelection) || string.IsNullOrWhiteSpace(_lastExplanation))
        {
            StatusText.Text = "Create an explanation before saving";
            return;
        }

        _savingTab = true;
        StatusText.Text = "Creating a topic title…";
        string title;
        try
        {
            title = await _explainer.CreateTitleAsync(_lastSourceSelection, _lastExplanation, AppSettings.LoadModel(), CancellationToken.None);
            if (string.IsNullOrWhiteSpace(title)) title = CreateTabTitle(_lastSourceSelection);
        }
        catch { title = CreateTabTitle(_lastSourceSelection); }

        var tab = new SavedTab
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            SourceSelection = _lastSourceSelection,
            Explanation = _lastExplanation,
            SummaryMode = GetSummaryMode(),
            SavedAt = DateTime.Now
        };
        _savedTabs.Insert(0, tab);
        SavedTabsStore.Save(_savedTabs);
        RefreshSavedTabs(tab.Id);
        StatusText.Text = "Tab saved";
        _savingTab = false;
    }

    private void SavedTabsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_windowLoaded || _suppressTabSelection || SavedTabsBox.SelectedItem is not SavedTab tab) return;
        _lastSourceSelection = tab.SourceSelection;
        _lastExplanation = tab.Explanation;
        SetText(ExplanationBox, tab.Explanation);
        SetSummaryMode(tab.SummaryMode);
        StatusText.Text = $"Opened saved tab from {tab.SavedAt:g}";
    }

    private void DeleteTab_Click(object sender, RoutedEventArgs e)
    {
        if (SavedTabsBox.SelectedItem is not SavedTab tab)
        {
            StatusText.Text = "Choose a saved tab to delete";
            return;
        }
        _savedTabs.RemoveAll(item => item.Id == tab.Id);
        SavedTabsStore.Save(_savedTabs);
        RefreshSavedTabs();
        StatusText.Text = "Saved tab deleted";
    }

    private void RefreshSavedTabs(string? selectId = null)
    {
        _suppressTabSelection = true;
        SavedTabsBox.ItemsSource = null;
        SavedTabsBox.ItemsSource = _savedTabs;
        SavedTabsBox.SelectedItem = selectId is null ? null : _savedTabs.Find(t => t.Id == selectId);
        _suppressTabSelection = false;
    }

    private void ClearSavedTabSelection()
    {
        _suppressTabSelection = true;
        SavedTabsBox.SelectedIndex = -1;
        _suppressTabSelection = false;
    }

    private void SetSummaryMode(string mode)
    {
        _suppressModeChange = true;
        foreach (ComboBoxItem item in SummaryModeBox.Items)
            if (string.Equals(item.Tag?.ToString(), mode, StringComparison.Ordinal)) { SummaryModeBox.SelectedItem = item; break; }
        _suppressModeChange = false;
    }

    private static string CreateTabTitle(string source)
    {
        string oneLine = string.Join(" ", source.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        return oneLine.Length <= 42 ? oneLine : oneLine[..42] + "…";
    }

    private async void StartQuiz_Click(object sender, RoutedEventArgs e)
    {
        if (_quizActive) { ShowQuiz(); return; }
        if (string.IsNullOrWhiteSpace(_lastExplanation))
        {
            StatusText.Text = "Create an explanation before starting a quiz";
            return;
        }

        _quizActive = true;
        _quizCts?.Cancel();
        _quizCts = new CancellationTokenSource();
        _quizSource = _lastExplanation;
        _quizQuestions.Clear();
        _quizIndex = -1;
        _quizLimited = false;
        _quizQuestionNumber = 0;
        _quizAnswered = 0;
        _quizCorrect = 0;
        _quizQuestion = null;
        QuizFeedbackBorder.Visibility = Visibility.Collapsed;
        ReturnToQuizButton.Visibility = Visibility.Visible;
        ShowQuiz();
        SetMascot("searching", "Building your quiz case file…");
        try { await GenerateNextQuizQuestionAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _quizBusy = false;
            _quizActive = false;
            ReturnToQuizButton.Visibility = Visibility.Collapsed;
            QuizQuestionText.Text = ex.Message;
            QuizProgressText.Text = "Quiz could not start";
            SetMascot("sad", "I couldn’t build that quiz. Let’s try again.");
        }
    }

    private void ShowQuiz()
    {
        QuizView.Visibility = Visibility.Visible;
        ReturnToQuizButton.Visibility = Visibility.Visible;
        UpdateQuizStatus();
    }

    private void BackFromQuiz_Click(object sender, RoutedEventArgs e)
    {
        QuizView.Visibility = Visibility.Collapsed;
        ReturnToQuizButton.Visibility = _quizActive ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReturnToQuiz_Click(object sender, RoutedEventArgs e)
    {
        if (_quizActive) ShowQuiz();
    }

    private void QuizChoice_Click(object sender, RoutedEventArgs e)
    {
        if (_quizBusy || _quizQuestion is null || sender is not Button selectedButton || !int.TryParse(selectedButton.Tag?.ToString(), out int selectedIndex)) return;
        _quizBusy = true;
        _quizAnswered++;
        bool correct = selectedIndex == _quizQuestion.CorrectIndex;
        if (correct) _quizCorrect++;

        Button[] buttons = GetQuizChoiceButtons();
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i].IsEnabled = false;
            if (i == _quizQuestion.CorrectIndex)
            {
                buttons[i].Background = new SolidColorBrush(Color.FromRgb(30, 126, 91));
                buttons[i].BorderBrush = new SolidColorBrush(Color.FromRgb(95, 224, 138));
            }
            else if (i == selectedIndex)
            {
                buttons[i].Background = new SolidColorBrush(Color.FromRgb(135, 48, 65));
                buttons[i].BorderBrush = new SolidColorBrush(Color.FromRgb(255, 120, 140));
            }
        }

        QuizFeedbackBorder.Visibility = Visibility.Collapsed;
        NextQuizButton.Visibility = Visibility.Visible;
        UpdateQuizStatus();
        string correctChoice = $"{(char)('A' + _quizQuestion.CorrectIndex)}. {_quizQuestion.Choices[_quizQuestion.CorrectIndex]}";
        SetMascot(correct ? "happy" : "sad", correct ? "Correct! Great deduction." : $"Incorrect. The correct choice was {correctChoice}");
    }

    private async void NextQuiz_Click(object sender, RoutedEventArgs e)
    {
        if (!_quizActive) return;
        NextQuizButton.Visibility = Visibility.Collapsed;
        QuizFeedbackBorder.Visibility = Visibility.Collapsed;
        _quizBusy = false;
        SetMascot("searching", "Searching for your next question…");
        try { await GenerateNextQuizQuestionAsync(); }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _quizBusy = false;
            QuizFeedbackText.Text = ex.Message;
            QuizFeedbackBorder.Visibility = Visibility.Visible;
            NextQuizButton.Content = "TRY AGAIN";
            NextQuizButton.Visibility = Visibility.Visible;
            SetMascot("sad", "I lost that question. Try the next clue.");
        }
    }

    private async System.Threading.Tasks.Task GenerateNextQuizQuestionAsync()
    {
        QuizQuestionText.Text = "Creating the next question…";
        _quizBusy = true;
        SetQuizChoicesEnabled(false);
        bool justBuiltQuiz = false;
        if (_quizQuestions.Count == 0)
        {
            QuizSetData set = await _explainer.CreateQuizSetAsync(_quizSource, AppSettings.LoadModel(), _quizCts?.Token ?? CancellationToken.None);
            SetMascot("searching", "Double-checking every question for repeated ideas…");
            set = await _explainer.ValidateUniqueQuizSetAsync(set, AppSettings.LoadModel(), _quizCts?.Token ?? CancellationToken.None);
            _quizQuestions.AddRange(set.Questions);
            _quizLimited = set.Limited;
            justBuiltQuiz = true;
        }

        _quizIndex++;
        if (_quizIndex >= _quizQuestions.Count)
        {
            ShowNoMoreUniqueQuestions();
            return;
        }

        _quizQuestion = _quizQuestions[_quizIndex];
        _quizQuestionNumber = _quizIndex + 1;
        QuizQuestionText.Text = _quizQuestion.Question;
        NextQuizButton.Content = "NEXT QUESTION";
        Button[] buttons = GetQuizChoiceButtons();
        for (int i = 0; i < 4; i++)
        {
            buttons[i].Content = new TextBlock { Text = $"{(char)('A' + i)}.  {_quizQuestion.Choices[i]}", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Left };
            buttons[i].ClearValue(Button.BackgroundProperty);
            buttons[i].ClearValue(Button.BorderBrushProperty);
        }
        SetQuizChoicesEnabled(true);
        _quizBusy = false;
        if (justBuiltQuiz && _quizLimited) ShowLimitedQuizNotice();
        else SetMascot("idle", "Choose the best answer, detective.");
        UpdateQuizStatus();
    }

    private void ShowLimitedQuizNotice()
    {
        string countText = _quizQuestions.Count == 1 ? "only 1 unique question" : $"only {_quizQuestions.Count} unique questions";
        string message = $"Important: The highlighted information provided enough material for {countText}. There wasn’t enough information provided to create more questions without repeating the same ideas.";
        QuizFeedbackBorder.Background = new SolidColorBrush(Color.FromRgb(58, 48, 18));
        QuizFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(255, 222, 92));
        QuizFeedbackText.FontWeight = FontWeights.Bold;
        QuizFeedbackText.Text = message;
        QuizFeedbackBorder.Visibility = Visibility.Visible;
        SetMascot("reading", message);
    }

    private void ShowNoMoreUniqueQuestions()
    {
        _quizBusy = false;
        _quizQuestion = null;
        SetQuizChoicesEnabled(false);
        foreach (Button button in GetQuizChoiceButtons()) button.Content = "—";
        NextQuizButton.Visibility = Visibility.Collapsed;
        QuizQuestionText.Text = "QUIZ COMPLETE";
        string message = "There wasn’t enough information provided to create any more different questions without repeating ideas you already answered.";
        QuizFeedbackBorder.Background = new SolidColorBrush(Color.FromRgb(58, 48, 18));
        QuizFeedbackText.Foreground = new SolidColorBrush(Color.FromRgb(255, 222, 92));
        QuizFeedbackText.FontWeight = FontWeights.Bold;
        QuizFeedbackText.Text = message;
        QuizFeedbackBorder.Visibility = Visibility.Visible;
        SetMascot("reading", message);
        UpdateQuizStatus();
    }

    private Button[] GetQuizChoiceButtons() => new[] { QuizChoice0, QuizChoice1, QuizChoice2, QuizChoice3 };
    private void SetQuizChoicesEnabled(bool enabled)
    {
        foreach (Button button in GetQuizChoiceButtons()) button.IsEnabled = enabled;
    }

    private void UpdateQuizStatus()
    {
        QuizProgressText.Text = _quizQuestionNumber == 0 ? "Preparing quiz…" : $"Question {_quizQuestionNumber} of {_quizQuestions.Count}";
        int totalQuestions = _quizQuestions.Count;
        QuizScoreText.Text = totalQuestions == 0 ? "Score: 0/0" : $"Score: {_quizCorrect}/{totalQuestions}";
    }

    private void EndQuiz_Click(object sender, RoutedEventArgs e)
    {
        int totalQuestions = _quizQuestions.Count;
        _quizActive = false;
        _quizCts?.Cancel();
        _quizBusy = false;
        _quizQuestion = null;
        _quizSource = "";
        _quizQuestions.Clear();
        _quizIndex = -1;
        QuizView.Visibility = Visibility.Collapsed;
        ReturnToQuizButton.Visibility = Visibility.Collapsed;
        QuizFeedbackBorder.Visibility = Visibility.Collapsed;
        NextQuizButton.Visibility = Visibility.Collapsed;
        StatusText.Text = $"Quiz ended — score {_quizCorrect}/{totalQuestions}";
        SetMascot("idle", "Quiz case closed. Nice work.");
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        string text = GetText(ExplanationBox);
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
            StatusText.Text = "Copied to clipboard";
        }
    }

    private async void ExploreSelected_Click(object sender, RoutedEventArgs e)
    {
        string topic = new TextRange(ExplanationBox.Selection.Start, ExplanationBox.Selection.End).Text.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            StatusText.Text = "Select words in the explanation first";
            return;
        }

        OpenSidePanel(topic);
        SetText(SideExplanationBox, "Exploring…");
        SetMascot("searching", "Inspecting that clue more closely…");
        _requestCts?.Cancel();
        _requestCts = new CancellationTokenSource();
        try
        {
            string result = await _explainer.ExploreAsync(topic, _lastSourceSelection, _lastExplanation, AppSettings.LoadModel(), GetSummaryMode(), _requestCts.Token);
            SetText(SideExplanationBox, result);
            StatusText.Text = "Deeper explanation ready";
            SetMascot("idle", "I found the deeper connection.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { SetText(SideExplanationBox, ex.Message); }
    }

    private async void FollowUpBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunFollowUpAsync();
        }
    }

    private async System.Threading.Tasks.Task RunFollowUpAsync()
    {
        string request = FollowUpBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(request)) return;
        if (string.IsNullOrWhiteSpace(_lastExplanation))
        {
            StatusText.Text = "Create an explanation first";
            return;
        }

        FollowUpBox.Clear();
        StatusText.Text = "Updating explanation…";
        bool researchRequest = request.Contains("more", StringComparison.OrdinalIgnoreCase) || request.Contains("information", StringComparison.OrdinalIgnoreCase) || request.Contains("detail", StringComparison.OrdinalIgnoreCase);
        SetMascot(researchRequest ? "reading" : "searching", researchRequest ? "Finding what you’re looking for now…" : "Great question—let me think.");
        _requestCts?.Cancel();
        _requestCts = new CancellationTokenSource();
        try
        {
            _lastExplanation = await _explainer.FollowUpAsync(request, _lastSourceSelection, _lastExplanation, AppSettings.LoadModel(), GetSummaryMode(), _requestCts.Token);
            SetText(ExplanationBox, _lastExplanation);
            StatusText.Text = "Explanation updated";
            SetMascot("idle", "I found your answer.");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = ex.Message; }
    }

    private void OpenSidePanel(string topic)
    {
        SelectedTopicText.Text = topic.Length > 100 ? topic[..100] + "…" : topic;
        if (_sidePanelOpen) return;
        _sidePanelOpen = true;
        SidePanel.Visibility = Visibility.Visible;
        SidePanelColumn.Width = new GridLength(360);
    }

    private void CloseSidePanel_Click(object sender, RoutedEventArgs e)
    {
        if (!_sidePanelOpen) return;
        _sidePanelOpen = false;
        SidePanel.Visibility = Visibility.Collapsed;
        SidePanelColumn.Width = new GridLength(0);
    }

    private void CopySide_Click(object sender, RoutedEventArgs e)
    {
        string text = GetText(SideExplanationBox);
        if (!string.IsNullOrWhiteSpace(text)) Clipboard.SetText(text);
    }

    private static string GetText(RichTextBox box) => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.Trim();
    private static void SetText(RichTextBox box, string text)
    {
        box.Document.Blocks.Clear();
        box.Document.Blocks.Add(new Paragraph(new Run(text)) { Margin = new Thickness(0) });
    }

    private void SetMascot(string state, string speech)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/mascot-{state}.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        MascotImage.Source = image;
        QuizMascotImage.Source = image;
        MascotSpeech.Text = speech;
        QuizMascotSpeech.Text = speech;
        MascotBubble.Visibility = string.IsNullOrWhiteSpace(speech) ? Visibility.Collapsed : Visibility.Visible;
        QuizMascotBubble.Visibility = string.IsNullOrWhiteSpace(speech) ? Visibility.Collapsed : Visibility.Visible;
        AnimateMascot(MascotImage, state);
        AnimateMascot(QuizMascotImage, state);
    }

    private static void AnimateMascot(Image image, string state)
    {
        image.BeginAnimation(UIElement.OpacityProperty, null);
        if (state == "searching")
        {
            var rotate = new RotateTransform(0);
            image.RenderTransform = rotate;
            rotate.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(-4, 4, TimeSpan.FromMilliseconds(360))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        else if (state == "happy")
        {
            var move = new TranslateTransform();
            image.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(220))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3) });
        }
        else if (state == "sad")
        {
            image.RenderTransform = Transform.Identity;
            image.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.65, TimeSpan.FromMilliseconds(600))
            { AutoReverse = true, RepeatBehavior = new RepeatBehavior(2) });
        }
        else if (state == "reading")
        {
            var move = new TranslateTransform();
            image.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromMilliseconds(650))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        }
        else image.RenderTransform = Transform.Identity;
    }

    private void ResizeHud(double widthChange, double heightChange)
    {
        double bottom = Top + ActualHeight;
        Width = Math.Clamp(ActualWidth + widthChange, MinWidth, SystemParameters.WorkArea.Width - 16);
        Height = Math.Clamp(ActualHeight + heightChange, MinHeight, SystemParameters.WorkArea.Height - 16);
        Top = Math.Max(SystemParameters.WorkArea.Top + 16, bottom - Height);
    }
    private void Hide_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Exit_Click(object sender, RoutedEventArgs e) { _reallyClosing = true; Close(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_reallyClosing) { e.Cancel = true; WindowState = WindowState.Minimized; return; }
        _watcher.Dispose();
        _requestCts?.Cancel();
        _quizCts?.Cancel();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
