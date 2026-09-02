# HighLenser

HighLenser is a Windows desktop HUD that automatically explains highlighted text or code using a free local Ollama model.

## What the first version does

- Watches the currently focused program for highlighted text using Windows UI Automation.
- Waits 650 ms for the selection to stop changing before making a request.
- Shows a short AI explanation in a resizable, always-on-top HUD near the bottom-left.
- Offers Standard, In Depth, and Study Notes explanation levels.
- Puts Key Takeaways and Why You Should Know This at the top of every explanation.
- Includes dedicated HUD− and HUD+ controls, corner-drag resizing, and one-click copying.
- Uses a high-contrast white summary-level menu with black text.
- Lets you select words inside an explanation and open a side panel for a deeper look at that exact topic.
- Includes a follow-up typing bar for questions and requests such as adding examples or more detail.
- Instantly reformats the current explanation when the summary level changes—no re-highlighting needed.
- Saves explanations as persistent tabs only when Save Tab is pressed; saved tabs remain after restarting the app.
- Submits follow-up requests with Enter, with no separate Ask button.
- Uses a custom cartoon highlighter-over-text desktop icon.
- Gives new saved tabs short AI-generated topic titles instead of copying their first words.
- Opens in the bottom-left every time and can still be dragged anywhere using the top bar.
- Keeps quiz controls on their own dedicated row so controls never overlap at smaller HUD sizes.
- Uses an interactive futuristic neon-glass theme with responsive hover states.
- Runs quizzes entirely as four-choice questions with clickable answer feedback and no quiz typing field.
- Adds a pencil-detective AI mascot with idle, searching, reading, happy, and sad animated states tied to real app actions.
- Places the normal-mode mascot in its own layout row so it never covers the follow-up typing field.
- Builds each quiz as a deduplicated bank of questions that test different facts or concepts instead of rewording one question.
- Displays a prominent mascot warning when the highlighted material cannot support more unique questions.
- Runs a second AI semantic similarity check on the completed question bank every time a quiz starts.
- Shows correct/incorrect results only through the mascot speech bubble; wrong answers reveal only the correct choice.
- Includes a full-screen-in-the-HUD quiz mode with short-answer grading, score tracking, pause/resume navigation, and an explicit End Quiz control.
- Opens stopped and provides a clear Start/Stop button, so nothing is sent until you press Start.
- Includes A− and A+ controls for text size.
- Uses Ollama locally, with no OpenAI account, API key, tokens, or usage charges.
- Limits selections to 12,000 characters and avoids resending the same selection.

## Easy installation

1. Extract the ZIP before opening anything inside it.
2. Double-click `Install Highlenser.bat`.
3. Setup checks whether Ollama and the Microsoft .NET 8 SDK are installed.
4. If either is missing, its official download page opens automatically. Install the missing items, open Ollama once, and run `Install Highlenser.bat` again.
5. Setup downloads `qwen2.5-coder:3b` and creates a **HighLenser** desktop shortcut.
6. Double-click **HighLenser** on your desktop.
7. Press **Start**, then highlight text in a browser, VS Code, Notepad, or another supported app.
8. Press **Stop** whenever you do not want the app reading selections.

## Create the public one-file installer

### Automatic GitHub build (recommended)

1. Open the repository's **Actions** tab.
2. Select **Build Windows Installer**.
3. Open the newest successful run.
4. Download **HighLenser-Windows-Installer** from the Artifacts section.
5. Extract that small download and share `HighLenser-Setup.exe`.

Pushing a version tag such as `v1.0.0` also creates a GitHub Release and attaches
`HighLenser-Setup.exe` as a direct public download.

### Build on a Windows computer

The ZIP is the developer package. To make the single file that other people should download:

1. On the developer's Windows computer, extract this ZIP.
2. Double-click `Build HighLenser Installer.bat`.
3. The builder checks for the .NET 8 SDK and installs the free Inno Setup compiler when needed.
4. Wait for the build to finish.
5. Open the new `Release` folder.
6. Share `HighLenser-Setup.exe` with users. Do not share the developer ZIP as the public download.

People who receive `HighLenser-Setup.exe` only double-click it and follow the normal installation window. They do not need the .NET SDK or the source-code folder.

When reinstalling or updating, the installer automatically closes an older running HighLenser or Selection Lens process before replacing the app.

To create a normal Windows executable folder, run:

`dotnet publish SelectionLens.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`

The result will be in `bin\Release\net8.0-windows\win-x64\publish`.

## Important limitation

Automatic selection detection depends on the other program exposing its selected text through Windows UI Automation. It should work in many common programs, but protected text fields, games, terminals, and some custom-rendered apps may block access. A future fallback can use a keyboard shortcut and clipboard copy for those programs.

## Cost and privacy

Highlighted text is sent only to Ollama at `localhost:11434` and processed on your laptop. There are no per-use token charges. The model download requires internet access, but explanations can run offline afterward.
