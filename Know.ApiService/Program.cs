using Know.ApiService.Data;
using Know.ApiService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=know.db"));

// Register Vector Service
builder.Services.AddScoped<VectorDbService>();

var app = builder.Build();

// Initialize Database
await DatabaseInitializer.InitializeAsync(app.Services);

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

// Helper for mock embeddings
float[] GenerateMockEmbedding()
{
    var random = new Random();
    var embedding = new float[384];
    for (int i = 0; i < 384; i++)
    {
        embedding[i] = (float)(random.NextDouble() * 2 - 1);
    }
    return embedding;
}

// Endpoints
app.MapPost("/api/articles", async (Article article, VectorDbService vectorService) =>
{
    var vector = GenerateMockEmbedding();
    var createdArticle = await vectorService.CreateArticleAsync(article, vector);
    return Results.Created($"/api/articles/{createdArticle.Id}", createdArticle);
})
.WithName("CreateArticle");

app.MapGet("/api/search", async ([FromQuery] string query, VectorDbService vectorService) =>
{
    var queryVector = GenerateMockEmbedding(); // Mocking the embedding of the query string
    var results = await vectorService.SearchAsync(queryVector, 5);
    return Results.Ok(results);
})
.WithName("SearchArticles");

app.MapGet("/api/users/{userId}/articles", async (string userId, VectorDbService vectorService) =>
{
    var articles = await vectorService.GetArticlesByUserIdAsync(userId);
    return Results.Ok(articles);
})
.WithName("GetUserArticles");

app.Run();
