namespace YCode.CLI
{
    [Inject]
    internal sealed class AgentContext
    {
        public List<ChatMessage> PendingContextBlocks { get; } = [];

        public Dictionary<string, int> State { get; } = new()
        {
            ["rounds_without_todo"] = 0,
            ["last_memory_activity_round"] = 0,
            ["total_rounds"] = 0
        };

        public int GetInt(string key)
        {
            return State.TryGetValue(key, out var value) ? value : 0;
        }

        public void SetInt(string key, int value)
        {
            State[key] = value;
        }

        public void EnsureContextBlock(string text)
        {
            var alreadyQueued = PendingContextBlocks.Any(x =>
                x.Role == ChatRole.User &&
                x.Contents.OfType<TextContent>().Any(c => c.Text == text));

            if (alreadyQueued)
            {
                return;
            }

            PendingContextBlocks.Add(new ChatMessage
            {
                Role = ChatRole.User,
                Contents = [new TextContent(text)]
            });
        }
    }
}


