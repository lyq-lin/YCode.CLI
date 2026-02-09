namespace YCode.CLI
{
    [Inject<IAgentTool>]
    internal sealed class SkillTool : IAgentTool
    {
        private readonly SkillsManager _skills;

        public SkillTool(SkillsManager skills)
        {
            _skills = skills;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Load a skill to gain specialized knowledge for a task. Available skills: \n {{_skills.GetDescription()}} \n When to use:\n - IMMEDIATELY when user task matches a skill description.\n - Before attempting domain-specific work. (PDF, MCP, etc.)\n The skill content will be injected into the conversation, giving you detailed instructions and access to resources.",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "skill": { "type": "string", "description": "Name of the skill to load." }
                        },
                        "required": ["skill"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "Skill";
        public string Description { get; }
        public bool IsReadOnly => true;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string skillName)
        {
            var content = _skills.GetSkillContent(skillName);

            if (String.IsNullOrWhiteSpace(content))
            {
                var available = String.Join(',', _skills.GetSkills()) ?? "none";

                return $"Error: Unknown skill '{skillName}'. Available: {available}";
            }

            return $"""
                <skill-loaded name="{skillName}">
                {content}
                </skill-loaded>

                Follow the instructions in the skill above to complete the user's task.
                """;
        }
    }
}




