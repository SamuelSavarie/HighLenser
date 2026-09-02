using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HighLenser.Mac;

public sealed class OllamaSetupWindow : Window
{
    private readonly TextBlock _status = new()
    {
        TextWrapping = TextWrapping.Wrap,
        FontSize = 14,
        Foreground = Brush.Parse("#F3D84A")
    };

    public OllamaSetupWindow()
    {
        Title = "Set up Ollama";
        Width = 520;
        Height = 390;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#0A0F1C");

        var download = ActionButton("Download Ollama for Mac", true);
        var check = ActionButton("Check again");
        var continueButton = ActionButton("Continue without Ollama");

        download.Click += (_, _) => OpenOllamaDownload();
        check.Click += (_, _) => RefreshStatus();
        continueButton.Click += (_, _) => Close(false);

        Content = new Border
        {
            Margin = new Thickness(1),
            Padding = new Thickness(30),
            CornerRadius = new CornerRadius(18),
            BorderBrush = Brush.Parse("#365B86"),
            BorderThickness = new Thickness(1),
            Background = Brush.Parse("#10162A"),
            Child = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = "HIGHLENSER SETUP", Foreground = Brush.Parse("#70F0FF"), FontSize = 13, FontWeight = FontWeight.Bold },
                    new TextBlock { Text = "Install Ollama to use local AI", Foreground = Brushes.White, FontSize = 27, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = "HighLenser uses Ollama to explain highlighted text privately on your Mac. Ollama is free, and your text stays on your computer.",
                        Foreground = Brush.Parse("#B5C0D0"), FontSize = 15, LineHeight = 23, TextWrapping = TextWrapping.Wrap
                    },
                    _status,
                    download,
                    check,
                    continueButton
                }
            }
        };

        Opened += (_, _) => RefreshStatus();
    }

    public static bool IsOllamaInstalled() =>
        Directory.Exists("/Applications/Ollama.app") ||
        File.Exists("/opt/homebrew/bin/ollama") ||
        File.Exists("/usr/local/bin/ollama");

    private void RefreshStatus()
    {
        if (IsOllamaInstalled())
        {
            _status.Text = "✓ Ollama is installed. HighLenser is ready.";
            _status.Foreground = Brush.Parse("#5FE08A");
            Close(true);
        }
        else
        {
            _status.Text = "Ollama was not found. Download it, move Ollama to Applications, open it once, then choose Check again.";
            _status.Foreground = Brush.Parse("#F3D84A");
        }
    }

    private static Button ActionButton(string text, bool accent = false) => new()
    {
        Content = text,
        Height = 42,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        FontWeight = FontWeight.SemiBold,
        Foreground = accent ? Brush.Parse("#071018") : Brushes.White,
        Background = accent ? Brush.Parse("#70F0FF") : Brush.Parse("#17243A"),
        BorderBrush = Brush.Parse("#315373"),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8)
    };

    private static void OpenOllamaDownload()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "open",
            Arguments = "https://ollama.com/download/mac",
            UseShellExecute = false
        });
    }
}
