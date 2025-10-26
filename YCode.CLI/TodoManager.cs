namespace YCode.CLI
{
    internal class TodoManager
    {
        private List<Dictionary<string, string>> _items = [];

        public static readonly string[] TODO_STATUSES = { "pending", "in_progress", "completed" };

        public string Update(List<Dictionary<string, object>> inputItems)
        {
            if (inputItems == null)
                throw new ArgumentException("Todo items must be a list");

            var cleaned = new List<Dictionary<string, string>>();

            var seenIds = new HashSet<string>();

            int inProgressCount = 0;

            for (int i = 0; i < inputItems.Count; i++)
            {
                var raw = inputItems[i];

                if (raw == null)
                    throw new ArgumentException("Each todo must be an object");

                // 处理ID
                string todoId = (raw.ContainsKey("id") ? raw["id"]?.ToString() : null) ?? (i + 1).ToString();

                if (seenIds.Contains(todoId))
                    throw new ArgumentException($"Duplicate todo id: {todoId}");

                seenIds.Add(todoId);

                // 验证内容
                string content = (raw.ContainsKey("content") ? raw["content"]?.ToString() : "")?.Trim() ?? "";

                if (string.IsNullOrEmpty(content))
                    throw new ArgumentException("Todo content cannot be empty");

                // 验证状态
                string status = (raw.ContainsKey("status") ? raw["status"]?.ToString() : TODO_STATUSES[0])?.ToLower() ?? TODO_STATUSES[0];

                if (!TODO_STATUSES.Contains(status))
                    throw new ArgumentException($"Status must be one of {string.Join(", ", TODO_STATUSES)}");

                if (status == "in_progress")
                    inProgressCount++;

                // 验证activeForm
                string activeForm = (raw.ContainsKey("activeForm") ? raw["activeForm"]?.ToString() : "")?.Trim() ?? "";

                if (string.IsNullOrEmpty(activeForm))
                    throw new ArgumentException("Todo activeForm cannot be empty");

                // 创建清理后的任务项
                var cleanedItem = new Dictionary<string, string>
                {
                    ["id"] = todoId,
                    ["content"] = content,
                    ["status"] = status,
                    ["active_form"] = activeForm
                };

                cleaned.Add(cleanedItem);

                // 数量限制
                if (cleaned.Count > 20)
                    throw new ArgumentException("Todo list is limited to 20 items in the demo");
            }

            // 进行中任务约束
            if (inProgressCount > 1)
                throw new ArgumentException("Only one task can be in_progress at a time");

            // 更新内部状态
            _items = cleaned;

            return Render();
        }

        public string Render()
        {
            if (_items.Count == 0)
                return "☐ No todos yet";

            var lines = new List<string>();

            foreach (var todo in _items)
            {
                string mark = todo["status"] == "completed" ? "☒" : "☐";

                lines.Add(DecorateLine(mark, todo));
            }

            return string.Join("\n", lines);
        }

        public Dictionary<string, int> Status()
        {
            return new Dictionary<string, int>
            {
                ["total"] = _items.Count,
                ["completed"] = _items.Count(t => t["status"] == "completed"),
                ["in_progress"] = _items.Count(t => t["status"] == "in_progress")
            };
        }

        public string DecorateLine(string mark, Dictionary<string, string> todo)
        {
            string text = $"{mark} {todo["content"]}";

            string status = todo["status"];

            // 这里简化了颜色显示，实际使用时可以添加ANSI颜色
            if (status == "completed")
                return $"✓ {todo["content"]}";
            if (status == "in_progress")
                return $"→ {todo["content"]}";
            return $"○ {todo["content"]}";
        }
    }
}
