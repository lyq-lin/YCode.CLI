using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;

namespace YCode.CLI
{
    internal class SkillsManager
    {
        private readonly IDeserializer _builder;
        private readonly string _skillsDir;
        private readonly Dictionary<string, JsonObject> _skills;

        public SkillsManager()
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            
            _skillsDir = Path.Combine(userProfile, ".ycode", "skills");

            _skills = [];

            _builder = new DeserializerBuilder().Build();

            this.LoadSkills();
        }

        private void LoadSkills()
        {
            if (!Directory.Exists(_skillsDir))
            {
                return;
            }

            var skill_dir = new DirectoryInfo(_skillsDir);

            foreach (var dir in skill_dir.GetDirectories())
            {
                if (!dir.Exists)
                {
                    continue;
                }

                var md = Path.Combine(dir.FullName, "SKILL.md");

                if (!File.Exists(md))
                {
                    continue;
                }

                var skill = this.ParseSkill(md);

                if (skill != null && skill.ContainsKey("name"))
                {
                    _skills[skill["name"]?.ToString()!] = skill;
                }
            }
        }

        private JsonObject? ParseSkill(string path)
        {
            var content = File.ReadAllText(path);

            var match = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);

            if (!match.Success || match.Groups.Count < 2)
            {
                return null;
            }

            var metadata = new JsonObject();

            try
            {
                var yamlObject = _builder.Deserialize<Dictionary<string, object>>(match.Groups[1].Value);

                foreach (var kvp in yamlObject)
                {
                    metadata[kvp.Key] = kvp.Value?.ToString() ?? "";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"YAML解析错误: {ex.Message}");
            }

            if (!metadata.ContainsKey("name") || !metadata.ContainsKey("description"))
            {
                return null;
            }

            return new JsonObject
            {
                ["name"] = metadata["name"]?.ToString(),
                ["description"] = metadata["description"]?.ToString(),
                ["body"] = match.Groups[2].Value,
                ["path"] = path,
                ["dir"] = Path.GetDirectoryName(path)
            };
        }

        public string GetDescription()
        {
            if (_skills.Count == 0)
            {
                return "(no skills available)";
            }

            return String.Join("\n", _skills.Values.Select(skill => $"- {skill["name"]}: {skill["description"]}"));
        }

        public string GetSkillContent(string name)
        {
            if (!_skills.ContainsKey(name))
            {
                return String.Empty;
            }

            var skill = _skills[name];

            var content = $"# Skill: {skill["name"]}\n\n{skill["body"]}";

            List<string> resources = [];

            var supports = new Dictionary<string, string>()
            {
                { "scripts", "Scripts" },
                { "references", "References" },
                { "assets", "Assets" }
            };

            foreach (var kvp in supports)
            {
                var folder_path = Path.Combine(skill["dir"]?.ToString()!, kvp.Key);

                if (Path.Exists(folder_path))
                {
                    var files = Directory.GetFiles(folder_path, "*");

                    if (files.Length > 0)
                    {
                        resources.Add($"{kvp.Value}: {String.Join(',', files.Select(x => Path.GetFileName(x)))}");
                    }
                }
            }

            if (resources.Count > 0)
            {
                content += $"\n\n **Available resource in {skill["dir"]}:**\n";

                content += String.Join("\n", resources.Select(r => $"- {r}"));
            }

            return content;
        }

        public string[] GetSkills()
        {
            return [.. _skills.Keys];
        }
    }
}



