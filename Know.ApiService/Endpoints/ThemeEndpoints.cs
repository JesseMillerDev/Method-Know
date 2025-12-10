using Know.ApiService.Data;
using Know.ApiService.Services;
using Know.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Endpoints;

public static class ThemeEndpoints
{
    public static void MapThemeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/theme").RequireAuthorization();

        group.MapPost("/generate", async (GenerateThemeRequest request, AiThemeService themeService) =>
        {
            try
            {
                var css = await themeService.GenerateThemeAsync(request.Description);
                return Results.Ok(new { Css = css });
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                return Results.Problem("Rate limit exceeded. Please try again later.", statusCode: 429);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: 500);
            }
        });

        group.MapPost("/save", async (SaveThemeRequest request, AppDbContext db, HttpContext httpContext) =>
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId)) return Results.Unauthorized();

            // Assuming userId is an int in the DB based on User model, but claims are strings.
            // Let's check how other endpoints handle it. 
            // Other endpoints use `userId` as string for VectorDbService, but for AppDbContext we might need int?
            // User.cs has `public int Id { get; set; }`.
            // Let's try to parse it.
            
            if (!int.TryParse(userId, out int id)) return Results.Unauthorized();

            var user = await db.Users.FindAsync(id);
            if (user == null) return Results.NotFound();

            user.CustomCss = request.Css;
            await db.SaveChangesAsync();

            return Results.Ok();
        });

        group.MapGet("/", async (AppDbContext db, HttpContext httpContext) =>
        {
            var userId = httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out int id)) return Results.Unauthorized();

            var user = await db.Users.FindAsync(id);
            if (user == null) return Results.NotFound();

            return Results.Ok(new { Css = user.CustomCss });
        });
    }
}

public record GenerateThemeRequest(string Description);
public record SaveThemeRequest(string Css);
