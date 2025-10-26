using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using YCode.BLog.Data;
using YCode.BLog.Models;
using YCode.BLog.DTOs;

namespace YCode.BLog.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BlogPostsController : ControllerBase
    {
        private readonly BlogDbContext _context;

        public BlogPostsController(BlogDbContext context)
        {
            _context = context;
        }

        // GET: api/blogposts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<BlogPostDto>>> GetBlogPosts(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 10,
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

            var totalCount = await query.CountAsync();
            var posts = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new BlogPostDto
                {
                    Id = p.Id,
                    Title = p.Title,
                    Content = p.Content,
                    Summary = p.Summary,
                    Author = p.Author,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    IsPublished = p.IsPublished,
                    Category = p.Category,
                    Tags = p.Tags,
                    ViewCount = p.ViewCount,
                    Slug = p.Slug
                })
                .ToListAsync();

            var result = new
            {
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                Data = posts
            };

            return Ok(result);
        }

        // GET: api/blogposts/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<BlogPostDto>> GetBlogPost(int id)
        {
            var blogPost = await _context.BlogPosts.FindAsync(id);

            if (blogPost == null)
            {
                return NotFound();
            }

            // Increment view count
            blogPost.ViewCount++;
            await _context.SaveChangesAsync();

            var blogPostDto = new BlogPostDto
            {
                Id = blogPost.Id,
                Title = blogPost.Title,
                Content = blogPost.Content,
                Summary = blogPost.Summary,
                Author = blogPost.Author,
                CreatedAt = blogPost.CreatedAt,
                UpdatedAt = blogPost.UpdatedAt,
                IsPublished = blogPost.IsPublished,
                Category = blogPost.Category,
                Tags = blogPost.Tags,
                ViewCount = blogPost.ViewCount,
                Slug = blogPost.Slug
            };

            return blogPostDto;
        }

        // GET: api/blogposts/slug/{slug}
        [HttpGet("slug/{slug}")]
        public async Task<ActionResult<BlogPostDto>> GetBlogPostBySlug(string slug)
        {
            var blogPost = await _context.BlogPosts
                .FirstOrDefaultAsync(p => p.Slug == slug);

            if (blogPost == null)
            {
                return NotFound();
            }

            // Increment view count
            blogPost.ViewCount++;
            await _context.SaveChangesAsync();

            var blogPostDto = new BlogPostDto
            {
                Id = blogPost.Id,
                Title = blogPost.Title,
                Content = blogPost.Content,
                Summary = blogPost.Summary,
                Author = blogPost.Author,
                CreatedAt = blogPost.CreatedAt,
                UpdatedAt = blogPost.UpdatedAt,
                IsPublished = blogPost.IsPublished,
                Category = blogPost.Category,
                Tags = blogPost.Tags,
                ViewCount = blogPost.ViewCount,
                Slug = blogPost.Slug
            };

            return blogPostDto;
        }

        // POST: api/blogposts
        [HttpPost]
        public async Task<ActionResult<BlogPostDto>> CreateBlogPost(CreateBlogPostDto createBlogPostDto)
        {
            var blogPost = new BlogPost
            {
                Title = createBlogPostDto.Title,
                Content = createBlogPostDto.Content,
                Summary = createBlogPostDto.Summary,
                Author = createBlogPostDto.Author,
                Category = createBlogPostDto.Category,
                Tags = createBlogPostDto.Tags,
                Slug = createBlogPostDto.Slug ?? GenerateSlug(createBlogPostDto.Title),
                CreatedAt = DateTime.UtcNow
            };

            _context.BlogPosts.Add(blogPost);
            await _context.SaveChangesAsync();

            var blogPostDto = new BlogPostDto
            {
                Id = blogPost.Id,
                Title = blogPost.Title,
                Content = blogPost.Content,
                Summary = blogPost.Summary,
                Author = blogPost.Author,
                CreatedAt = blogPost.CreatedAt,
                UpdatedAt = blogPost.UpdatedAt,
                IsPublished = blogPost.IsPublished,
                Category = blogPost.Category,
                Tags = blogPost.Tags,
                ViewCount = blogPost.ViewCount,
                Slug = blogPost.Slug
            };

            return CreatedAtAction(nameof(GetBlogPost), new { id = blogPost.Id }, blogPostDto);
        }

        // PUT: api/blogposts/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateBlogPost(int id, UpdateBlogPostDto updateBlogPostDto)
        {
            var blogPost = await _context.BlogPosts.FindAsync(id);
            if (blogPost == null)
            {
                return NotFound();
            }

            blogPost.Title = updateBlogPostDto.Title;
            blogPost.Content = updateBlogPostDto.Content;
            blogPost.Summary = updateBlogPostDto.Summary;
            blogPost.Category = updateBlogPostDto.Category;
            blogPost.Tags = updateBlogPostDto.Tags;
            blogPost.Slug = updateBlogPostDto.Slug ?? GenerateSlug(updateBlogPostDto.Title);
            blogPost.IsPublished = updateBlogPostDto.IsPublished;
            blogPost.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/blogposts/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBlogPost(int id)
        {
            var blogPost = await _context.BlogPosts.FindAsync(id);
            if (blogPost == null)
            {
                return NotFound();
            }

            _context.BlogPosts.Remove(blogPost);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // GET: api/blogposts/categories
        [HttpGet("categories")]
        public async Task<ActionResult<IEnumerable<string>>> GetCategories()
        {
            var categories = await _context.BlogPosts
                .Where(p => p.IsPublished && !string.IsNullOrEmpty(p.Category))
                .Select(p => p.Category!)
                .Distinct()
                .ToListAsync();

            return Ok(categories);
        }

        // GET: api/blogposts/tags
        [HttpGet("tags")]
        public async Task<ActionResult<IEnumerable<string>>> GetTags()
        {
            var allTags = await _context.BlogPosts
                .Where(p => p.IsPublished)
                .SelectMany(p => p.Tags)
                .Distinct()
                .ToListAsync();

            return Ok(allTags);
        }

        private static string GenerateSlug(string title)
        {
            return title.ToLower()
                .Replace(" ", "-")
                .Replace(".", "")
                .Replace(",", "")
                .Replace("!", "")
                .Replace("?", "");
        }
    }
}