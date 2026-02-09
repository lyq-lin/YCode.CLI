using System;

namespace YCode.CLI
{
    [Inject<IAgentTool>]
    internal sealed class TodoTool : IAgentTool
    {
        private readonly TodoManager _todo;
        private readonly AgentContext _context;

        public TodoTool(TodoManager todo, AgentContext context)
        {
            _todo = todo;
            _context = context;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Update the shared todo list (pending | in_progress | completed).",
                    "arguments": {
                        "items": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "id": {"type": "string" },
                                    "content": {"type": "string" },
                                    "activeForm": {"type": "string" },
                                    "status": {"type": "string", "enum": ["pending", "in_progress", "completed"] },
                                },
                                "required": ["content", "activeForm", "status"],
                                "additionalProperties": false,
                            },
                            "maxItems": 20
                        }
                    }
                }
                """;
        }

        public string Name => "TodoWriter";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => true;

        public Delegate Handler => this.Run;

        private string Run(List<Dictionary<string, object>> items)
        {
            try
            {
                var summary = String.Empty;

                var result = _todo.Update(items);

                _context.SetInt("rounds_without_todo", 0);

                var status = _todo.Status();

                if (status["total"] == 0)
                {
                    summary = "No todos have been created.";
                }
                else
                {
                    summary = $"Status updated: {status["completed"]} completed, {status["in_progress"]} in progress.";
                }

                return result + $"{Environment.NewLine} {summary}";
            }
            catch (Exception ex)
            {
                return $"Error updating todos: {ex.Message}";
            }
        }
    }
}



