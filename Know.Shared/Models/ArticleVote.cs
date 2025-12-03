using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Know.Shared.Models;

public class ArticleVote
{
    public int Id { get; set; }

    [Required]
    public int ArticleId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public int VoteValue { get; set; } // 1 for Upvote, -1 for Downvote

    public DateTime VotedAt { get; set; } = DateTime.UtcNow;
}
