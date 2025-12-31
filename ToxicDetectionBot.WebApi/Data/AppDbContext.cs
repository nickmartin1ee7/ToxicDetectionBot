using Microsoft.EntityFrameworkCore;

namespace ToxicDetectionBot.WebApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<UserSentiment> UserSentiments { get; set; }
}
