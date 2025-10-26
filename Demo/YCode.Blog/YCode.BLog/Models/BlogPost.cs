using System.ComponentModel.DataAnnotations;

namespace YCode.BLog.Models
{
    public class BlogPost
    {
        public int Id { get; set; }
        
        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;
        
        [Required]
        public string Content { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string? Summary { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Author { get; set; } = "Anonymous";
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedAt { get; set; }
        
        public bool IsPublished { get; set; } = true;
        
        [StringLength(100)]
        public string? Category { get; set; }
        
        public List<string> Tags { get; set; } = new();
        
        public int ViewCount { get; set; } = 0;
        
        [StringLength(200)]
        public string? Slug { get; set; }
    }
}