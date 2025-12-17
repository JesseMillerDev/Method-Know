using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Services;

public class ArticleService
{
    private readonly AppDbContext _dbContext;
    private readonly VectorService _vectorService;
    private readonly VoteService _voteService;
    private readonly TagCacheService _tagCache;
    private readonly BackgroundQueue _queue;
    private readonly ILogger<ArticleService> _logger;

    public ArticleService(
        AppDbContext dbContext,
        VectorService vectorService,
        VoteService voteService,
        TagCacheService tagCache,
        BackgroundQueue queue,
        ILogger<ArticleService> logger)
    {
        _dbContext = dbContext;
        _vectorService = vectorService;
        _voteService = voteService;
        _tagCache = tagCache;
        _queue = queue;
        _logger = logger;
    }

    public async Task<Article> CreateArticleAsync(Article article, float[]? vector)
    {
        // 1. Save to EF Core (Structured Data)
        _dbContext.Articles.Add(article);
        await _dbContext.SaveChangesAsync();

        // 2. Save to Vector Table (Raw SQL) - ONLY if vector is provided
        if (vector != null)
        {
            await _vectorService.UpsertVectorAsync(article.Id, vector);
        }

        // 3. Update Tag Cache
        if (article.TagList.Any())
        {
            _tagCache.AddTags(article.TagList);
        }

        return article;
    }

    public async Task<(OperationResult Status, Article? Article)> UpdateArticleAsync(int id, Article updatedArticle, float[]? vector, string userId)
    {
        var existingArticle = await _dbContext.Articles.FindAsync(id);

        if (existingArticle == null)
        {
            return (OperationResult.NotFound, null);
        }

        if (!string.Equals(existingArticle.UserId, userId, StringComparison.Ordinal))
        {
            return (OperationResult.Forbidden, null);
        }

        var oldTags = existingArticle.TagList;

        existingArticle.Title = updatedArticle.Title;
        existingArticle.Content = updatedArticle.Content;
        existingArticle.Category = updatedArticle.Category;
        
        // Clear tags and summary to force regeneration
        existingArticle.Tags = "[]"; 
        existingArticle.Summary = null;

        // Update Tag Cache (remove old tags since we cleared them)
        // We don't add new tags yet because they are null
        _tagCache.RemoveTags(oldTags);

        await _dbContext.SaveChangesAsync();

        if (vector != null)
        {
            await _vectorService.UpsertVectorAsync(existingArticle.Id, vector);
        }

        // Queue for background processing (Tagging -> Summary -> Embedding)
        await _queue.EnqueueAsync(existingArticle.Id);

        return (OperationResult.Success, existingArticle);
    }

    public async Task UpdateArticleEmbeddingAsync(int articleId, float[] vector)
    {
        await _vectorService.UpsertVectorAsync(articleId, vector);
    }
    
    public async Task UpdateArticleTagsAndSummaryAsync(int articleId, List<string> tags, string summary)
    {
        var article = await _dbContext.Articles.FindAsync(articleId);
        if (article == null) return;

        article.TagList = tags;
        article.Summary = summary;

        // Update Tag Cache
        if (tags.Any())
        {
            _tagCache.AddTags(tags);
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task<OperationResult> DeleteArticleAsync(int id, string userId)
    {
        var article = await _dbContext.Articles.FindAsync(id);

        if (article == null)
        {
            return OperationResult.NotFound;
        }

        if (!string.Equals(article.UserId, userId, StringComparison.Ordinal))
        {
            return OperationResult.Forbidden;
        }

        var votes = _dbContext.ArticleVotes.Where(v => v.ArticleId == id);
        _dbContext.ArticleVotes.RemoveRange(votes);
        _dbContext.Articles.Remove(article);

        // Update Tag Cache
        if (article.TagList.Any())
        {
            _tagCache.RemoveTags(article.TagList);
        }

        await _dbContext.SaveChangesAsync();

        await _vectorService.DeleteVectorAsync(id);

        return OperationResult.Success;
    }

    public async Task<Article?> GetArticleByIdAsync(int id, string? userId = null)
    {
        var article = await _dbContext.Articles.FindAsync(id);

        if (article != null && userId != null)
        {
            await _voteService.EnrichArticlesWithUserVotesAsync(new[] { article }, userId);
        }

        return article;
    }

    public async Task<IEnumerable<Article>> GetAllArticlesAsync(string? userId = null, string[]? categories = null, string[]? tags = null, string? searchQuery = null, int page = 1, int pageSize = 20)
    {
        var query = _dbContext.Articles.AsQueryable();

        if (categories != null && categories.Any())
        {
            query = query.Where(a => categories.Contains(a.Category));
        }

        if (tags != null && tags.Any())
        {
            // See original comment in VectorDbService about in-memory filtering for tags
        }

        if (!string.IsNullOrEmpty(searchQuery))
        {
            query = query.Where(a => a.Title.ToLower().Contains(searchQuery.ToLower()) ||
                                     a.Content.ToLower().Contains(searchQuery.ToLower()));
        }

        query = query.OrderByDescending(a => a.CreatedAt);

        List<Article> articles;

        if (tags != null && tags.Any())
        {
            var allArticles = await query.ToListAsync();
            articles = allArticles
                .Select(a => new { Article = a, MatchCount = a.TagList.Count(t => tags.Contains(t)) })
                .Where(x => x.MatchCount > 0)
                .OrderByDescending(x => x.MatchCount)
                .ThenByDescending(x => x.Article.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => x.Article)
                .ToList();
        }
        else
        {
            articles = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        if (userId != null && articles.Any())
        {
            await _voteService.EnrichArticlesWithUserVotesAsync(articles, userId);
        }

        return articles;
    }

    public async Task<IEnumerable<Article>> GetArticlesByUserIdAsync(string userId, string? category = null, string? searchQuery = null)
    {
        var query = _dbContext.Articles
            .Where(a => a.UserId == userId);

        if (!string.IsNullOrEmpty(category) && !category.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(a => a.Category == category);
        }

        if (!string.IsNullOrEmpty(searchQuery))
        {
            query = query.Where(a => a.Title.ToLower().Contains(searchQuery.ToLower()) ||
                                     a.Content.ToLower().Contains(searchQuery.ToLower()));
        }

        var articles = await query
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        await _voteService.EnrichArticlesWithUserVotesAsync(articles, userId);

        return articles;
    }

    public async Task<IEnumerable<Article>> SearchAsync(float[] queryVector, int limit, string? userId = null)
    {
        var results = await _vectorService.SearchAsync(queryVector, limit);

        if (userId != null && results.Any())
        {
            await _voteService.EnrichArticlesWithUserVotesAsync(results, userId);
        }

        return results;
    }
}
