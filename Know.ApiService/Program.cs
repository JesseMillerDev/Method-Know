using Know.ApiService.Data;
using Know.Shared.Models;
using Know.ApiService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Know.ApiService.Endpoints;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register DbContext
var dbPath = "know.db";
if (Directory.Exists("/data"))
{
    dbPath = "/data/know.db";
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register Vector Service
builder.Services.AddScoped<VectorDbService>();

// Register Auth Service
builder.Services.AddScoped<AuthService>();

// Register Embedding Service
builder.Services.AddSingleton<OnnxEmbeddingService>();

// Register Background Services
builder.Services.AddSingleton<BackgroundQueue>();
builder.Services.AddHostedService<EmbeddingBackgroundService>();

// Register Tagging Service
builder.Services.AddSingleton<TaggingService>();

// Configure JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "super_secret_key_that_should_be_in_env_vars_and_long_enough";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "MethodKnow",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "MethodKnowUsers",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();
            policy.WithOrigins(allowedOrigins)
                   .AllowAnyMethod()
                   .AllowAnyHeader()
                   .AllowCredentials();
        });
});

var app = builder.Build();

app.UseCors("AllowAll");

// Initialize Database
await DatabaseInitializer.InitializeAsync(app.Services);

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// Auth Endpoints
app.MapPost("/api/auth/signup", async (AuthService authService, [FromBody] LoginRequest request) =>
{
    var user = await authService.RegisterAsync(request.Email, request.Password);
    if (user == null)
    {
        return Results.Conflict("User already exists");
    }
    return Results.Ok(new { user.Id, user.Email });
})
.WithName("Signup");

app.MapPost("/api/auth/login", async (AuthService authService, [FromBody] LoginRequest request) =>
{
    var token = await authService.LoginAsync(request.Email, request.Password);
    if (token == null)
    {
        return Results.Unauthorized();
    }
    return Results.Ok(new { Token = token });
})
.WithName("Login");

// Endpoints
app.MapPost("/api/articles", async (Article article, VectorDbService vectorService, BackgroundQueue queue) =>
{
    // Create article without vector first (fast)
    // Tags and Summary will be generated in background
    var createdArticle = await vectorService.CreateArticleAsync(article, null);
    
    // Queue for background processing (Tagging -> Summary -> Embedding)
    await queue.EnqueueAsync(createdArticle.Id);
    return Results.Created($"/api/articles/{createdArticle.Id}", createdArticle);
})
.WithName("CreateArticle")
.RequireAuthorization();

app.MapPut("/api/articles/{id}", async (int id, Article article, VectorDbService vectorService, BackgroundQueue queue, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    // Tags will be updated in background if needed, or we can choose to NOT update tags on edit to preserve manual edits.
    // For now, let's assume we want to re-generate tags if content changed, but we can't easily detect that here without fetching first.
    // The user might have manually edited tags, so we shouldn't overwrite them blindly.
    // However, the background service checks "if missing or empty". 
    // If we want to force re-tagging on edit, we'd need to clear them.
    // Let's leave tags as-is for now on edit, or maybe clear them if the user wants?
    // Actually, the previous code ALWAYS re-generated tags on edit.
    // To match that behavior, we should clear the tags so the background service sees them as empty?
    // Or better: The background service logic I wrote only checks `if (article.TagList == null || !article.TagList.Any())`.
    // So if we want re-tagging, we must clear them here.
    
    // BUT: If the user *manually* edited tags in the UI (which they can't do yet, but might), we'd lose them.
    // Given the previous code forced re-tagging, I will maintain that behavior by clearing the tags in the object passed to UpdateArticleAsync.
    // Wait, UpdateArticleAsync updates the DB with `updatedArticle`.
    // So if I set `article.TagList = null` here, it saves null to DB, then background service sees null and re-generates.
    // Perfect.
    
    article.TagList = new List<string>(); 
    article.Summary = null; // Re-generate summary too

    var (status, updatedArticle) = await vectorService.UpdateArticleAsync(id, article, null, userId);
    
    // Queue for background processing
    if (status == OperationResult.Success)
    {
        await queue.EnqueueAsync(id);
    }

    return status switch
    {
        OperationResult.NotFound => Results.NotFound(),
        OperationResult.Forbidden => Results.Forbid(),
        _ => Results.Ok(updatedArticle)
    };
})
.WithName("UpdateArticle")
.RequireAuthorization();

app.MapGet("/api/search", async ([FromQuery] string query, VectorDbService vectorService, OnnxEmbeddingService embeddingService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var queryVector = await embeddingService.GenerateEmbeddingAsync(query);
    var results = await vectorService.SearchAsync(queryVector, 5, userId);
    return Results.Ok(results);
})
.WithName("SearchArticles");

app.MapGet("/api/articles", async (VectorDbService vectorService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var articles = await vectorService.GetAllArticlesAsync(userId);
    return Results.Ok(articles);
})
.WithName("GetAllArticles")
.RequireAuthorization();

app.MapGet("/api/articles/{id}", async (int id, VectorDbService vectorService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var article = await vectorService.GetArticleByIdAsync(id, userId);
    
    if (article == null) return Results.NotFound();
    
    return Results.Ok(article);
})
.WithName("GetArticleById");

app.MapDelete("/api/articles/{id}", async (int id, VectorDbService vectorService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var status = await vectorService.DeleteArticleAsync(id, userId);

    return status switch
    {
        OperationResult.NotFound => Results.NotFound(),
        OperationResult.Forbidden => Results.Forbid(),
        _ => Results.NoContent()
    };
})
.WithName("DeleteArticle")
.RequireAuthorization();

app.MapPost("/api/articles/{id}/vote", async (int id, int? voteValue, VectorDbService vectorService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
    
    // Default to Upvote (1) if not specified, for backward compatibility or simple calls
    int val = voteValue ?? 1;
    
    var success = await vectorService.VoteAsync(id, userId, val);
    if (!success) return Results.NotFound();
    
    return Results.Ok();
})
.WithName("VoteArticle")
.RequireAuthorization();

app.MapGet("/api/users/{userId}/articles", async (string userId, VectorDbService vectorService) =>
{
    var articles = await vectorService.GetArticlesByUserIdAsync(userId);
    return Results.Ok(articles);
})
.WithName("GetUserArticles")
.RequireAuthorization();

app.MapGet("/api/tags", async (VectorDbService vectorService) =>
{
    var tags = await vectorService.GetAllTagsAsync();
    return Results.Ok(tags);
})
.WithName("GetAllTags")
.RequireAuthorization();

app.MapProfileEndpoints();

app.Run();

public record LoginRequest(string Email, string Password);
