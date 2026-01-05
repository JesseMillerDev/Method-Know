using Know.ApiService.Data;
using Know.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Know.ApiService.Services;

public class VoteService
{
    private readonly AppDbContext _dbContext;

    public VoteService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> VoteAsync(int articleId, string userId, int voteValue)
    {
        var existingVote = await _dbContext.ArticleVotes
            .FirstOrDefaultAsync(v => v.ArticleId == articleId && v.UserId == userId);

        var article = await _dbContext.Articles.FindAsync(articleId);
        if (article == null) return false;

        var shouldNotify = false;

        if (existingVote != null)
        {
            // If clicking same vote button, toggle off (remove vote)
            if (existingVote.VoteValue == voteValue)
            {
                _dbContext.ArticleVotes.Remove(existingVote);

                // Revert stats
                if (voteValue == 1) article.Upvotes--;
                else if (voteValue == -1) article.Downvotes--;
                article.VoteCount--;
            }
            else
            {
                // Changing vote (e.g. Up -> Down)
                existingVote.VoteValue = voteValue;
                existingVote.VotedAt = DateTime.UtcNow; // Update timestamp

                if (voteValue == 1)
                {
                    article.Upvotes++;
                    article.Downvotes--;
                    shouldNotify = true;
                }
                else
                {
                    article.Downvotes++;
                    article.Upvotes--;
                }
                // VoteCount stays same (1 user = 1 vote count)
            }
        }
        else
        {
            // New vote
            _dbContext.ArticleVotes.Add(new ArticleVote
            {
                ArticleId = articleId,
                UserId = userId,
                VoteValue = voteValue
            });

            if (voteValue == 1)
            {
                article.Upvotes++;
                shouldNotify = true;
            }
            else if (voteValue == -1) article.Downvotes++;
            article.VoteCount++;
        }

        // Recalculate Score
        article.Score = article.Upvotes - article.Downvotes;

        if (shouldNotify && article.UserId != userId)
        {
            var actorLabel = "Someone";
            if (int.TryParse(userId, out var actorId))
            {
                actorLabel = await _dbContext.Users
                    .Where(u => u.Id == actorId)
                    .Select(u => u.Email)
                    .FirstOrDefaultAsync() ?? "Someone";
            }

            if (string.IsNullOrWhiteSpace(actorLabel))
            {
                actorLabel = "Someone";
            }

            var message = $"{actorLabel} liked your resource \"{article.Title}\".";
            _dbContext.Notifications.Add(new Notification
            {
                UserId = article.UserId,
                ActorUserId = userId,
                ArticleId = article.Id,
                Type = "like",
                Message = message,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task EnrichArticlesWithUserVotesAsync(IEnumerable<Article> articles, string userId)
    {
        if (articles == null || !articles.Any()) return;

        var articleIds = articles.Select(a => a.Id).ToList();
        var userVotes = await _dbContext.ArticleVotes
            .Where(v => v.UserId == userId && articleIds.Contains(v.ArticleId))
            .ToDictionaryAsync(v => v.ArticleId, v => v.VoteValue);

        foreach (var article in articles)
        {
            if (userVotes.TryGetValue(article.Id, out int voteValue))
            {
                article.IsVoted = true;
                article.UserVoteValue = voteValue;
            }
        }
    }
}
