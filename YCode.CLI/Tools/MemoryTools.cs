using System;

namespace YCode.CLI
{
    [Inject<IAgentTool>]
    internal sealed class MemoryWriterTool : IAgentTool
    {
        private readonly MemoryManager _memory;
        private readonly AgentContext _context;

        public MemoryWriterTool(MemoryManager memory, AgentContext context)
        {
            _memory = memory;
            _context = context;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Save long-term memory items (profile, daily, or project).",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "category": { "type": "string", "enum": ["profile", "daily", "project"] },
                            "content": { "type": "string" },
                            "date": { "type": "string", "description": "YYYY-MM-DD for daily memory (optional)" },
                            "tags": { "type": "array", "items": { "type": "string" } },
                            "project": { "type": "string", "description": "Project key for project memory (optional, defaults to current workspace name)" }
                        },
                        "required": ["category", "content"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "MemoryWriter";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string category, string content, string? date = null, List<string>? tags = null, string? project = null)
        {
            try
            {
                _context.SetInt("last_memory_activity_round", _context.GetInt("total_rounds"));

                return _memory.AddMemory(category, content, date, tags, project);
            }
            catch (Exception ex)
            {
                return $"Error updating memory: {ex.Message}";
            }
        }
    }

    [Inject<IAgentTool>]
    internal sealed class MemorySearchTool : IAgentTool
    {
        private readonly MemoryManager _memory;
        private readonly AgentContext _context;

        public MemorySearchTool(MemoryManager memory, AgentContext context)
        {
            _memory = memory;
            _context = context;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Search memories across profile, daily, and project scopes.",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "query": { "type": "string" },
                            "limit": { "type": "integer", "minimum": 1, "maximum": 30 }
                        },
                        "required": ["query"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "MemorySearch";
        public string Description { get; }
        public bool IsReadOnly => true;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string query, int limit = 8)
        {
            try
            {
                _context.SetInt("last_memory_activity_round", _context.GetInt("total_rounds"));

                return _memory.Search(query, limit);
            }
            catch (Exception ex)
            {
                return $"Error searching memory: {ex.Message}";
            }
        }
    }
}



