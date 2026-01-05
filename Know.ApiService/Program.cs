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
builder.Services.AddHttpClient();

// Register DbContext
var dbPath = "know.db";
if (Directory.Exists("/data"))
{
    dbPath = "/data/know.db";
}
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register Vector Service
builder.Services.AddScoped<VectorService>();

// Register Vote Service
builder.Services.AddScoped<VoteService>();

// Register Article Service
builder.Services.AddScoped<ArticleService>();

// Register Auth Service
builder.Services.AddScoped<AuthService>();

// Register Background Services
builder.Services.AddSingleton<BackgroundQueue>();
builder.Services.AddHostedService<EmbeddingBackgroundService>();

// Register Gemini Service
builder.Services.AddScoped<GeminiService>();

// Register Tagging Service (Gemini-backed)
builder.Services.AddScoped<TaggingService>();

// Register Tag Cache Service
builder.Services.AddSingleton<TagCacheService>();

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

if (!app.Environment.IsEnvironment("Testing"))
{
    // Initialize Database
    await DatabaseInitializer.InitializeAsync(app.Services);

    // Initialize Tag Cache
    await app.Services.GetRequiredService<TagCacheService>().InitializeAsync();
}

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI();

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}

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
app.MapPost("/api/articles", async (Article article, ArticleService articleService, BackgroundQueue queue) =>
{
    // Create article without vector first (fast)
    // Tags and Summary will be generated in background
    var createdArticle = await articleService.CreateArticleAsync(article, null);
    
    // Queue for background processing (Tagging -> Summary -> Embedding)
    await queue.EnqueueAsync(createdArticle.Id);
    return Results.Created($"/api/articles/{createdArticle.Id}", createdArticle);
})
.WithName("CreateArticle")
.RequireAuthorization();

app.MapPut("/api/articles/{id}", async (int id, Article article, ArticleService articleService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var (status, updatedArticle) = await articleService.UpdateArticleAsync(id, article, null, userId);

    return status switch
    {
        OperationResult.NotFound => Results.NotFound(),
        OperationResult.Forbidden => Results.Forbid(),
        _ => Results.Ok(updatedArticle)
    };
})
.WithName("UpdateArticle")
.RequireAuthorization();

app.MapGet("/api/search", async ([FromQuery] string query, [FromQuery] int? limit, ArticleService articleService, GeminiService geminiService, HttpContext httpContext) =>
{
    if (string.IsNullOrWhiteSpace(query))
    {
        return Results.BadRequest("Query is required.");
    }

    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var queryVector = await geminiService.GenerateEmbeddingAsync(query);
    var cappedLimit = Math.Clamp(limit ?? 20, 1, 50);
    var results = await articleService.SearchAsync(queryVector, cappedLimit, userId);
    return Results.Ok(results);
})
.WithName("SearchArticles");

app.MapGet("/api/articles", async ([FromQuery] string[]? categories, [FromQuery] string[]? tags, [FromQuery] string? search, [FromQuery] int? page, [FromQuery] int? pageSize, ArticleService articleService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var p = page ?? 1;
    var ps = pageSize ?? 20;
    var articles = await articleService.GetAllArticlesAsync(userId, categories, tags, search, p, ps);
    return Results.Ok(articles);
})
.WithName("GetAllArticles")
.RequireAuthorization();

app.MapGet("/api/articles/{id}", async (int id, ArticleService articleService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    var article = await articleService.GetArticleByIdAsync(id, userId);
    
    if (article == null) return Results.NotFound();
    
    return Results.Ok(article);
})
.WithName("GetArticleById");

app.MapDelete("/api/articles/{id}", async (int id, ArticleService articleService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

    var status = await articleService.DeleteArticleAsync(id, userId);

    return status switch
    {
        OperationResult.NotFound => Results.NotFound(),
        OperationResult.Forbidden => Results.Forbid(),
        _ => Results.NoContent()
    };
})
.WithName("DeleteArticle")
.RequireAuthorization();

app.MapPost("/api/articles/{id}/vote", async (int id, int? voteValue, VoteService voteService, HttpContext httpContext) =>
{
    var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();
    
    // Default to Upvote (1) if not specified, for backward compatibility or simple calls
    int val = voteValue ?? 1;
    
    var success = await voteService.VoteAsync(id, userId, val);
    if (!success) return Results.NotFound();
    
    return Results.Ok();
})
.WithName("VoteArticle")
.RequireAuthorization();

app.MapGet("/api/users/{userId}/articles", async (string userId, [FromQuery] string? category, [FromQuery] string? search, ArticleService articleService) =>
{
    var articles = await articleService.GetArticlesByUserIdAsync(userId, category, search);
    return Results.Ok(articles);
})
.WithName("GetUserArticles")
.RequireAuthorization();

app.MapGet("/api/tags", (TagCacheService tagCache) =>
{
    var tags = tagCache.GetPopularTags();
    return Results.Ok(tags);
})
.WithName("GetAllTags")
.RequireAuthorization();

app.MapProfileEndpoints();
app.MapAdminEndpoints();

app.Run();

public record LoginRequest(string Email, string Password);

public partial class Program { }
