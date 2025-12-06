using Dapper;
using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Services;

public enum OperationResult
{
    Success,
    NotFound,
    Forbidden
}

public class VectorDbService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<VectorDbService> _logger;

    public VectorDbService(AppDbContext dbContext, ILogger<VectorDbService> logger)
    {
        _dbContext = dbContext;
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
            await InsertVectorAsync(article.Id, vector);
        }

        return article;
    }

    public async Task UpdateArticleEmbeddingAsync(int articleId, float[] vector)
    {
        await InsertVectorAsync(articleId, vector);
    }

    private async Task InsertVectorAsync(int articleId, float[] vector)
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
        } catch {}

        // Convert float[] to byte[] for sqlite-vec
        var vectorBytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, vectorBytes, 0, vectorBytes.Length);

        // Check if vector exists first (upsert logic)
        // For simplicity, we'll just delete and insert, or use INSERT OR REPLACE if table supports it.
        // vec_articles doesn't have a PK on article_id usually unless defined. 
        // Let's assume we just want to insert. If it exists, we might want to delete old one first.
        
        var deleteSql = "DELETE FROM vec_articles WHERE article_id = @Id";
        await connection.ExecuteAsync(deleteSql, new { Id = articleId });

        var sql = "INSERT INTO vec_articles(article_id, embedding) VALUES (@Id, @Vector)";
        var rows = await connection.ExecuteAsync(sql, new { Id = articleId, Vector = vectorBytes });
        _logger.LogInformation("Inserted vector for article {Id}. Rows affected: {Rows}", articleId, rows);
    }

    public async Task<bool> VoteAsync(int articleId, string userId, int voteValue)
    {
        var existingVote = await _dbContext.ArticleVotes
            .FirstOrDefaultAsync(v => v.ArticleId == articleId && v.UserId == userId);

        var article = await _dbContext.Articles.FindAsync(articleId);
        if (article == null) return false;

        if (existingVote != null)
        {
            // If clicking same vote button, toggle off (remove vote)
            if (existingVote.VoteValue == voteValue)
            {
                _dbContext.ArticleVotes.Remove(existingVote);
                
                // Revert stats
                if (voteValue == 1) article.Upvotes--;
                else if (voteValue == -1) article.Downvotes--;
                article.VoteCount--;
            }
            else
            {
                // Changing vote (e.g. Up -> Down)
                existingVote.VoteValue = voteValue;
                existingVote.VotedAt = DateTime.UtcNow; // Update timestamp

                if (voteValue == 1) 
                {
                    article.Upvotes++;
                    article.Downvotes--;
                }
                else 
                {
                    article.Downvotes++;
                    article.Upvotes--;
                }
                // VoteCount stays same (1 user = 1 vote count)
            }
        }
        else
        {
            // New vote
            _dbContext.ArticleVotes.Add(new ArticleVote
            {
                ArticleId = articleId,
                UserId = userId,
                VoteValue = voteValue
            });
            
            if (voteValue == 1) article.Upvotes++;
            else if (voteValue == -1) article.Downvotes++;
            article.VoteCount++;
        }

        // Recalculate Score
        article.Score = article.Upvotes - article.Downvotes;

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
                    .ToDictionaryAsync(v => v.ArticleId, v => v.VoteValue);
                
                foreach (var article in results)
                {
                    if (userVotes.TryGetValue(article.Id, out int voteValue))
                    {
                        article.IsVoted = true;
                        article.UserVoteValue = voteValue;
                    }
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
            .ToDictionaryAsync(v => v.ArticleId, v => v.VoteValue);
            
        foreach (var article in articles)
        {
            if (userVotes.TryGetValue(article.Id, out int voteValue))
            {
                article.IsVoted = true;
                article.UserVoteValue = voteValue;
            }
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
                .ToDictionaryAsync(v => v.ArticleId, v => v.VoteValue);
            
            foreach (var article in articles)
            {
                if (userVotes.TryGetValue(article.Id, out int voteValue))
                {
                    article.IsVoted = true;
                    article.UserVoteValue = voteValue;
                }
            }
        }

        return articles;
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

        existingArticle.Title = updatedArticle.Title;
        existingArticle.Content = updatedArticle.Content;
        existingArticle.Category = updatedArticle.Category;
        existingArticle.Tags = updatedArticle.Tags;
        existingArticle.Summary = updatedArticle.Summary;

        await _dbContext.SaveChangesAsync();

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        if (vector != null)
        {
            try
            {
                try
                {
                    var extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "vec0.dylib");
                    ((Microsoft.Data.Sqlite.SqliteConnection)connection).LoadExtension(extensionPath);
                }
                catch { }

                var vectorBytes = new byte[vector.Length * sizeof(float)];
                Buffer.BlockCopy(vector, 0, vectorBytes, 0, vectorBytes.Length);

                var sql = "UPDATE vec_articles SET embedding = @Vector WHERE article_id = @Id";
                var rows = await connection.ExecuteAsync(sql, new { Id = existingArticle.Id, Vector = vectorBytes });

                if (rows == 0)
                {
                    await connection.ExecuteAsync("INSERT INTO vec_articles(article_id, embedding) VALUES (@Id, @Vector)", new { Id = existingArticle.Id, Vector = vectorBytes });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update vector for article {Id}", existingArticle.Id);
            }
        }

        return (OperationResult.Success, existingArticle);
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

        await _dbContext.SaveChangesAsync();

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        try
        {
            await connection.ExecuteAsync("DELETE FROM vec_articles WHERE article_id = @Id", new { Id = id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vector for article {Id}", id);
        }

        return OperationResult.Success;
    }
    public async Task<List<string>> GetAllTagsAsync()
    {
        var tagsJson = await _dbContext.Articles
            .Select(a => a.Tags)
            .ToListAsync();

        var allTags = tagsJson
            .SelectMany(t => 
            {
                try 
                {
                    return System.Text.Json.JsonSerializer.Deserialize<List<string>>(t) ?? new List<string>();
                }
                catch
                {
                    return new List<string>();
                }
            })
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        return allTags;
    }
}
