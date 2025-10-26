using Microsoft.EntityFrameworkCore;
using YCode.BLog.Data;
using YCode.BLog.Models;

namespace YCode.BLog.Services
{
    public interface IBlogPostService
    {
        Task<IEnumerable<BlogPost>> GetPublishedPostsAsync(int page = 1, int pageSize = 10);
        Task<IEnumerable<BlogPost>> SearchPostsAsync(string searchTerm);
        Task<IEnumerable<string>> GetCategoriesAsync();
        Task<IEnumerable<string>> GetTagsAsync();
        Task<IEnumerable<BlogPost>> GetPopularPostsAsync(int count = 5);
        Task<IEnumerable<BlogPost>> GetRelatedPostsAsync(int postId, int count = 3);
    }

    public class BlogPostService : IBlogPostService
    {
        private readonly BlogDbContext _context;

        public BlogPostService(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<BlogPost>> GetPublishedPostsAsync(int page = 1, int pageSize = 10)
        {
            return await _context.BlogPosts
                .Where(p => p.IsPublished)
                .OrderByDescending(p => p.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        public async Task<IEnumerable<BlogPost>> SearchPostsAsync(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return await GetPublishedPostsAsync();

            return await _context.BlogPosts
                .Where(p => p.IsPublished &&
                           (p.Title.Contains(searchTerm) ||
                            p.Content.Contains(searchTerm) ||
                            p.Summary.Contains(searchTerm)))
                .OrderByDescending(p => p.CreatedAt)
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetCategoriesAsync()
        {
            return await _context.BlogPosts
                .Where(p => p.IsPublished && !string.IsNullOrEmpty(p.Category))
                .Select(p => p.Category!)
                .Distinct()
                .ToListAsync();
        }

        public async Task<IEnumerable<string>> GetTagsAsync()
        {
            return await _context.BlogPosts
                .Where(p => p.IsPublished)
                .SelectMany(p => p.Tags)
                .Distinct()
                .ToListAsync();
        }

        public async Task<IEnumerable<BlogPost>> GetPopularPostsAsync(int count = 5)
        {
            return await _context.BlogPosts
                .Where(p => p.IsPublished)
                .OrderByDescending(p => p.ViewCount)
                .ThenByDescending(p => p.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task<IEnumerable<BlogPost>> GetRelatedPostsAsync(int postId, int count = 3)
        {
            var post = await _context.BlogPosts.FindAsync(postId);
            if (post == null) return Enumerable.Empty<BlogPost>();

            return await _context.BlogPosts
                .Where(p => p.IsPublished &&
                           p.Id != postId &&
                           (p.Category == post.Category ||
                            p.Tags.Any(t => post.Tags.Contains(t))))
                .OrderByDescending(p => p.CreatedAt)
                .Take(count)
                .ToListAsync();
        }
    }
}