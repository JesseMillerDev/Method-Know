using System.Security.Claims;
using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/profile").RequireAuthorization();

        group.MapGet("/", GetProfile);
        group.MapPut("/", UpdateProfile);
    }

    public static async Task<Results<Ok<User>, NotFound>> GetProfile(ClaimsPrincipal user, AppDbContext db)
    {
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdStr is null || !int.TryParse(userIdStr, out var userId))
        {
            return TypedResults.NotFound();
        }

        var dbUser = await db.Users.FindAsync(userId);
        if (dbUser is null)
        {
            return TypedResults.NotFound();
        }

        return TypedResults.Ok(dbUser);
    }

    public static async Task<Results<Ok<User>, NotFound>> UpdateProfile(User updatedUser, ClaimsPrincipal user, AppDbContext db)
    {
        var userIdStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdStr is null || !int.TryParse(userIdStr, out var userId))
        {
            return TypedResults.NotFound();
        }

        if (userId != updatedUser.Id)
        {
            // Prevent updating other users
            return TypedResults.NotFound(); 
        }

        var dbUser = await db.Users.FindAsync(userId);
        if (dbUser is null)
        {
            return TypedResults.NotFound();
        }

        dbUser.Bio = updatedUser.Bio;
        dbUser.Interests = updatedUser.Interests;
        dbUser.NotificationPreferences = updatedUser.NotificationPreferences;
        dbUser.FirstName = updatedUser.FirstName;
        dbUser.LastName = updatedUser.LastName;

        await db.SaveChangesAsync();

        return TypedResults.Ok(dbUser);
    }
}
