using System.Security.Claims;
using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/notifications").RequireAuthorization();

        group.MapGet("/", GetNotifications);
        group.MapPost("/mark-read", MarkAllRead);
    }

    public static async Task<Results<Ok<List<Notification>>, UnauthorizedHttpResult>> GetNotifications(
        ClaimsPrincipal user,
        AppDbContext db,
        [FromQuery] int? limit)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        var cappedLimit = Math.Clamp(limit ?? 20, 1, 50);
        var notifications = await db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(cappedLimit)
            .ToListAsync();

        return TypedResults.Ok(notifications);
    }

    public static async Task<Results<Ok, UnauthorizedHttpResult>> MarkAllRead(
        ClaimsPrincipal user,
        AppDbContext db)
    {
        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return TypedResults.Unauthorized();
        }

        var unreadNotifications = await db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync();

        if (unreadNotifications.Count > 0)
        {
            foreach (var notification in unreadNotifications)
            {
                notification.IsRead = true;
            }

            await db.SaveChangesAsync();
        }

        return TypedResults.Ok();
    }
}
