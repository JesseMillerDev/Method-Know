namespace Know.ApiService.Services;

public class TaggingService
{
    private readonly GeminiService _geminiService;
    private readonly ILogger<TaggingService> _logger;

    public TaggingService(GeminiService geminiService, ILogger<TaggingService> logger)
    {
        _geminiService = geminiService;
        _logger = logger;
    }

    public async Task<List<string>> GenerateTagsAsync(string content)
    {
        try
        {
            return await _geminiService.GenerateTagsAsync(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate tags via Gemini.");
            return new List<string>();
        }
    }

    public async Task<string> GenerateSummaryAsync(string content)
    {
        try
        {
            return await _geminiService.GenerateSummaryAsync(content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate summary via Gemini.");
            return "";
        }
    }
}
