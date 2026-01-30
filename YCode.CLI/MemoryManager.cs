using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace YCode.CLI
{
    internal class MemoryManager
    {
        private const int DailyRetentionDays = 30;
        private readonly string _rootDir;
        private readonly string _profilePath;
        private readonly string _dailyDir;
        private readonly string _notesDir;
        private List<MemoryItem>? _profile;
        private bool _dailyCleanupDone;

        public MemoryManager()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _rootDir = Path.Combine(userProfile, ".ycode", "memory");
            _profilePath = Path.Combine(_rootDir, "profile.json");
            _dailyDir = Path.Combine(_rootDir, "daily");
            _notesDir = Path.Combine(_rootDir, "notes");

            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_dailyDir);
            Directory.CreateDirectory(_notesDir);
        }

        public string AddMemory(string category, string content, string? date, List<string>? tags)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "Error: memory content cannot be empty.";
            }

            EnsureDailyCleanup();

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

        public ChatMessage? BuildContextBlock(string? userInput = null, int maxProfile = 20)
        {
            EnsureDailyCleanup();

            var today = DateTime.Today;
            var todayKey = today.ToString("yyyy-MM-dd");
            var profile = LoadProfile();
            var dailyList = LoadDaily(todayKey);
            var hasProfile = profile.Count > 0;
            var hasDaily = dailyList.Count > 0;
            var tokens = ExtractTokens(userInput);
            var hasTokens = tokens.Count > 0;
            var relatedDaily = hasTokens ? LoadRelevantDaily(today, tokens, DailyRetentionDays, 5) : [];
            var relatedNotes = hasTokens ? LoadRelevantNotes(tokens, 3) : [];

            if (!hasProfile && !hasDaily && relatedNotes.Count == 0 && relatedDaily.Count == 0)
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

            if (relatedDaily.Count > 0)
            {
                sb.AppendLine("daily-related:");

                foreach (var item in relatedDaily)
                {
                    sb.AppendLine($"- [{item.DateKey}] {item.Content}");
                }
            }

            if (relatedNotes.Count > 0)
            {
                sb.AppendLine("notes:");
                
                foreach (var note in relatedNotes)
                {
                    sb.AppendLine($"- {note.Title}: {note.Preview}");
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

        private void EnsureDailyCleanup()
        {
            if (_dailyCleanupDone)
                return;

            var cutoff = DateTime.Today.AddDays(-DailyRetentionDays);

            foreach (var file in Directory.GetFiles(_dailyDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);

                if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (date < cutoff)
                {
                    File.Delete(file);
                }
            }

            _dailyCleanupDone = true;
        }

        private void SaveDaily(string dateKey, List<MemoryItem> items)
        {
            var path = Path.Combine(_dailyDir, $"{dateKey}.json");
            SaveList(path, items);
        }

        private List<RelatedDailyItem> LoadRelevantDaily(DateTime today, HashSet<string> tokens, int lookbackDays, int maxItems)
        {
            var minDate = today.AddDays(-lookbackDays);

            var items = new List<RelatedDailyItem>();

            foreach (var file in Directory.GetFiles(_dailyDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);

                if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date))
                {
                    continue;
                }

                if (date >= today || date < minDate)
                {
                    continue;
                }

                var list = LoadList(file);
                
                foreach (var entry in list)
                {
                    var score = ScoreText(entry.Content, tokens, entry.Tags);
                    if (score > 0)
                    {
                        items.Add(new RelatedDailyItem
                        {
                            DateKey = name,
                            Content = entry.Content,
                            Score = score
                        });
                    }
                }
            }

            return items
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.DateKey)
                .Take(maxItems)
                .ToList();
        }

        private List<RelatedNote> LoadRelevantNotes(HashSet<string> tokens, int maxNotes)
        {
            var notes = new List<RelatedNote>();

            foreach (var file in Directory.GetFiles(_notesDir, "*.md"))
            {
                var content = File.ReadAllText(file);
                var score = ScoreText(content, tokens, null);
                if (score <= 0)
                {
                    continue;
                }

                var preview = ExtractPreview(content, 2);
                var title = Path.GetFileNameWithoutExtension(file);

                notes.Add(new RelatedNote
                {
                    Title = title,
                    Preview = preview,
                    Score = score
                });
            }

            return notes
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Title)
                .Take(maxNotes)
                .ToList();
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

        private static HashSet<string> ExtractTokens(string? text)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            foreach (Match match in Regex.Matches(text, @"[\p{L}\p{N}]+"))
            {
                var token = match.Value.Trim();
                
                if (token.Length == 0)
                {
                    continue;
                }

                if (token.Length == 1 && !IsCjk(token[0]))
                {
                    continue;
                }

                tokens.Add(token.ToLowerInvariant());
            }

            return tokens;
        }

        private static bool IsCjk(char c)
        {
            return (c >= '\u4E00' && c <= '\u9FFF')
                || (c >= '\u3400' && c <= '\u4DBF');
        }

        private static int ScoreText(string content, HashSet<string> tokens, List<string>? tags)
        {
            if (tokens.Count == 0 || string.IsNullOrWhiteSpace(content))
            {
                return 0;
            }

            var normalized = NormalizeContent(content);
            var score = 0;
            foreach (var token in tokens)
            {
                if (normalized.Contains(token.ToLowerInvariant()))
                {
                    score++;
                }
            }

            if (tags != null)
            {
                foreach (var tag in tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag) && tokens.Contains(tag.Trim().ToLowerInvariant()))
                    {
                        score++;
                    }
                }
            }

            return score;
        }

        private static string ExtractPreview(string content, int maxLines)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return "(empty)";
            }

            var previewLines = lines
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Take(maxLines)
                .Select(line => line.Trim())
                .ToList();

            return previewLines.Count == 0 ? "(empty)" : string.Join(" / ", previewLines);
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

    internal class RelatedDailyItem
    {
        public string DateKey { get; set; } = "";
        public string Content { get; set; } = "";
        public int Score { get; set; }
    }

    internal class RelatedNote
    {
        public string Title { get; set; } = "";
        public string Preview { get; set; } = "";
        public int Score { get; set; }
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
