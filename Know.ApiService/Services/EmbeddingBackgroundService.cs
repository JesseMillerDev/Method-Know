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

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var articleId = await _queue.DequeueAsync(stoppingToken);

                _logger.LogInformation("Processing embedding for article {Id}", articleId);

                using var scope = _scopeFactory.CreateScope();
                var vectorService = scope.ServiceProvider.GetRequiredService<VectorDbService>();
                var embeddingService = scope.ServiceProvider.GetRequiredService<OnnxEmbeddingService>();
                var taggingService = scope.ServiceProvider.GetRequiredService<TaggingService>();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var article = await dbContext.Articles.FindAsync(articleId);
                if (article == null)
                {
                    _logger.LogWarning("Article {Id} not found, skipping processing.", articleId);
                    continue;
                }

                // 1. Generate Tags if missing or empty
                if (article.TagList == null || !article.TagList.Any())
                {
                    _logger.LogInformation("Generating tags for article {Id}...", articleId);
                    var tags = await taggingService.GenerateTagsAsync($"{article.Title} {article.Content}");
                    article.TagList = tags;
                }

                // 2. Generate Summary if missing or empty
                if (string.IsNullOrWhiteSpace(article.Summary))
                {
                    _logger.LogInformation("Generating summary for article {Id}...", articleId);
                    var summary = await taggingService.GenerateSummaryAsync(article.Content);
                    article.Summary = summary;
                }

                // Save changes to DB (tags/summary) before embedding
                await dbContext.SaveChangesAsync();

                // 3. Generate Embedding
                _logger.LogInformation("Generating embedding for article {Id}...", articleId);
                var textToEmbed = $"{article.Title} {article.Content}";
                var vector = await embeddingService.GenerateEmbeddingAsync(textToEmbed);

                await vectorService.UpdateArticleEmbeddingAsync(articleId, vector);
                
                _logger.LogInformation("Completed processing for article {Id}", articleId);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred executing embedding background task.");
            }
        }
    }
}
