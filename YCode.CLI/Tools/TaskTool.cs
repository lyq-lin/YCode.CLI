namespace YCode.CLI
{
    [Inject<IAgentTool>]
    internal sealed class TaskTool : IAgentTool
    {
        private readonly AppConfig _config;
        private readonly McpManager _mcp;
        private readonly AgentManager _manager;

        public TaskTool(AppConfig config, McpManager mcp, AgentManager manager)
        {
            _config = config;
            _mcp = mcp;
            _manager = manager;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Spawn a subagent for a focused subtask. Subagents run in ISOLATED context - they don't see parent's history. Use this to keep the main conversation clean. \n Agent types: \n {{_manager.GetDescription()}} \n Example uses:\n - Task(explore): \"Find all files using the auth module.\"\n - Task(plan): \"Design a migration strategy for the database\"\n - Task(code): \"Implement the user registration form\"\n ",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "description": { "type": "string", "description": "Short task name (3-5 words) for progress display" },
                            "prompt": { "type": "string", "description": "Detailed instructions for the subagent" },
                            "agent_type": { "type": "string", "enum": [], "description": "Type of agent to spawn" }
                        },
                        "required": ["description", "prompt", "agent_type"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "Task";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private async Task<string> Run(string description, string prompt, string agentType)
        {
            if (!_manager.Agents.ContainsKey(agentType))
            {
                throw new NotSupportedException($"Agent type '{agentType}' is not supported.");
            }

            var config = _manager.Agents[agentType];

            var sub_system = $"""
                You are a {agentType} subagent operating INSIDE the user's repository at {_config.WorkDir}.\n

                {config["prompt"]}

                Complete the task and return a clear, concise summary.
                """;

            var sub_tools = GetToolsForAgent(agentType);

            var sub_messages = new List<ChatMessage>()
            {
                new ChatMessage()
                {
                    Role = ChatRole.User,
                    Contents = [new TextContent(prompt)]
                }
            };

            var sub_agent = new OpenAIClient(
                new ApiKeyCredential(_config.Key),
                new OpenAIClientOptions()
                {
                    Endpoint = new Uri(_config.Uri),

                }).GetChatClient(_config.Model)
                .AsAIAgent(sub_system, tools: sub_tools);

            AnsiConsole.MarkupLine($"[dim]    [/][bold cyan]🤖 [[{EscapeMarkup(agentType)}]][/] {EscapeMarkup(description)}");

            var start = DateTime.Now;

            var sub_tools_use = new List<FunctionResultContent>();

            var next = String.Empty;

            try
            {
                await foreach (var resp in sub_agent.RunStreamingAsync(sub_messages))
                {
                    foreach (var content in resp.Contents)
                    {
                        switch (content)
                        {
                            case TextContent text:
                                {
                                    next += text.Text;
                                }
                                break;
                            case FunctionResultContent result:
                                {
                                    next += $"<previous_tool_use id='{result.CallId}'>{result.Result}</previous_tool_use>";

                                    sub_tools_use.Add(result);

                                    AnsiConsole.MarkupLine($"[dim]    [/][bold cyan]🤖 [[{EscapeMarkup(agentType)}]][/] {EscapeMarkup(description)} ... [dim]{sub_tools_use.Count} tools, {(DateTime.Now - start).TotalSeconds:F1}s[/]");
                                }
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            sub_messages.Add(new ChatMessage(ChatRole.Assistant, next));

            AnsiConsole.MarkupLine($"[dim]    [/][bold cyan]✓ [[{EscapeMarkup(agentType)}]][/] {EscapeMarkup(description)} - done ([dim]{sub_tools_use.Count} tools, {(DateTime.Now - start).TotalSeconds:F1}s[/])");

            if (!String.IsNullOrWhiteSpace(next))
            {
                return next;
            }

            return "(subagent returned no text)";
        }

        private AITool[] GetToolsForAgent(string agentType)
        {
            if (_manager.Agents.TryGetValue(agentType, out var meta))
            {
                if (meta.TryGetPropertyValue("tools", out var tools))
                {
                    if (tools?.ToString() == "*")
                    {
                        return _mcp.GetTools();
                    }
                    else if (tools is JsonArray toolArray)
                    {
                        var selectedTools = new List<AITool>();

                        foreach (var toolNameNode in toolArray)
                        {
                            var toolName = toolNameNode?.ToString();

                            if (toolName != null)
                            {
                                AITool[] tool = [];

                                if (toolName == "bash")
                                {
                                    tool = _mcp.GetTools(x => x.Name is "run" or "run_background" or "kill_background" or "list_background");
                                }
                                else
                                {
                                    tool = _mcp.GetTools(x => x.Name == toolName);
                                }

                                if (tool.Length > 0)
                                {
                                    selectedTools.AddRange(tool);
                                }
                            }
                        }

                        return [.. selectedTools];
                    }
                }
            }

            throw new NotSupportedException($"Agent type '{agentType}' is not supported.");
        }

        private static string EscapeMarkup(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            return text
                .Replace("[", "[[")
                .Replace("]", "]]");
        }
    }
}




