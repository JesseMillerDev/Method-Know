using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Know.Shared.Models;

public class Article
{
    public int Id { get; set; }
    
    [Required]
    public string Title { get; set; } = string.Empty;
    
    [Required]
    public string Content { get; set; } = string.Empty;
    
    public string Category { get; set; } = string.Empty;

    [Required]
    public string UserId { get; set; } = string.Empty;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int VoteCount { get; set; }
    
    public int Score { get; set; }
    public int Upvotes { get; set; }
    public int Downvotes { get; set; }

    [NotMapped]
    public bool IsVoted { get; set; }

    [NotMapped]
    public int UserVoteValue { get; set; } // 0 = None, 1 = Up, -1 = Down
}
