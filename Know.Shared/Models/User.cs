namespace Know.Shared.Models;

public class User
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string PasswordHash { get; set; }
    public string? Bio { get; set; }
    public string? Interests { get; set; }
    public string? NotificationPreferences { get; set; }
    public string? CustomCss { get; set; }
}
