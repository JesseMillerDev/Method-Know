using Dapper;
using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Services;

public class VectorDbService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<VectorDbService> _logger;

    public VectorDbService(AppDbContext dbContext, ILogger<VectorDbService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<Article> CreateArticleAsync(Article article, float[] vector)
    {
        // 1. Save to EF Core (Structured Data)
        _dbContext.Articles.Add(article);
        await _dbContext.SaveChangesAsync();

        // 2. Save to Vector Table (Raw SQL)
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        try
        {
            // Note: We need to ensure the extension is loaded for this connection if it wasn't the one in Initializer.
            // SQLite connections are usually pooled, but extensions might need reloading if the connection is fresh.
            // However, typically extensions are per-connection. 
            // For simplicity/robustness, we might want to try loading it again or assume the pool handles it?
            // Actually, SQLite extensions are NOT persistent across connections. 
            // We should probably load it here if we want to be safe, or rely on a connection interceptor.
            // For this implementation, I'll try to load it and swallow error if already loaded or fail if missing.
            // But since we are in a scoped service, we can just try loading it.
            
            // A better approach for production is a DbConnectionInterceptor, but for now:
             try 
             { 
                 var extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "vec0.dylib");
                 ((Microsoft.Data.Sqlite.SqliteConnection)connection).LoadExtension(extensionPath); 
             } catch {}

            // Convert float[] to byte[] for sqlite-vec
            var vectorBytes = new byte[vector.Length * sizeof(float)];
            Buffer.BlockCopy(vector, 0, vectorBytes, 0, vectorBytes.Length);

            var sql = "INSERT INTO vec_articles(article_id, embedding) VALUES (@Id, @Vector)";
            var rows = await connection.ExecuteAsync(sql, new { Id = article.Id, Vector = vectorBytes });
            _logger.LogInformation("Inserted vector for article {Id}. Rows affected: {Rows}", article.Id, rows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert vector for article {Id}", article.Id);
            // We might want to rollback the EF transaction here if strict consistency is needed.
            // For now, we'll log and keep the article.
        }

        return article;
    }

    public async Task<bool> ToggleVoteAsync(int articleId, string userId)
    {
        var existingVote = await _dbContext.ArticleVotes
            .FirstOrDefaultAsync(v => v.ArticleId == articleId && v.UserId == userId);

        var article = await _dbContext.Articles.FindAsync(articleId);
        if (article == null) return false;

        if (existingVote != null)
        {
            _dbContext.ArticleVotes.Remove(existingVote);
            article.VoteCount--;
        }
        else
        {
            _dbContext.ArticleVotes.Add(new ArticleVote
            {
                ArticleId = articleId,
                UserId = userId
            });
            article.VoteCount++;
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<IEnumerable<Article>> SearchAsync(float[] queryVector, int limit, string? userId = null)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }
        
        try 
        { 
            var extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "vec0.dylib");
            ((Microsoft.Data.Sqlite.SqliteConnection)connection).LoadExtension(extensionPath); 
        } 
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load vec0 extension.");
        }

        // Convert float[] to byte[] for sqlite-vec
        var queryVectorBytes = new byte[queryVector.Length * sizeof(float)];
        Buffer.BlockCopy(queryVector, 0, queryVectorBytes, 0, queryVectorBytes.Length);

        var sql = @"
            SELECT a.* 
            FROM vec_articles v 
            JOIN Articles a ON a.Id = v.article_id
            WHERE vec_distance_cosine(v.embedding, @Vector) < 1.0
            ORDER BY vec_distance_cosine(v.embedding, @Vector)
            LIMIT @Limit";

        try 
        {
            var results = (await connection.QueryAsync<Article>(sql, new { Vector = queryVectorBytes, Limit = limit })).ToList();
            
            if (userId != null && results.Any())
            {
                var articleIds = results.Select(a => a.Id).ToList();
                var userVotes = await _dbContext.ArticleVotes
                    .Where(v => v.UserId == userId && articleIds.Contains(v.ArticleId))
                    .Select(v => v.ArticleId)
                    .ToListAsync();
                
                foreach (var article in results)
                {
                    article.IsVoted = userVotes.Contains(article.Id);
                }
            }
            
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector search failed.");
            return Enumerable.Empty<Article>();
        }
    }

    public async Task<IEnumerable<Article>> GetArticlesByUserIdAsync(string userId)
    {
        var articles = await _dbContext.Articles
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();
            
        // Users always have voted for their own content? Or maybe not. 
        // Let's check actual votes.
        var articleIds = articles.Select(a => a.Id).ToList();
        var userVotes = await _dbContext.ArticleVotes
            .Where(v => v.UserId == userId && articleIds.Contains(v.ArticleId))
            .Select(v => v.ArticleId)
            .ToListAsync();
            
        foreach (var article in articles)
        {
            article.IsVoted = userVotes.Contains(article.Id);
        }
        
        return articles;
    }

    public async Task<IEnumerable<Article>> GetAllArticlesAsync(string? userId = null)
    {
        var articles = await _dbContext.Articles
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync();

        if (userId != null && articles.Any())
        {
            var articleIds = articles.Select(a => a.Id).ToList();
            var userVotes = await _dbContext.ArticleVotes
                .Where(v => v.UserId == userId && articleIds.Contains(v.ArticleId))
                .Select(v => v.ArticleId)
                .ToListAsync();
            
            foreach (var article in articles)
            {
                article.IsVoted = userVotes.Contains(article.Id);
            }
        }

        return articles;
    }
}
