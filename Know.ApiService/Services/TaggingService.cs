using LLama;
using LLama.Common;
using LLama.Sampling;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace Know.ApiService.Services;

public class TaggingService : IDisposable
{
    private readonly string _modelPath;
    private readonly LLamaWeights? _model;
    private readonly LLamaContext? _context;
    private readonly InteractiveExecutor? _executor;
    private readonly ILogger<TaggingService> _logger;

    public TaggingService(IConfiguration configuration, IWebHostEnvironment env, ILogger<TaggingService> logger)
    {
        _logger = logger;
        // Assume model is in Models folder, similar to embedding model
        _modelPath = Path.Combine(env.ContentRootPath, "Models", "Llama-3.2-3B-Instruct-Q4_K_M.gguf"); 

        if (!File.Exists(_modelPath))
        {
            _logger.LogWarning("LLM Model not found at {Path}. Tagging will be disabled.", _modelPath);
            return;
        }

        var parameters = new ModelParams(_modelPath)
        {
            ContextSize = 2048,
            GpuLayerCount = 0 // CPU for now
        };

        _model = LLamaWeights.LoadFromFile(parameters);
    }

    public async Task<List<string>> GenerateTagsAsync(string content)
    {
        if (_model == null) return new List<string>();

        try
        {
            // Create a context for this specific request to ensure isolation
            var parameters = new ModelParams(_modelPath) { ContextSize = 2048, GpuLayerCount = 0 };
            using var context = _model.CreateContext(parameters);
            var executor = new InteractiveExecutor(context);

            // Llama 3 Prompt Format
            var prompt = $@"<|begin_of_text|><|start_header_id|>system<|end_header_id|>

You are a strict technical tagging assistant.
Identify the top 3-5 most relevant technical topics, technologies, or frameworks in the text.
Output ONLY a JSON array of strings.
Do NOT output any other text, explanation, or markdown formatting.
Do NOT use stop words (e.g. 'the', 'and', 'a').

Example Output: [""C#"", "".NET"", ""Web API""]<|eot_id|><|start_header_id|>user<|end_header_id|>

Text:
{content}

Tags:<|eot_id|><|start_header_id|>assistant<|end_header_id|>
";

            var inferenceParams = new InferenceParams()
            {
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.1f },
                AntiPrompts = new List<string> { "<|eot_id|>" },
                MaxTokens = 100
            };

            var result = "";
            await foreach (var text in executor.InferAsync(prompt, inferenceParams))
            {
                result += text;
            }

            return ParseAndNormalizeTags(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate tags (likely OOM). Skipping tagging.");
            return new List<string>();
        }
    }

    public async Task<string> GenerateSummaryAsync(string content)
    {
        if (_model == null) return "";

        try
        {
            // Create a context for this specific request to ensure isolation
            var parameters = new ModelParams(_modelPath) { ContextSize = 2048, GpuLayerCount = 0 };
            using var context = _model.CreateContext(parameters);
            var executor = new InteractiveExecutor(context);

            // Llama 3 Prompt Format
            var prompt = $@"<|begin_of_text|><|start_header_id|>system<|end_header_id|>

You are a helpful assistant that summarizes technical articles.
Create a concise 2-sentence summary of the following text.
Do not include any introductory text like ""Here is a summary"".
Just return the summary itself.<|eot_id|><|start_header_id|>user<|end_header_id|>

Text:
{content}

Summary:<|eot_id|><|start_header_id|>assistant<|end_header_id|>
";

            var inferenceParams = new InferenceParams()
            {
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.2f },
                AntiPrompts = new List<string> { "<|eot_id|>" },
                MaxTokens = 150
            };

            var result = "";
            await foreach (var text in executor.InferAsync(prompt, inferenceParams))
            {
                result += text;
            }

            return result.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate summary (likely OOM). Skipping summary.");
            return "";
        }
    }

    private List<string> ParseAndNormalizeTags(string llmOutput)
    {
        var tags = new List<string>();
        try
        {
            _logger.LogInformation("Raw LLM Tag Output: {Output}", llmOutput);

            // Clean up potential markdown code blocks
            var json = llmOutput.Replace("```json", "").Replace("```", "").Trim();
            
            // Try to find the JSON array if there's extra text
            var match = Regex.Match(json, @"\[.*?\]", RegexOptions.Singleline);
            if (match.Success)
            {
                json = match.Value;
                try 
                {
                    var parsed = JsonSerializer.Deserialize<List<string>>(json);
                    if (parsed != null)
                    {
                        tags.AddRange(parsed);
                    }
                }
                catch { /* Ignore invalid JSON inside brackets */ }
            }
            
            // If JSON parsing failed or returned nothing, try line-based parsing (bullets, numbered lists)
            if (tags.Count == 0)
            {
                var lines = llmOutput.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var clean = line.Trim();
                    // Remove leading bullets (- or *) and numbers (1.)
                    clean = Regex.Replace(clean, @"^[\-\*0-9\.]+\s*", "");
                    
                    // Remove quotes if the model outputted "Tag"
                    clean = clean.Trim('"', '\'');
                    
                    if (!string.IsNullOrWhiteSpace(clean))
                    {
                        // If line contains commas, split it (unless it looks like a sentence)
                        if (clean.Contains(",") && clean.Split(' ').Length < 5)
                        {
                             var parts = clean.Split(',');
                             foreach (var part in parts) tags.Add(part.Trim());
                        }
                        else
                        {
                            tags.Add(clean);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse tags from LLM output: {Output}", llmOutput);
        }

        return NormalizeTags(tags);
    }

    private List<string> NormalizeTags(IEnumerable<string> tags)
    {
        var normalized = new HashSet<string>();
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
        { 
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for", "of", "with", "by", 
            "is", "are", "was", "were", "be", "been", "being", "have", "has", "had", "do", "does", "did",
            "this", "that", "these", "those", "it", "its", "from", "as", "any", "until", "your", "notor", "desdev" // Added some from user screenshot
        };

        foreach (var tag in tags)
        {
            var cleanTag = tag.Trim();
            
            // Remove surrounding quotes if they got stuck
            cleanTag = cleanTag.Trim('"', '\'');

            if (string.IsNullOrWhiteSpace(cleanTag)) continue;
            
            // Filter out short tags (unless specific known ones like "C", "R", "Go" - though "Go" is dangerous)
            // Let's say min length 2, but allow "C" if it's exactly "C" (rarely used alone for C language vs C#)
            if (cleanTag.Length < 2 && cleanTag.ToUpper() != "C" && cleanTag.ToUpper() != "R") continue;

            // Filter out stop words
            if (stopWords.Contains(cleanTag)) continue;
            
            // Filter out tags that are too long (likely sentences)
            if (cleanTag.Length > 30) continue;

            // Filter out tags containing "Text:" or "Tags:"
            if (cleanTag.Contains("Text:", StringComparison.OrdinalIgnoreCase) || 
                cleanTag.Contains("Tags:", StringComparison.OrdinalIgnoreCase)) continue;

            cleanTag = ApplyAliases(cleanTag);
            
            if (!normalized.Contains(cleanTag))
            {
                normalized.Add(cleanTag);
            }
        }

        return normalized.ToList();
    }

    private string ApplyAliases(string tag)
    {
        // Simple normalization map
        var lower = tag.ToLowerInvariant();
        
        if (lower == "c#" || lower == "csharp" || lower == "c-sharp") return "C#";
        if (lower == ".net" || lower == "dotnet") return ".NET";
        if (lower == "ef core" || lower == "entity framework core") return "EF Core";
        if (lower == "ai" || lower == "artificial intelligence") return "AI";
        if (lower == "ml" || lower == "machine learning") return "Machine Learning";
        if (lower == "llm" || lower == "large language model") return "LLM";

        // Default: Title Case (simple implementation)
        // System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(lower);
        // But ToTitleCase doesn't handle all acronyms well. 
        // Let's just return the tag as is if no alias, but maybe capitalize first letter?
        // The LLM usually does a decent job. Let's trust the LLM for general casing but fix specific ones.
        return tag;
    }

    public void Dispose()
    {
        _context?.Dispose();
        _model?.Dispose();
    }
}
