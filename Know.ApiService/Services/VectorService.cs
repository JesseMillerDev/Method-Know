using Dapper;
using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Services;

public class VectorService
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<VectorService> _logger;
    private readonly IConfiguration _configuration;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initAttempted;
    private bool _vectorEnabled;

    public VectorService(AppDbContext dbContext, ILogger<VectorService> logger, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _logger = logger;
        _configuration = configuration;
    }

    private string GetExtensionPath()
    {
        // Allow override via configuration
        var configPath = _configuration["VectorDb:LibraryPath"];
        if (!string.IsNullOrEmpty(configPath))
        {
            return configPath;
        }

        string fileName;
        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
        {
            fileName = "vec0.dll";
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
        {
            fileName = "vec0.dylib";
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
        {
            fileName = "vec0.so";
        }
        else
        {
            // Fallback or throw? Let's default to linux/mac style or just throw.
            // For now, default to dylib as it was before, or maybe throw PlatformNotSupportedException
            // But to be safe let's just default to what it was if we can't detect, or maybe just log warning.
            // Let's stick to the plan: detect OS.
            fileName = "vec0.dylib"; // Default fallback
        }

        return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", fileName);
    }

    private bool IsVectorDisabledByConfig()
    {
        var enabled = _configuration["VectorDb:Enabled"];
        return string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> EnsureVectorReadyAsync()
    {
        if (_initAttempted)
        {
            return _vectorEnabled;
        }

        await _initLock.WaitAsync();
        try
        {
            if (_initAttempted)
            {
                return _vectorEnabled;
            }

            _initAttempted = true;

            if (IsVectorDisabledByConfig())
            {
                _logger.LogWarning("Vector search is disabled via configuration.");
                _vectorEnabled = false;
                return false;
            }

            var connection = _dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            try
            {
                var extensionPath = GetExtensionPath();
                ((Microsoft.Data.Sqlite.SqliteConnection)connection).LoadExtension(extensionPath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load sqlite-vec extension. Vector search will be disabled.");
                _vectorEnabled = false;
                return false;
            }

            try
            {
                var createTableSql = @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS vec_articles USING vec0(
                        article_id INTEGER PRIMARY KEY, 
                        embedding float[3072]
                    );";
                await connection.ExecuteAsync(createTableSql);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize vec_articles table. Vector search will be disabled.");
                _vectorEnabled = false;
                return false;
            }

            _vectorEnabled = true;
            return true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task UpsertVectorAsync(int articleId, float[] vector)
    {
        if (!await EnsureVectorReadyAsync())
        {
            return;
        }

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        // Convert float[] to byte[] for sqlite-vec
        var vectorBytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, vectorBytes, 0, vectorBytes.Length);

        // Check if vector exists first (upsert logic)
        // For simplicity, we'll just delete and insert, or use INSERT OR REPLACE if table supports it.
        // vec_articles doesn't have a PK on article_id usually unless defined. 
        // Let's assume we just want to insert. If it exists, we might want to delete old one first.

        try
        {
            var deleteSql = "DELETE FROM vec_articles WHERE article_id = @Id";
            await connection.ExecuteAsync(deleteSql, new { Id = articleId });

            var sql = "INSERT INTO vec_articles(article_id, embedding) VALUES (@Id, @Vector)";
            var rows = await connection.ExecuteAsync(sql, new { Id = articleId, Vector = vectorBytes });
            _logger.LogInformation("Inserted vector for article {Id}. Rows affected: {Rows}", articleId, rows);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Vector insert failed for article {Id}.", articleId);
        }
    }

    public async Task DeleteVectorAsync(int articleId)
    {
        if (!await EnsureVectorReadyAsync())
        {
            return;
        }

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        try
        {
            await connection.ExecuteAsync("DELETE FROM vec_articles WHERE article_id = @Id", new { Id = articleId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete vector for article {Id}", articleId);
        }
    }

    public async Task<IEnumerable<Article>> SearchAsync(float[] queryVector, int limit)
    {
        if (!await EnsureVectorReadyAsync())
        {
            return Enumerable.Empty<Article>();
        }

        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
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
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector search failed.");
            return Enumerable.Empty<Article>();
        }
    }
}
