using Microsoft.EntityFrameworkCore;
using YCode.BLog.Models;

namespace YCode.BLog.Data
{
    public class BlogDbContext : DbContext
    {
        public BlogDbContext(DbContextOptions<BlogDbContext> options) : base(options)
        {
        }

        public DbSet<BlogPost> BlogPosts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BlogPost>(entity =>
            {
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Tags)
                      .HasConversion(
                          v => string.Join(',', v),
                          v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
                      );
                entity.HasIndex(e => e.Slug).IsUnique();
                entity.HasIndex(e => e.Category);
                entity.HasIndex(e => e.CreatedAt);
            });
        }
    }
}