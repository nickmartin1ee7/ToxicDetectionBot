using Microsoft.EntityFrameworkCore;

namespace ToxicDetectionBot.WebApi.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserSentiment> UserSentiments { get; set; }
    public DbSet<UserSentimentScore> UserSentimentScores { get; set; }
    public DbSet<UserAlignmentScore> UserAlignmentScores { get; set; }
    public DbSet<UserOptOut> UserOptOuts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserSentiment>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GuildId);
            entity.HasIndex(e => e.IsSummarized);
            entity.HasIndex(e => e.GuildName);
            entity.HasIndex(e => e.ChannelName);
        });

        modelBuilder.Entity<UserSentimentScore>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GuildId);
        });

        modelBuilder.Entity<UserAlignmentScore>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.GuildId);
        });

        modelBuilder.Entity<UserOptOut>(entity =>
        {
            entity.HasKey(e => e.UserId);
            entity.HasIndex(e => e.IsOptedOut);
        });
    }
}
