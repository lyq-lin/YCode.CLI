using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;

namespace YCode.CLI
{
    internal class MemoryManager
    {
        private readonly string _rootDir;
        private readonly string _profilePath;
        private readonly string _dailyDir;
        private List<MemoryItem>? _profile;

        public MemoryManager()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _rootDir = Path.Combine(userProfile, ".ycode", "memory");
            _profilePath = Path.Combine(_rootDir, "profile.json");
            _dailyDir = Path.Combine(_rootDir, "daily");

            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_dailyDir);
        }

        public string AddMemory(string category, string content, string? date, List<string>? tags)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "Error: memory content cannot be empty.";
            }

            var normalizedCategory = category?.Trim().ToLowerInvariant() ?? "";
            var normalizedContent = NormalizeContent(content);
            var now = DateTimeOffset.Now.ToString("O");

            if (normalizedCategory == "profile")
            {
                var profile = LoadProfile();
                var existing = profile.FirstOrDefault(x => NormalizeContent(x.Content) == normalizedContent);
                if (existing != null)
                {
                    existing.UpdatedAt = now;
                    MergeTags(existing, tags);
                    SaveProfile(profile);
                    return "Memory updated: profile item already exists.";
                }

                profile.Add(new MemoryItem
                {
                    Content = content.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    Tags = tags ?? []
                });

                TrimList(profile, 50);
                SaveProfile(profile);
                return "Memory saved: profile.";
            }

            if (normalizedCategory == "daily")
            {
                var dateKey = ResolveDateKey(date);
                if (dateKey == null)
                {
                    return "Error: date must be in YYYY-MM-DD format for daily memory.";
                }

                var list = LoadDaily(dateKey);
                var existing = list.FirstOrDefault(x => NormalizeContent(x.Content) == normalizedContent);
                if (existing != null)
                {
                    existing.UpdatedAt = now;
                    MergeTags(existing, tags);
                    SaveDaily(dateKey, list);
                    return $"Memory updated: daily {dateKey} item already exists.";
                }

                list.Add(new MemoryItem
                {
                    Content = content.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    Tags = tags ?? []
                });

                TrimList(list, 50);
                SaveDaily(dateKey, list);
                return $"Memory saved: daily {dateKey}.";
            }

            return "Error: category must be profile or daily.";
        }

        public ChatMessage? BuildContextBlock(int maxProfile = 20)
        {
            var todayKey = DateTime.Now.ToString("yyyy-MM-dd");
            var profile = LoadProfile();
            var dailyList = LoadDaily(todayKey);
            var hasProfile = profile.Count > 0;
            var hasDaily = dailyList.Count > 0;

            if (!hasProfile && !hasDaily)
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<memory>");

            if (hasProfile)
            {
                sb.AppendLine("profile:");

                foreach (var item in profile
                    .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                    .Take(maxProfile))
                {
                    sb.AppendLine($"- {item.Content}");
                }
            }

            if (hasDaily)
            {
                sb.AppendLine($"daily ({todayKey}):");

                foreach (var item in dailyList)
                {
                    sb.AppendLine($"- {item.Content}");
                }
            }

            sb.AppendLine("</memory>");

            return new ChatMessage
            {
                Role = ChatRole.User,
                Contents = [new TextContent(sb.ToString())]
            };
        }

        private List<MemoryItem> LoadProfile()
        {
            if (_profile != null)
                return _profile;

            _profile = LoadList(_profilePath);
            return _profile;
        }

        private void SaveProfile(List<MemoryItem> profile)
        {
            SaveList(_profilePath, profile);
        }

        private List<MemoryItem> LoadDaily(string dateKey)
        {
            var path = Path.Combine(_dailyDir, $"{dateKey}.json");
            return LoadList(path);
        }

        private void SaveDaily(string dateKey, List<MemoryItem> items)
        {
            var path = Path.Combine(_dailyDir, $"{dateKey}.json");
            SaveList(path, items);
        }

        private List<MemoryItem> LoadList(string path)
        {
            if (!File.Exists(path))
                return [];

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<MemoryItem>>(json, JsonOptions()) ?? [];
            }
            catch
            {
                var backup = path + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                File.Copy(path, backup, true);
                return [];
            }
        }

        private void SaveList(string path, List<MemoryItem> items)
        {
            var json = JsonSerializer.Serialize(items, JsonOptions());
            File.WriteAllText(path, json);
        }

        private static JsonSerializerOptions JsonOptions()
        {
            return new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
        }

        private static string NormalizeContent(string content)
        {
            return content.Trim().ToLowerInvariant();
        }

        private static void MergeTags(MemoryItem item, List<string>? tags)
        {
            if (tags == null || tags.Count == 0)
            {
                return;
            }

            var set = new HashSet<string>(item.Tags ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    set.Add(tag.Trim());
                }
            }

            item.Tags = [.. set];
        }

        private static void TrimList(List<MemoryItem> list, int max)
        {
            if (list.Count <= max)
            {
                return;
            }

            var ordered = list
                .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
                .Take(max)
                .ToList();

            list.Clear();
            list.AddRange(ordered);
        }

        private static string? ResolveDateKey(string? date)
        {
            if (string.IsNullOrWhiteSpace(date))
            {
                return DateTime.Now.ToString("yyyy-MM-dd");
            }

            if (DateTime.TryParse(date, out var parsed))
            {
                return parsed.ToString("yyyy-MM-dd");
            }

            return null;
        }
    }

    internal class MemoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("n");
        public string Content { get; set; } = "";
        public string? CreatedAt { get; set; }
        public string? UpdatedAt { get; set; }
        public List<string>? Tags { get; set; } = [];
    }
}
