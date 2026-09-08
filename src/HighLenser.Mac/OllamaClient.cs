using System.Net.Http.Json;
using System.Text.Json;

namespace HighLenser.Mac;

public sealed class OllamaClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private const string Model = "qwen2.5-coder:3b";
    private const string VisionModel = "gemma3:4b";

    public Task<string> SnapshotNotesAsync(byte[] pngBytes, CancellationToken token)
    {
        const string prompt = "Read all useful text and visual information in this screenshot. Turn it into clear study notes with short headings and bullets. Preserve important names, numbers, formulas, dates, definitions, and relationships. Do not describe the screenshot itself and do not invent missing information.";
        return SendVisionAsync(prompt, Convert.ToBase64String(pngBytes), token, true);
    }

    public async Task<string> ExplainAsync(string selectedText, string mode, CancellationToken token)
    {
        string detail = mode switch
        {
            "In Depth" => "Assume the reader is new to the topic. Explain every important idea step by step and include examples.",
            "Study Notes" => "Create concise, copy-ready study notes using headings, bullets, definitions, and relationships.",
            _ => "Give a clear summary in simple language with enough detail to understand the main meaning."
        };

        string prompt = $"""
Always organize the answer in this order:

KEY TAKEAWAYS
- Give the most important points as clear bullets.

WHY YOU SHOULD KNOW THIS
- Explain why it matters or when it is useful.

{detail}

If the content is code, explain its purpose, important logic, inputs, outputs, and likely issues.
Use simple, direct language and base the answer only on the selected content.

SELECTED CONTENT:
{selectedText}
""";

        try
        {
            using var response = await Http.PostAsJsonAsync("http://localhost:11434/api/generate", new
            {
                model = Model,
                prompt,
                stream = false,
                options = new { num_predict = mode == "In Depth" ? 1400 : 850, temperature = 0.2 }
            }, token);
            string json = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                string error = ReadError(json);
                if (error.Contains("model", StringComparison.OrdinalIgnoreCase) &&
                    error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadModelAsync(token);
                    return await ExplainAsync(selectedText, mode, token);
                }
                throw new InvalidOperationException(error);
            }
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString()?.Trim() ?? "Ollama returned an empty explanation.";
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("Ollama is not running. Open Ollama, then try again.");
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException("The local model took too long. Try a shorter selection.");
        }
    }

    public async Task<string> ExploreAsync(string topic, string originalContent, string fullNotes, CancellationToken token)
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

ORIGINAL CONTENT:
{Limit(originalContent, 6000)}

FULL CURRENT NOTES:
{Limit(fullNotes, 10000)}
""";

        try
        {
            using var response = await Http.PostAsJsonAsync("http://localhost:11434/api/generate", new
            {
                model = Model,
                prompt,
                stream = false,
                options = new { num_predict = 1400, temperature = 0.2 }
            }, token);
            string json = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException(ReadError(json));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString()?.Trim() ?? "Ollama returned an empty explanation.";
        }
        catch (HttpRequestException) { throw new InvalidOperationException("Ollama is not running. Open Ollama, then try again."); }
        catch (TaskCanceledException) when (!token.IsCancellationRequested) { throw new InvalidOperationException("The local model took too long. Try selecting a shorter part."); }
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    private static async Task DownloadModelAsync(CancellationToken token)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync("http://localhost:11434/api/pull", new
            {
                name = Model,
                stream = false
            }, token);
            string json = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HighLenser could not download its AI model. Check your internet connection and try again. {ReadError(json)}");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("HighLenser cannot download its AI model. Check that Ollama is open and your Mac is connected to the internet, then try again.");
        }
    }

    private static async Task<string> SendVisionAsync(string prompt, string image, CancellationToken token, bool allowDownload)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync("http://localhost:11434/api/generate", new
            {
                model = VisionModel,
                prompt,
                images = new[] { image },
                stream = false,
                options = new { num_predict = 1100, temperature = 0.1 }
            }, token);
            string json = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
            {
                string error = ReadError(json);
                if (allowDownload && error.Contains("model", StringComparison.OrdinalIgnoreCase) && error.Contains("not found", StringComparison.OrdinalIgnoreCase))
                {
                    await DownloadVisionModelAsync(token);
                    return await SendVisionAsync(prompt, image, token, false);
                }
                throw new InvalidOperationException($"Ollama could not read the snapshot. {error}");
            }
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("response").GetString()?.Trim() ?? "No readable information was found in that snapshot.";
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("HighLenser cannot reach Ollama. Check that Ollama is open. If the vision model is still downloading, also check your Wi-Fi and try again.");
        }
        catch (TaskCanceledException) when (!token.IsCancellationRequested)
        {
            throw new InvalidOperationException("The snapshot took too long to process. Check your connection if this is the first snapshot, then try a smaller area.");
        }
    }

    private static async Task DownloadVisionModelAsync(CancellationToken token)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync("http://localhost:11434/api/pull", new { name = VisionModel, stream = false }, token);
            string json = await response.Content.ReadAsStringAsync(token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"HighLenser could not download its snapshot model. Check your internet connection and try again. {ReadError(json)}");
        }
        catch (HttpRequestException)
        {
            throw new InvalidOperationException("HighLenser cannot download its snapshot model. Check that Ollama is open and your Mac is connected to the internet, then try again.");
        }
    }

    private static string ReadError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("error", out var error)
                ? error.GetString() ?? "Ollama could not complete the request."
                : "Ollama could not complete the request.";
        }
        catch { return "Ollama could not complete the request."; }
    }
}
