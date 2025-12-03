using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Know.Shared.Models;

namespace Know.ApiService.Data;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        // Step A: Ensure Database Created (Dev Mode)
        await dbContext.Database.EnsureCreatedAsync();

        // Manual Schema Update for Upvotes (Poor man's migration)
        var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync();

        try 
        {
            // 1. Create ArticleVotes table
            await connection.ExecuteAsync(@"
                CREATE TABLE IF NOT EXISTS ""ArticleVotes"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_ArticleVotes"" PRIMARY KEY AUTOINCREMENT,
                    ""ArticleId"" INTEGER NOT NULL,
                    ""UserId"" TEXT NOT NULL,
                    ""VotedAt"" TEXT NOT NULL
                );");

            // 2. Add VoteCount to Articles if missing
            try { await connection.ExecuteAsync(@"ALTER TABLE ""Articles"" ADD COLUMN ""VoteCount"" INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) {}

            // 3. Add Score, Upvotes, Downvotes to Articles
            try { await connection.ExecuteAsync(@"ALTER TABLE ""Articles"" ADD COLUMN ""Score"" INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) {}
            try { await connection.ExecuteAsync(@"ALTER TABLE ""Articles"" ADD COLUMN ""Upvotes"" INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) {}
            try { await connection.ExecuteAsync(@"ALTER TABLE ""Articles"" ADD COLUMN ""Downvotes"" INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) {}

            // 4. Add VoteValue to ArticleVotes
            try { await connection.ExecuteAsync(@"ALTER TABLE ""ArticleVotes"" ADD COLUMN ""VoteValue"" INTEGER NOT NULL DEFAULT 0;"); } catch (SqliteException) {}
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to update schema for upvotes.");
        }
        
        try
        {
            await connection.OpenAsync();
            
            // Try to load the extension. 
            // Note: "vec0" is the name of the entry point/extension. 
            // The file name might be different depending on OS (e.g. vec0.dll, libvec0.so, vec0.dylib)
            // but LoadExtension usually takes the name.
            // If this fails, it means the extension binary is not found in the path.
            // Try to load the extension using absolute path
            var extensionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Libs", "vec0.dylib");
            connection.LoadExtension(extensionPath);
            logger.LogInformation("Successfully loaded 'vec0' extension.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load 'vec0' extension. Vector search capabilities will be disabled or fail.");
            // We continue even if it fails, so the app doesn't crash, but vector ops will fail later.
        }

        // Step C: Create Virtual Table
        // We only do this if the extension loaded successfully or we want to try anyway.
        // If extension didn't load, this SQL will likely fail if it relies on vec0 module.
        try
        {
            // Check if table exists first or just use IF NOT EXISTS
            var createTableSql = @"
                CREATE VIRTUAL TABLE IF NOT EXISTS vec_articles USING vec0(
                    article_id INTEGER PRIMARY KEY, 
                    embedding float[384]
                );";

            await connection.ExecuteAsync(createTableSql);
            logger.LogInformation("Verified 'vec_articles' virtual table.");
        }
        catch (Exception ex)
        {
             logger.LogError(ex, "Failed to create 'vec_articles' virtual table.");
        }
    }
}
