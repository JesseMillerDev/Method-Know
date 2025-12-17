using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Know.ApiService.Services;

public class GeminiService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GeminiService> _logger;
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta";

    public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Gemini:ApiKey"] ?? throw new InvalidOperationException("Gemini:ApiKey is not configured.");
        _logger = logger;
    }

    public async Task<List<string>> GenerateTagsAsync(string content)
    {
        var prompt = $@"Identify 2-5 general, high-level technical topics, categories, or domains that the following text belongs to.
Avoid overly specific terms; prefer broader categories (e.g., 'Web Development' instead of 'React Hooks', 'History' instead of 'Bixby Letter').
Output ONLY a JSON array of strings.
Do NOT output any other text, explanation, or markdown formatting.

Text:
{content}";

        var result = await GenerateContentAsync("gemini-3-flash-preview", prompt);
        return ParseTags(result);
    }

    public async Task<string> GenerateSummaryAsync(string content)
    {
        var prompt = $@"Create a concise 2-sentence summary of the following technical text.
Do not include any introductory text. Just return the summary itself.

Text:
{content}";

        return await GenerateContentAsync("gemini-3-flash-preview", prompt);
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text)
    {
        var url = $"{BaseUrl}/models/gemini-embedding-001:embedContent?key={_apiKey}";
        
        var requestBody = new
        {
            model = "models/gemini-embedding-001",
            content = new { parts = new[] { new { text = text } } }
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<EmbeddingResponse>();
        return result?.Embedding?.Values ?? Array.Empty<float>();
    }

    private async Task<string> GenerateContentAsync(string model, string prompt)
    {
        var url = $"{BaseUrl}/models/{model}:generateContent?key={_apiKey}";
        
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = 1000,
                response_mime_type = "application/json"
            }
        };

        var response = await _httpClient.PostAsJsonAsync(url, requestBody);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>();
        return result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
    }

    private List<string> ParseTags(string output)
    {
        try
        {
            // Clean up potential markdown
            var json = output.Replace("```json", "").Replace("```", "").Trim();
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse tags from Gemini output: {Output}", output);
            return new List<string>();
        }
    }

    private class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate>? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonPropertyName("content")]
        public Content? Content { get; set; }
    }

    private class Content
    {
        [JsonPropertyName("parts")]
        public List<Part>? Parts { get; set; }
    }

    private class Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    private class EmbeddingResponse
    {
        [JsonPropertyName("embedding")]
        public EmbeddingData? Embedding { get; set; }
    }

    private class EmbeddingData
    {
        [JsonPropertyName("values")]
        public float[]? Values { get; set; }
    }
}
