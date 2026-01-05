using Microsoft.EntityFrameworkCore;
using Know.Shared.Models;

namespace Know.ApiService.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Article> Articles { get; set; }
    public DbSet<ArticleVote> ArticleVotes { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Notification> Notifications { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        modelBuilder.Entity<Article>()
            .Property(a => a.Title)
            .IsRequired();
            
        modelBuilder.Entity<Article>()
            .Property(a => a.Content)
            .IsRequired();
    }
}
