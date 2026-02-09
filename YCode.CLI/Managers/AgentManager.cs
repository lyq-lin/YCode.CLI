namespace YCode.CLI
{
    [Inject]
    internal sealed class AgentManager
    {
        public Dictionary<string, JsonObject> Agents { get; }

        public AgentManager()
        {
            Agents = new Dictionary<string, JsonObject>()
            {
                ["explore"] = new JsonObject
                {
                    ["description"] = "Read-only agent for exploring code, finding files, searching",
                    ["tools"] = new JsonArray("bash", "read_file"),
                    ["prompt"] = "You are an exploration agent. Search and analyze, but never modify files. Return a concise summary.",
                },
                ["code"] = new JsonObject
                {
                    ["description"] = "Full agent for implementing features and fixing bugs",
                    ["tools"] = "*",
                    ["prompt"] = "You are a coding agent. Implement the requested changes efficiently.",
                },
                ["plan"] = new JsonObject
                {
                    ["description"] = "Planning agent for designing implementation strategies",
                    ["tools"] = new JsonArray("bash", "read_file"),
                    ["prompt"] = "You are a planning agent. Analyze the codebase and output a numbered implementation plan. Do NOT make changes.",
                }
            };
        }

        public string GetDescription()
        {
            return String.Join("\n", Agents.Select(x => $"- {x.Key}: {x.Value["description"]}"));
        }
    }
}


