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

    public async Task UpsertVectorAsync(int articleId, float[] vector)
    {
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
        catch { }

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

    public async Task DeleteVectorAsync(int articleId)
    {
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
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector search failed.");
            return Enumerable.Empty<Article>();
        }
    }
}
