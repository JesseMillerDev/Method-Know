using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace Know.ApiService.Services;

public class GeminiChatClient : IChatClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelId;

    public GeminiChatClient(HttpClient httpClient, string apiKey, string modelId = "gemini-3-pro-preview")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _modelId = modelId;
    }

    public ChatClientMetadata Metadata => new("google", new Uri("https://generativelanguage.googleapis.com/v1beta/"), _modelId);

    public object? GetService(Type serviceType, object? serviceKey = null) => 
        serviceType == typeof(GeminiChatClient) ? this : null;

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var messagesList = chatMessages.ToList();
        var request = CreateRequest(messagesList, options);
        var response = await _httpClient.PostAsync(
            $"https://generativelanguage.googleapis.com/v1beta/models/{_modelId}:generateContent?key={_apiKey}",
            new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json"),
            cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(json);

        var text = geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";
        
        return new ChatResponse(new[] { new ChatMessage(ChatRole.Assistant, text) });
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Fallback to non-streaming for now as before
        var completion = await GetResponseAsync(chatMessages, options, cancellationToken);
        var text = completion.Messages.FirstOrDefault()?.Text;
        
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = new[] { new TextContent(text) }
        };
    }

    private object CreateRequest(IList<ChatMessage> chatMessages, ChatOptions? options)
    {
        // Map roles: System -> user (with instruction), User -> user, Assistant -> model
        // Gemini doesn't strictly support "system" role in messages list in the same way OpenAI does, 
        // but supports system_instruction.
        
        var systemMessage = chatMessages.FirstOrDefault(m => m.Role == ChatRole.System);
        object? systemInstruction = null;
        if (systemMessage != null)
        {
            systemInstruction = new { parts = new[] { new { text = systemMessage.Text } } };
        }

        var mappedContents = chatMessages
            .Where(m => m.Role != ChatRole.System)
            .Select(m => new
            {
                role = m.Role == ChatRole.User ? "user" : "model",
                parts = new[] { new { text = m.Text } }
            }).ToList();

        return new
        {
            contents = mappedContents,
            system_instruction = systemInstruction,
            generationConfig = new
            {
                temperature = options?.Temperature ?? 0.7,
                maxOutputTokens = options?.MaxOutputTokens ?? 2048
            }
        };
    }

    public void Dispose() => _httpClient.Dispose();

    // Helper classes for JSON deserialization
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
}
