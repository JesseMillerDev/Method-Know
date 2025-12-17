using Dapper;
using Know.ApiService.Data;
using Know.ApiService.Services;
using Know.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Know.ApiService.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder routes)
    {
        var admin = routes.MapGroup("/api/admin"); // .RequireAuthorization("AdminPolicy"); // TODO: Add admin policy later

        admin.MapPost("/seed-stress-test", SeedStressTest);
        admin.MapPost("/retag-missing", RetagMissing);
        admin.MapPost("/clear-all", ClearAll);
    }

    public static async Task<IResult> SeedStressTest(
        [FromQuery] int count, 
        AppDbContext db, 
        HttpClient http,
        BackgroundQueue queue)
    {
        if (count <= 0) count = 100;
        if (count > 5000) count = 5000; // Limit to avoid timeouts

        var client = http;
        var articles = new List<Article>();
        var random = new Random();

        // Fetch data in batches of 100 (Hugging Face API limit is usually 100)
        int offset = 0;
        int batchSize = 100;
        
        while (articles.Count < count)
        {
            int length = Math.Min(batchSize, count - articles.Count);
            var url = $"https://datasets-server.huggingface.co/rows?dataset=google/boolq&config=default&split=train&offset={offset}&length={length}";
            
            try 
            {
                var response = await client.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    return Results.Problem($"Failed to fetch data from Hugging Face: {response.ReasonPhrase}");
                }

                var content = await response.Content.ReadAsStringAsync();
                var datasetResponse = JsonSerializer.Deserialize<HfDatasetResponse>(content);

                if (datasetResponse?.Rows == null || !datasetResponse.Rows.Any())
                {
                    break; // No more data
                }

                foreach (var row in datasetResponse.Rows)
                {
                    if (row.RowData == null) continue;

                    var article = new Article
                    {
                        Title = row.RowData.Question ?? "Untitled",
                        Content = row.RowData.Passage ?? "No content",
                        UserId = "system_stress_test", // Fixed user for now
                        CreatedAt = DateTime.UtcNow,
                        Category = "StressTest",
                        Tags = "[]"
                    };
                    articles.Add(article);
                }

                offset += length;
            }
            catch (Exception ex)
            {
                return Results.Problem($"Error fetching data: {ex.Message}");
            }
        }

        // Batch insert
        await db.Articles.AddRangeAsync(articles);
        await db.SaveChangesAsync();

        // Enqueue for background processing (Embedding, Tagging, Summary)
        foreach (var article in articles)
        {
            await queue.EnqueueAsync(article.Id);
        }

        return Results.Ok(new { Message = $"Successfully seeded {articles.Count} articles and queued for processing." });
    }

    private static async Task<IResult> RetagMissing(AppDbContext db, BackgroundQueue queue)
    {
        var articles = await db.Articles
            .Where(a => a.Tags == "[]" || a.Tags == null || a.Summary == null)
            .ToListAsync();

        foreach (var article in articles)
        {
            await queue.EnqueueAsync(article.Id);
        }

        return Results.Ok(new { Message = $"Queued {articles.Count} articles for reprocessing." });
    }

    private static async Task<IResult> ClearAll(AppDbContext db, VectorService vectorService, TagCacheService tagCache)
    {
        // 1. Clear ArticleVotes
        await db.ArticleVotes.ExecuteDeleteAsync();

        // 2. Clear Articles
        await db.Articles.ExecuteDeleteAsync();

        // 3. Clear Vector Table
        // We need to do this via VectorService or raw SQL because it's a virtual table
        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();
        await connection.ExecuteAsync("DELETE FROM vec_articles;");

        // 4. Clear Tag Cache
        tagCache.Clear();

        return Results.Ok(new { Message = "Successfully cleared all articles, votes, vectors, and tag cache." });
    }

    private class HfDatasetResponse
    {
        [JsonPropertyName("rows")]
        public List<HfRow>? Rows { get; set; }
    }

    private class HfRow
    {
        [JsonPropertyName("row")]
        public HfRowData? RowData { get; set; }
    }

    private class HfRowData
    {
        [JsonPropertyName("question")]
        public string? Question { get; set; }

        [JsonPropertyName("passage")]
        public string? Passage { get; set; }
    }
}
