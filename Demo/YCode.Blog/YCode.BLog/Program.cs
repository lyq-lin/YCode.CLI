using Microsoft.EntityFrameworkCore;
using YCode.BLog.Data;
using YCode.BLog.Models;
using YCode.BLog.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

// Add Entity Framework Core
builder.Services.AddDbContext<BlogDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add services
builder.Services.AddScoped<IBlogPostService, BlogPostService>();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<BlogDbContext>();
    dbContext.Database.EnsureCreated();

    // Seed sample data if database is empty
    if (!dbContext.BlogPosts.Any())
    {
        dbContext.BlogPosts.AddRange(
            new BlogPost
            {
                Title = "欢迎来到我的博客",
                Content = "这是我的第一篇博客文章。我将在这里分享我的技术经验和学习心得。",
                Summary = "欢迎来到我的个人博客，这里将记录我的技术成长之路。",
                Author = "YCode",
                Category = "杂谈",
                Tags = new List<string> { "博客", "欢迎" },
                Slug = "welcome-to-my-blog"
            },
            new BlogPost
            {
                Title = "ASP.NET Core 入门指南",
                Content = "ASP.NET Core 是一个跨平台的高性能框架，用于构建现代化的 Web 应用程序。",
                Summary = "学习如何使用 ASP.NET Core 构建 Web 应用程序的基础知识。",
                Author = "YCode",
                Category = "技术",
                Tags = new List<string> { "ASP.NET Core", "Web开发", "C#" },
                Slug = "aspnet-core-guide"
            }
        );
        dbContext.SaveChanges();
    }
}

app.Run();
