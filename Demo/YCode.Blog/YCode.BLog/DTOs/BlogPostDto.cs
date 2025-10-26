namespace YCode.BLog.DTOs
{
    public class BlogPostDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public string? Summary { get; set; }
        public string Author { get; set; } = "Anonymous";
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public bool IsPublished { get; set; }
        public string? Category { get; set; }
        public List<string> Tags { get; set; } = new();
        public int ViewCount { get; set; }
        public string? Slug { get; set; }
    }
}