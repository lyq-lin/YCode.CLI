using System.ComponentModel.DataAnnotations;

namespace YCode.BLog.DTOs
{
    public class CreateBlogPostDto
    {
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Content { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Summary { get; set; }
        
        [StringLength(100)]
        public string Author { get; set; } = "Anonymous";
        
        [StringLength(100)]
        public string? Category { get; set; }
        
        public List<string> Tags { get; set; } = new();
        
        [StringLength(200)]
        public string? Slug { get; set; }
    }
}