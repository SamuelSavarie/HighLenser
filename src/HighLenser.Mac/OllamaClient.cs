using System.Net.Http.Json;
using System.Text.Json;

namespace HighLenser.Mac;

public sealed class OllamaClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private const string Model = "qwen2.5-coder:3b";

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

    private static async Task DownloadModelAsync(CancellationToken token)
    {
        using var response = await Http.PostAsJsonAsync("http://localhost:11434/api/pull", new
        {
            name = Model,
            stream = false
        }, token);
        string json = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HighLenser could not download its AI model. {ReadError(json)}");
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
