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
        private const int MaxItemsPerList = 80;
        private readonly string _rootDir;
        private readonly string _profilePath;
        private readonly string _dailyDir;
        private readonly string _notesDir;
        private readonly string _projectsDir;
        private readonly string _workDir;
        private List<MemoryItem>? _profile;
        private bool _dailyCleanupDone;
        private int _lastHeartbeatRound = -1;
        private string? _lastHeartbeatFingerprint;

        public MemoryManager(string workDir)
        {
            _workDir = workDir;

            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            _rootDir = Path.Combine(userProfile, ".ycode", "memory");
            _profilePath = Path.Combine(_rootDir, "profile.json");
            _dailyDir = Path.Combine(_rootDir, "daily");
            _notesDir = Path.Combine(_rootDir, "notes");
            _projectsDir = Path.Combine(_workDir, ".ycode", "projects");

            Directory.CreateDirectory(_rootDir);
            Directory.CreateDirectory(_dailyDir);
            Directory.CreateDirectory(_notesDir);
            Directory.CreateDirectory(_projectsDir);
        }

        public string AddMemory(string category, string content, string? date, List<string>? tags, string? project = null)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return "Error: memory content cannot be empty.";
            }

            EnsureDailyCleanup();

            var normalizedCategory = category?.Trim().ToLowerInvariant() ?? "";
            var normalizedContent = NormalizeContent(content);
            var now = DateTimeOffset.Now.ToString("O");
            var normalizedTags = NormalizeTags(tags);

            if (normalizedCategory == "profile")
            {
                var profile = LoadProfile();
                var existing = profile.FirstOrDefault(x => NormalizeContent(x.Content) == normalizedContent);
                if (existing != null)
                {
                    existing.UpdatedAt = now;
                    MergeTags(existing, normalizedTags);
                    SaveProfile(profile);
                    return "Memory updated: profile item already exists.";
                }

                profile.Add(new MemoryItem
                {
                    Content = content.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    Tags = normalizedTags
                });

                TrimList(profile, MaxItemsPerList);
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
                    MergeTags(existing, normalizedTags);
                    SaveDaily(dateKey, list);
                    return $"Memory updated: daily {dateKey} item already exists.";
                }

                list.Add(new MemoryItem
                {
                    Content = content.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    Tags = normalizedTags
                });

                TrimList(list, MaxItemsPerList);
                SaveDaily(dateKey, list);
                return $"Memory saved: daily {dateKey}.";
            }

            if (normalizedCategory == "project")
            {
                var projectKey = ResolveProjectKey(project);
                if (projectKey == null)
                {
                    return "Error: project key cannot be empty.";
                }

                var list = LoadProjectMemories(projectKey);
                var existing = list.FirstOrDefault(x => NormalizeContent(x.Content) == normalizedContent);
                if (existing != null)
                {
                    existing.UpdatedAt = now;
                    MergeTags(existing, normalizedTags);
                    SaveProjectMemories(projectKey, list);
                    return $"Memory updated: project {projectKey} item already exists.";
                }

                list.Add(new MemoryItem
                {
                    Content = content.Trim(),
                    CreatedAt = now,
                    UpdatedAt = now,
                    Tags = normalizedTags
                });

                TrimList(list, MaxItemsPerList);
                SaveProjectMemories(projectKey, list);
                return $"Memory saved: project {projectKey}.";
            }

            return "Error: category must be profile, daily, or project.";
        }

        public string Search(string query, int limit = 8)
        {
            var tokens = ExtractTokens(query);
            if (tokens.Count == 0)
            {
                return "No search tokens provided.";
            }

            EnsureDailyCleanup();
            var results = new List<MemorySearchResult>();

            foreach (var item in LoadProfile())
            {
                var score = ScoreText(item.Content, tokens, item.Tags);
                if (score > 0)
                {
                    results.Add(new MemorySearchResult("profile", item.Content, score));
                }
            }

            foreach (var file in Directory.GetFiles(_dailyDir, "*.json"))
            {
                var dateKey = Path.GetFileNameWithoutExtension(file);
                foreach (var item in LoadList(file))
                {
                    var score = ScoreText(item.Content, tokens, item.Tags);
                    if (score > 0)
                    {
                        results.Add(new MemorySearchResult($"daily:{dateKey}", item.Content, score));
                    }
                }
            }

            foreach (var dir in Directory.GetDirectories(_projectsDir))
            {
                var projectKey = Path.GetFileName(dir);
                foreach (var item in LoadProjectMemories(projectKey))
                {
                    var score = ScoreText(item.Content, tokens, item.Tags);
                    if (score > 0)
                    {
                        results.Add(new MemorySearchResult($"project:{projectKey}", item.Content, score));
                    }
                }
            }

            if (results.Count == 0)
            {
                return "No memory matched your query.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Memory search results:");

            foreach (var result in results
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Scope)
                .Take(Math.Clamp(limit, 1, 30)))
            {
                sb.AppendLine($"- [{result.Scope}] {result.Content}");
            }

            return sb.ToString().TrimEnd();
        }

        public bool MaybeSaveHeartbeat(string userInput, int roundNumber)
        {
            if (string.IsNullOrWhiteSpace(userInput) || userInput.Trim().Length < 12)
            {
                return false;
            }

            if (_lastHeartbeatRound >= 0 && roundNumber - _lastHeartbeatRound < 6)
            {
                return false;
            }

            var normalized = NormalizeContent(userInput);
            if (normalized == _lastHeartbeatFingerprint)
            {
                return false;
            }

            _lastHeartbeatFingerprint = normalized;
            _lastHeartbeatRound = roundNumber;

            var compact = CompactText(userInput, 140);

            _ = AddMemory("daily", $"Heartbeat: user focus -> {compact}", null, ["heartbeat", "auto"]);

            return true;
        }

        public ChatMessage? BuildContextBlock(string? userInput = null, int maxProfile = 20)
        {
            EnsureDailyCleanup();

            var today = DateTime.Today;
            var todayKey = today.ToString("yyyy-MM-dd");
            var profile = LoadProfile();
            var dailyList = LoadDaily(todayKey);
            var tokens = ExtractTokens(userInput);
            var relatedDaily = tokens.Count > 0 ? LoadRelevantDaily(today, tokens, DailyRetentionDays, 5) : [];
            var relatedNotes = tokens.Count > 0 ? LoadRelevantNotes(tokens, 3) : [];

            var projectKey = ResolveProjectKey(null)!;
            var projectMemories = LoadProjectMemories(projectKey);
            var projectAgents = LoadProjectAgents(projectKey);

            if (profile.Count == 0 && dailyList.Count == 0 && relatedNotes.Count == 0 && relatedDaily.Count == 0
                && projectMemories.Count == 0 && string.IsNullOrWhiteSpace(projectAgents))
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.AppendLine("<memory>");

            if (profile.Count > 0)
            {
                sb.AppendLine("profile:");
                foreach (var item in profile.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(maxProfile))
                {
                    sb.AppendLine($"- {item.Content}");
                }
            }

            if (projectMemories.Count > 0)
            {
                sb.AppendLine($"project ({projectKey}):");
                foreach (var item in projectMemories.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(20))
                {
                    sb.AppendLine($"- {item.Content}");
                }
            }

            if (!string.IsNullOrWhiteSpace(projectAgents))
            {
                sb.AppendLine($"project-agents ({projectKey}/AGENTS.md):");
                sb.AppendLine(projectAgents);
            }

            if (dailyList.Count > 0)
            {
                sb.AppendLine($"daily ({todayKey}):");
                foreach (var item in dailyList.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(30))
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
            _profile ??= LoadList(_profilePath);
            return _profile;
        }

        private void SaveProfile(List<MemoryItem> profile) => SaveList(_profilePath, profile);

        private List<MemoryItem> LoadDaily(string dateKey) => LoadList(Path.Combine(_dailyDir, $"{dateKey}.json"));

        private string LoadProjectAgents(string projectKey)
        {
            var path = Path.Combine(_projectsDir, projectKey, "AGENTS.md");
            if (!File.Exists(path)) return string.Empty;
            var content = File.ReadAllText(path).Trim();
            return string.IsNullOrWhiteSpace(content) ? string.Empty : content;
        }

        private List<MemoryItem> LoadProjectMemories(string projectKey)
        {
            var path = Path.Combine(_projectsDir, projectKey, "memory.json");
            return LoadList(path);
        }

        private void SaveProjectMemories(string projectKey, List<MemoryItem> items)
        {
            var dir = Path.Combine(_projectsDir, projectKey);
            Directory.CreateDirectory(dir);
            SaveList(Path.Combine(dir, "memory.json"), items);
        }

        private void EnsureDailyCleanup()
        {
            if (_dailyCleanupDone) return;

            var cutoff = DateTime.Today.AddDays(-DailyRetentionDays);
            foreach (var file in Directory.GetFiles(_dailyDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date)) continue;
                if (date < cutoff) File.Delete(file);
            }

            _dailyCleanupDone = true;
        }

        private void SaveDaily(string dateKey, List<MemoryItem> items) => SaveList(Path.Combine(_dailyDir, $"{dateKey}.json"), items);

        private List<RelatedDailyItem> LoadRelevantDaily(DateTime today, HashSet<string> tokens, int lookbackDays, int maxItems)
        {
            var minDate = today.AddDays(-lookbackDays);
            var items = new List<RelatedDailyItem>();

            foreach (var file in Directory.GetFiles(_dailyDir, "*.json"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var date)) continue;
                if (date >= today || date < minDate) continue;

                foreach (var entry in LoadList(file))
                {
                    var score = ScoreText(entry.Content, tokens, entry.Tags);
                    if (score > 0)
                    {
                        items.Add(new RelatedDailyItem { DateKey = name, Content = entry.Content, Score = score });
                    }
                }
            }

            return items.OrderByDescending(x => x.Score).ThenByDescending(x => x.DateKey).Take(maxItems).ToList();
        }

        private List<RelatedNote> LoadRelevantNotes(HashSet<string> tokens, int maxNotes)
        {
            var notes = new List<RelatedNote>();

            foreach (var file in Directory.GetFiles(_notesDir, "*.md"))
            {
                var content = File.ReadAllText(file);
                var score = ScoreText(content, tokens, null);
                if (score <= 0) continue;

                notes.Add(new RelatedNote
                {
                    Title = Path.GetFileNameWithoutExtension(file),
                    Preview = ExtractPreview(content, 2),
                    Score = score
                });
            }

            return notes.OrderByDescending(x => x.Score).ThenBy(x => x.Title).Take(maxNotes).ToList();
        }

        private List<MemoryItem> LoadList(string path)
        {
            if (!File.Exists(path)) return [];

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
            var folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            var json = JsonSerializer.Serialize(items, JsonOptions());
            File.WriteAllText(path, json);
        }

        private static JsonSerializerOptions JsonOptions() => new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private static string NormalizeContent(string content) => content.Trim().ToLowerInvariant();

        private static List<string> NormalizeTags(List<string>? tags)
        {
            if (tags == null || tags.Count == 0) return [];

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    set.Add(tag.Trim().ToLowerInvariant());
                }
            }

            return [.. set];
        }

        private static HashSet<string> ExtractTokens(string? text)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text)) return tokens;

            foreach (Match match in Regex.Matches(text, @"[\p{L}\p{N}]+"))
            {
                var token = match.Value.Trim();
                if (token.Length == 0) continue;
                if (token.Length == 1 && !IsCjk(token[0])) continue;
                tokens.Add(token.ToLowerInvariant());
            }

            return tokens;
        }

        private static bool IsCjk(char c) =>
            (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\u3400' && c <= '\u4DBF');

        private static int ScoreText(string content, HashSet<string> tokens, List<string>? tags)
        {
            if (tokens.Count == 0 || string.IsNullOrWhiteSpace(content)) return 0;

            var normalized = NormalizeContent(content);
            var score = tokens.Count(token => normalized.Contains(token, StringComparison.OrdinalIgnoreCase));

            if (tags != null)
            {
                score += tags.Count(tag => !string.IsNullOrWhiteSpace(tag) && tokens.Contains(tag.Trim().ToLowerInvariant()));
            }

            return score;
        }

        private static string ExtractPreview(string content, int maxLines)
        {
            var lines = content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return "(empty)";

            var previewLines = lines.Where(line => !string.IsNullOrWhiteSpace(line)).Take(maxLines).Select(line => line.Trim()).ToList();
            return previewLines.Count == 0 ? "(empty)" : string.Join(" / ", previewLines);
        }

        private static void MergeTags(MemoryItem item, List<string>? tags)
        {
            if (tags == null || tags.Count == 0) return;

            var set = new HashSet<string>(item.Tags ?? [], StringComparer.OrdinalIgnoreCase);
            foreach (var tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag)) set.Add(tag.Trim().ToLowerInvariant());
            }

            item.Tags = [.. set];
        }

        private static void TrimList(List<MemoryItem> list, int max)
        {
            if (list.Count <= max) return;

            var ordered = list.OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt).Take(max).ToList();
            list.Clear();
            list.AddRange(ordered);
        }

        private string? ResolveProjectKey(string? project)
        {
            var raw = string.IsNullOrWhiteSpace(project)
                ? new DirectoryInfo(_workDir).Name
                : project.Trim();

            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var key = Regex.Replace(raw, @"[^a-zA-Z0-9._-]+", "-").Trim('-').ToLowerInvariant();
            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        private static string CompactText(string text, int maxChars)
        {
            var compact = Regex.Replace(text.Trim(), @"\s+", " ");
            return compact.Length <= maxChars ? compact : compact[..maxChars] + "...";
        }

        private static string? ResolveDateKey(string? date)
        {
            if (string.IsNullOrWhiteSpace(date)) return DateTime.Now.ToString("yyyy-MM-dd");
            return DateTime.TryParse(date, out var parsed) ? parsed.ToString("yyyy-MM-dd") : null;
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

    internal class MemorySearchResult(string scope, string content, int score)
    {
        public string Scope { get; } = scope;
        public string Content { get; } = content;
        public int Score { get; } = score;
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
