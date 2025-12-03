using Know.ApiService.Data;
using Know.ApiService.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Register DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=know.db"));

// Register Vector Service
builder.Services.AddScoped<VectorDbService>();

// Register Auth Service
builder.Services.AddScoped<AuthService>();

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
        builder =>
        {
            builder.WithOrigins("https://localhost:7208", "http://localhost:5075")
                   .AllowAnyMethod()
                   .AllowAnyHeader();
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
app.MapPost("/api/articles", async (Article article, VectorDbService vectorService) =>
{
    var vector = GenerateMockEmbedding();
    var createdArticle = await vectorService.CreateArticleAsync(article, vector);
    return Results.Created($"/api/articles/{createdArticle.Id}", createdArticle);
})
.WithName("CreateArticle")
.RequireAuthorization();

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
.WithName("GetUserArticles")
.RequireAuthorization();

app.Run();

public record LoginRequest(string Email, string Password);
