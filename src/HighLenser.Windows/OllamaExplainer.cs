using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SelectionLens;

public sealed record QuizQuestionData(string Question, string[] Choices, int CorrectIndex, string Explanation);
public sealed record QuizSetData(List<QuizQuestionData> Questions, bool Limited);

public sealed class OllamaExplainer
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(30) };
    private const string VisionModel = "gemma3:4b";

    public Task<string> SnapshotNotesAsync(byte[] pngBytes, CancellationToken cancellationToken)
    {
        const string prompt = "Read all useful text and visual information in this screenshot. Turn it into clear study notes with short headings and bullets. Preserve important names, numbers, formulas, dates, definitions, and relationships. Do not describe the screenshot itself and do not invent missing information.";
        return SendVisionAsync(prompt, Convert.ToBase64String(pngBytes), cancellationToken, true);
    }

    public Task<string> ExplainAsync(string selectedText, string model, string summaryMode, CancellationToken cancellationToken)
    {
        string prompt = $"""
{FormatInstructions(summaryMode)}

If the content is code, explain its purpose, important logic, inputs and outputs, and likely issues. If it is regular text, explain its meaning. Base the answer on the selected content.

SELECTED CONTENT:
{selectedText}
""";
        return GenerateAsync(prompt, model, summaryMode, cancellationToken);
    }

    public Task<string> ExploreAsync(string topic, string originalSelection, string currentExplanation, string model, string summaryMode, CancellationToken cancellationToken)
    {
        string prompt = $"""
The user selected a word, phrase, or sentence because they do not understand it. Teach the selected part as if this is the first time they have ever seen it.

Do not use the normal KEY TAKEAWAYS, WHY YOU SHOULD KNOW THIS, or SUMMARY format. Instead:
- Begin with a direct, plain-language meaning of the selected part.
- If it is a sentence, unpack it piece by piece.
- Explain exactly how it connects to the full notes and original material supplied below.
- Define any other unfamiliar words needed to understand it.
- Give one or more simple, concrete examples. For an abstract idea, use an everyday example.
- Be detailed and patient, but use simple language and do not assume prior knowledge.
- Stay within the context of the notes. Mention when a word could have other meanings but explain the meaning used here.

SELECTED PART THE USER NEEDS HELP WITH:
{topic}

ORIGINAL SELECTED CONTENT:
{Limit(originalSelection, 6000)}

FULL CURRENT NOTES:
{Limit(currentExplanation, 10000)}
""";
        return SendAsync(prompt, model, 1400, cancellationToken);
    }

    public Task<string> FollowUpAsync(string request, string originalSelection, string currentExplanation, string model, string summaryMode, CancellationToken cancellationToken)
    {
        string prompt = $"""
{FormatInstructions(summaryMode)}

Update the explanation to satisfy the user's request. Return a complete revised explanation, not only the new portion. Keep accurate useful information from the earlier explanation and incorporate the requested additions or changes.

USER REQUEST:
{request}

ORIGINAL SELECTED CONTENT:
{Limit(originalSelection, 8000)}

CURRENT EXPLANATION:
{Limit(currentExplanation, 10000)}
""";
        return GenerateAsync(prompt, model, summaryMode, cancellationToken);
    }

    public async Task<string> CreateTitleAsync(string selectedText, string explanation, string model, CancellationToken cancellationToken)
    {
        string prompt = $"""
Create a short, recognizable topic title for saved study information. Identify the central subject instead of copying the opening words. Use 2 to 6 words, title case, and output only the title with no quotation marks or punctuation.

SELECTED INFORMATION:
{Limit(selectedText, 5000)}

SUMMARY:
{Limit(explanation, 5000)}
""";
        string raw = await SendAsync(prompt, model, 30, cancellationToken);
        string[] lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return "";
        string title = lines[0].Trim().Trim('"', '\'', '.', ':', '-', ' ');
        if (title.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)) title = title[6..].Trim();
        return title.Length <= 60 ? title : title[..60].TrimEnd() + "…";
    }

    public async Task<QuizSetData> CreateQuizSetAsync(string summarizedInformation, string model, CancellationToken cancellationToken)
    {
        const string jsonShape = "{\"questions\":[{\"question\":\"Question text\",\"choices\":[\"Choice A\",\"Choice B\",\"Choice C\",\"Choice D\"],\"correct_index\":0,\"explanation\":\"Why the answer is correct\"}]}";
        string prompt = $"""
Create a bank of multiple-choice questions using only the summarized information below. Make every question test a genuinely different fact, concept, relationship, cause, effect, or example. Rewording the same idea does not count as a different question. Create up to 8 questions, but create fewer—even only one—when the information does not support more unique questions. Each question needs exactly four plausible choices and one correct answer.

Return only valid JSON in this exact shape, with no markdown:
{jsonShape}

For every question, correct_index must be an integer from 0 to 3. Never invent facts not present in the summary.

SUMMARIZED INFORMATION:
{Limit(summarizedInformation, 12000)}

""";
        string raw = await SendAsync(prompt, model, 1800, cancellationToken);
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("The local model did not create a valid quiz question. Try again.");
        using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
        var unique = new List<QuizQuestionData>();
        foreach (var item in doc.RootElement.GetProperty("questions").EnumerateArray())
        {
            string question = item.GetProperty("question").GetString()?.Trim() ?? "";
            string[] choices = item.GetProperty("choices").EnumerateArray().Select(choice => choice.GetString()?.Trim() ?? "").ToArray();
            int correctIndex = item.GetProperty("correct_index").GetInt32();
            string explanation = item.TryGetProperty("explanation", out var reason) ? reason.GetString()?.Trim() ?? "" : "";
            if (string.IsNullOrWhiteSpace(question) || choices.Length != 4 || choices.Any(string.IsNullOrWhiteSpace) || correctIndex is < 0 or > 3) continue;
            var candidate = new QuizQuestionData(question, choices, correctIndex, explanation);
            if (!unique.Any(existing => QuestionsAreSimilar(existing.Question, candidate.Question))) unique.Add(candidate);
            if (unique.Count == 8) break;
        }
        if (unique.Count == 0) throw new InvalidOperationException("There was not enough information to create a quiz question.");
        return new QuizSetData(unique, unique.Count < 8);
    }

    public async Task<QuizSetData> ValidateUniqueQuizSetAsync(QuizSetData set, string model, CancellationToken cancellationToken)
    {
        if (set.Questions.Count <= 1) return set;

        string questionList = JsonSerializer.Serialize(set.Questions.Select((question, index) => new
        {
            index,
            question = question.Question,
            correct_answer = question.Choices[question.CorrectIndex]
        }));
        const string jsonShape = "{\"keep_indices\":[0,2,4]}";
        string prompt = $"""
Review the completed quiz bank below after generation. Remove repeated or incredibly similar questions. Questions count as duplicates when they test the same underlying fact or concept, even if they use different wording, reverse the wording, or use different answer choices. Keep the strongest question from each distinct concept.

Return only valid JSON in this shape:
{jsonShape}

The indices must refer to the supplied list and remain in their original order. Keep at least one question.

QUIZ BANK:
{questionList}
""";

        string raw = await SendAsync(prompt, model, 220, cancellationToken);
        int start = raw.IndexOf('{');
        int end = raw.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidOperationException("The quiz similarity check did not return a valid result. Try starting the quiz again.");
        using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
        HashSet<int> keep = doc.RootElement.GetProperty("keep_indices").EnumerateArray()
            .Select(item => item.GetInt32()).Where(index => index >= 0 && index < set.Questions.Count).ToHashSet();
        if (keep.Count == 0) keep.Add(0);

        var checkedQuestions = set.Questions.Where((question, index) => keep.Contains(index)).ToList();
        var finalQuestions = new List<QuizQuestionData>();
        foreach (var question in checkedQuestions)
            if (!finalQuestions.Any(existing => QuestionsAreSimilar(existing.Question, question.Question))) finalQuestions.Add(question);

        return new QuizSetData(finalQuestions, set.Limited || finalQuestions.Count < set.Questions.Count || finalQuestions.Count < 8);
    }

    private static bool QuestionsAreSimilar(string first, string second)
    {
        HashSet<string> a = MeaningfulWords(first);
        HashSet<string> b = MeaningfulWords(second);
        if (a.Count == 0 || b.Count == 0) return string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);
        int shared = a.Intersect(b).Count();
        return (double)shared / Math.Min(a.Count, b.Count) >= 0.58;
    }

    private static HashSet<string> MeaningfulWords(string text)
    {
        string[] ignored = { "what", "which", "when", "where", "does", "this", "that", "from", "about", "according", "following", "best", "most", "main", "primary" };
        return Regex.Matches(text.ToLowerInvariant(), "[a-z0-9]+")
            .Select(match => match.Value)
            .Where(word => word.Length >= 4 && !ignored.Contains(word))
            .ToHashSet();
    }

    private static string FormatInstructions(string summaryMode)
    {
        string modeInstruction = summaryMode switch
        {
            "InDepth" => "IN-DEPTH EXPLANATION: Assume the reader has never seen this topic. Explain every important idea step by step, define unfamiliar terms, include examples, and do not skip the reasoning between ideas.",
            "StudyNotes" => "STUDY NOTES: Create clean, concise, copy-ready study notes using short bullets, labels, definitions, and important relationships. Avoid filler and repetition.",
            _ => "SUMMARY: Give a balanced explanation in simple language with the main meaning and enough detail to understand it without becoming overly long."
        };

        return $"""
Always organize the answer in this exact order:

KEY TAKEAWAYS
- Give the most important points as clear bullets.

WHY YOU SHOULD KNOW THIS
- Explain why it matters, when it is useful, or how it connects to the larger topic.

{modeInstruction}

Use simple, direct language.
""";
    }

    private static async Task<string> GenerateAsync(string prompt, string model, string summaryMode, CancellationToken cancellationToken)
        => await SendAsync(prompt, model, summaryMode == "InDepth" ? 1400 : 850, cancellationToken);

    private static async Task<string> SendAsync(string prompt, string model, int maxTokens, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync("http://localhost:11434/api/generate", new
            {
                model,
                prompt,
                stream = false,
                options = new { num_predict = maxTokens, temperature = 0.2 }
            }, cancellationToken);

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string details = TryReadError(json);
                if (details.Contains("model", StringComparison.OrdinalIgnoreCase) &&
                    details.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadModelAsync(model, cancellationToken);
                    return await SendAsync(prompt, model, maxTokens, cancellationToken);
                }
                throw new InvalidOperationException($"Ollama could not complete the request. {details}");
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("response", out var result) && !string.IsNullOrWhiteSpace(result.GetString()))
                return result.GetString()!;
            return "Ollama returned an empty explanation.";
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Ollama is not running. Open Ollama from the Start menu, then try again.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The local model took too long to respond. Try a shorter selection.");
        }
    }

    private static async Task<string> SendVisionAsync(string prompt, string imageBase64, CancellationToken cancellationToken, bool allowDownload)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync("http://localhost:11434/api/generate", new
            {
                model = VisionModel,
                prompt,
                images = new[] { imageBase64 },
                stream = false,
                options = new { num_predict = 1100, temperature = 0.1 }
            }, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string details = TryReadError(json);
                if (allowDownload && details.Contains("model", StringComparison.OrdinalIgnoreCase) && details.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadModelAsync(VisionModel, cancellationToken);
                    return await SendVisionAsync(prompt, imageBase64, cancellationToken, false);
                }
                throw new InvalidOperationException($"Ollama could not read the snapshot. {details}");
            }
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("response", out var result) && !string.IsNullOrWhiteSpace(result.GetString())
                ? result.GetString()! : "No readable information was found in that snapshot.";
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("HighLenser cannot reach Ollama. Check that Ollama is open. If the vision model is still downloading, also check your Wi-Fi and try again.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException("The snapshot took too long to process. Check your connection if this is the first snapshot, then try a smaller area.");
        }
    }

    private static async Task DownloadModelAsync(string model, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.PostAsJsonAsync("http://localhost:11434/api/pull", new
            {
                name = model,
                stream = false
            }, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HighLenser could not download its AI model. Check your internet connection and try again. {TryReadError(json)}");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("HighLenser cannot download its AI model. Check that Ollama is open and your computer is connected to the internet, then try again.");
        }
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    private static string TryReadError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? "" : "";
        }
        catch (JsonException) { return ""; }
    }
}
