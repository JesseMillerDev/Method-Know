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
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var article = await dbContext.Articles.FindAsync(articleId);
                if (article == null)
                {
                    _logger.LogWarning("Article {Id} not found, skipping embedding generation.", articleId);
                    continue;
                }

                var textToEmbed = $"{article.Title} {article.Content}";
                var vector = await embeddingService.GenerateEmbeddingAsync(textToEmbed);

                await vectorService.UpdateArticleEmbeddingAsync(articleId, vector);
                
                _logger.LogInformation("Completed embedding for article {Id}", articleId);
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
