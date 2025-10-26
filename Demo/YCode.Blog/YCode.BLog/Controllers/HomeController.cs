using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YCode.BLog.Data;

namespace YCode.BLog.Controllers
{
    public class HomeController : Controller
    {
        private readonly BlogDbContext _context;

        public HomeController(BlogDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(
            [FromQuery] int page = 1,
            [FromQuery] string? category = null,
            [FromQuery] string? tag = null,
            [FromQuery] string? search = null)
        {
            var query = _context.BlogPosts.AsQueryable();

            // Filter by published status
            query = query.Where(p => p.IsPublished);

            // Filter by category
            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(p => p.Category == category);
            }

            // Filter by tag
            if (!string.IsNullOrEmpty(tag))
            {
                query = query.Where(p => p.Tags.Contains(tag));
            }

            // Search in title and content
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Title.Contains(search) || p.Content.Contains(search));
            }

            // Order by creation date (newest first)
            query = query.OrderByDescending(p => p.CreatedAt);

            var pageSize = 10;
            var totalCount = await query.CountAsync();
            var posts = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.Category = category;
            ViewBag.Tag = tag;
            ViewBag.Search = search;

            ViewBag.Categories = await _context.BlogPosts
                .Where(p => p.IsPublished && !string.IsNullOrEmpty(p.Category))
                .Select(p => p.Category!)
                .Distinct()
                .ToListAsync();

            // Get all tags from published posts
            var allTags = await _context.BlogPosts
                .Where(p => p.IsPublished)
                .Select(p => p.Tags)
                .ToListAsync();
            
            ViewBag.Tags = allTags
                .SelectMany(tags => tags)
                .Where(tag => !string.IsNullOrEmpty(tag))
                .Distinct()
                .ToList();

            return View(posts);
        }

        public async Task<IActionResult> Post(int id)
        {
            var post = await _context.BlogPosts.FindAsync(id);
            if (post == null || !post.IsPublished)
            {
                return NotFound();
            }

            // Increment view count
            post.ViewCount++;
            await _context.SaveChangesAsync();

            // Get related posts by category first
            var relatedPosts = await _context.BlogPosts
                .Where(p => p.IsPublished && p.Id != id && p.Category == post.Category)
                .OrderByDescending(p => p.CreatedAt)
                .Take(3)
                .ToListAsync();

            // If not enough related posts by category, add some by shared tags
            if (relatedPosts.Count < 3)
            {
                var additionalPosts = await _context.BlogPosts
                    .Where(p => p.IsPublished && p.Id != id && p.Category != post.Category)
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(3 - relatedPosts.Count)
                    .ToListAsync();
                
                relatedPosts.AddRange(additionalPosts);
            }

            ViewBag.RelatedPosts = relatedPosts;

            return View(post);
        }

        public async Task<IActionResult> PostBySlug(string slug)
        {
            var post = await _context.BlogPosts
                .FirstOrDefaultAsync(p => p.Slug == slug);
            if (post == null || !post.IsPublished)
            {
                return NotFound();
            }

            // Increment view count
            post.ViewCount++;
            await _context.SaveChangesAsync();

            // Get related posts by category first
            var relatedPosts = await _context.BlogPosts
                .Where(p => p.IsPublished && p.Id != post.Id && p.Category == post.Category)
                .OrderByDescending(p => p.CreatedAt)
                .Take(3)
                .ToListAsync();

            // If not enough related posts by category, add some by shared tags
            if (relatedPosts.Count < 3)
            {
                var additionalPosts = await _context.BlogPosts
                    .Where(p => p.IsPublished && p.Id != post.Id && p.Category != post.Category)
                    .OrderByDescending(p => p.CreatedAt)
                    .Take(3 - relatedPosts.Count)
                    .ToListAsync();
                
                relatedPosts.AddRange(additionalPosts);
            }

            ViewBag.RelatedPosts = relatedPosts;

            return View("Post", post);
        }

        public IActionResult About()
        {
            return View();
        }

        public IActionResult Contact()
        {
            return View();
        }
    }
}