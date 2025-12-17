using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Services;

public class EmbeddingBackgroundService : BackgroundService
{
    private readonly BackgroundQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EmbeddingBackgroundService> _logger;

    public EmbeddingBackgroundService(
        BackgroundQueue queue, 
        IServiceScopeFactory scopeFactory,
        ILogger<EmbeddingBackgroundService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Embedding Background Service is starting.");
        
        // Limit concurrency to avoid hitting rate limits too hard
        var semaphore = new SemaphoreSlim(5);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var articleId = await _queue.DequeueAsync(stoppingToken);
                
                // Fire and forget the task, but track it via the semaphore
                _ = Task.Run(async () => 
                {
                    await semaphore.WaitAsync(stoppingToken);
                    try 
                    {
                        await ProcessArticleAsync(articleId, stoppingToken);
                    }
                    finally 
                    {
                        semaphore.Release();
                    }
                }, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred dequeuing background task.");
            }
        }
    }

    private async Task ProcessArticleAsync(int articleId, CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Processing article {Id}", articleId);

            using var scope = _scopeFactory.CreateScope();
            var articleService = scope.ServiceProvider.GetRequiredService<ArticleService>();
            var geminiService = scope.ServiceProvider.GetRequiredService<GeminiService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var article = await dbContext.Articles.FindAsync(new object[] { articleId }, stoppingToken);
            if (article == null)
            {
                _logger.LogWarning("Article {Id} not found, skipping processing.", articleId);
                return;
            }

            // 1. Generate Tags if missing or empty
            List<string> tags = article.TagList ?? new List<string>();
            if (!tags.Any())
            {
                _logger.LogInformation("Generating tags for article {Id} using Gemini...", articleId);
                tags = await geminiService.GenerateTagsAsync($"{article.Title} {article.Content}");
            }

            // 2. Generate Summary if missing or empty
            string summary = article.Summary;
            if (string.IsNullOrWhiteSpace(summary))
            {
                _logger.LogInformation("Generating summary for article {Id} using Gemini...", articleId);
                summary = await geminiService.GenerateSummaryAsync(article.Content);
            }

            // Save tags and summary via ArticleService to update cache
            await articleService.UpdateArticleTagsAndSummaryAsync(articleId, tags, summary);

            // 3. Generate Embedding
            _logger.LogInformation("Generating embedding for article {Id} using Gemini...", articleId);
            var textToEmbed = $"{article.Title} {article.Content}";
            var vector = await geminiService.GenerateEmbeddingAsync(textToEmbed);

            if (vector.Length > 0)
            {
                await articleService.UpdateArticleEmbeddingAsync(articleId, vector);
            }
            
            _logger.LogInformation("Completed processing for article {Id}", articleId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred processing article {Id}", articleId);
        }
    }
}
