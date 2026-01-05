namespace Know.Shared.Models;

public class Notification
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string ActorUserId { get; set; } = string.Empty;
    public int ArticleId { get; set; }
    public string Type { get; set; } = "like";
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
