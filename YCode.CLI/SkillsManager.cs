using System.ComponentModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace YCode.CLI
{
    internal class SkillsManager
    {
        private readonly List<SkillMeta> _skills;
        private readonly string _skillsPath;

        public SkillsManager(string skillsPath)
        {
            _skillsPath = skillsPath;

            var skillDirs = Directory.GetDirectories(skillsPath);

            _skills = [.. skillDirs.Select(d =>
            {
                var yaml = File.ReadAllText(Path.Combine(d, "SKILL.md"))
                               .Split("---")[1];

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(UnderscoredNamingConvention.Instance)
                    .Build();

                return deserializer.Deserialize<SkillMeta>(yaml);
            })];
        }

        public List<SkillMeta> Skills => _skills;

        [Description("Load the full instruction text (SKILL.md) for the specified skill and return it as a string.")]
        public string ReadSkill([Description("""
            "skill": {
                "type": "object",
                "properties": {
                    "name": {"type": "string"},
                    "description": {"type": "string"}
                 },
                "required": ["name"],
                "additionalProperties": False
            }
        """)] SkillMeta skill)
        {
            var dir = Path.Combine(_skillsPath, skill.Name);

            return File.ReadAllText(Path.Combine(dir, "SKILL.md"));
        }
    }

    internal class SkillMeta
    {
        public string Name { get; set; } = String.Empty;
        public string? Description { get; set; }
    }
}
