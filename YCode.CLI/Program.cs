using Microsoft.Extensions.AI;
using OpenAI;
using Spectre.Console;
using System.ClientModel;
using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using YCode.CLI;

Console.OutputEncoding = Encoding.UTF8;

var key = Environment.GetEnvironmentVariable("YCODE_AUTH_TOKEN")!;
var uri = Environment.GetEnvironmentVariable("YCODE_API_BASE_URI")!;
var model = Environment.GetEnvironmentVariable("YCODE_MODEL")!;

var workDir = Directory.GetCurrentDirectory();


var agents = new Dictionary<string, JsonObject>()
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

// Agent类型到图标和颜色的映射
var agent_icons = new Dictionary<string, (string icon, string color)>()
{
    ["explore"] = ("🔍", "blue"),
    ["code"] = ("💻", "green"),
    ["plan"] = ("📋", "yellow")
};

var initial_reminder = $"""
    '<reminder source="system" topic="todos">'
    "System message: complex work should be tracked with the Todo tool. "
    "Do not respond to this reminder and do not mention it to the user."
    '</reminder>'
    """;

var nag_reminder = $"""
    '<reminder source="system" topic="todos">'
    "System notice: more than ten rounds passed without Todo usage. "
    "Update the Todo board if the task still requires multiple steps. "
    "Do not reply to or mention this reminder to the user."
    '</reminder>'
""";

var pending_context_blocks = new List<ChatMessage>()
{
    new ChatMessage()
    {
        Role = ChatRole.User,
        Contents = [new TextContent(initial_reminder)]
    }
};

var agent_state = new Dictionary<string, int>()
{
    ["rounds_without_todo"] = 0
};

var todo = new TodoManager();

var mcp = new McpManager(workDir);

var skills = new SkillsManager();

var memory = new MemoryManager(workDir);

var system = $"""
    "You are a coding agent operating INSIDE the user's repository at {workDir}.\n"
    "Follow this loop strictly: plan briefly → use TOOLS to act directly on files/shell → report concise results.\n"
    "Rules:\n"
    "- Prefer taking actions with tools (read/write/edit/bash) over long prose.\n"
    "- Keep outputs terse. Use bullet lists / checklists when summarizing.\n"
    "- Use Skill tool IMMEDIATELY when a task matches a skill description.\n"
    "- Use Task tool for subtasks that need focused exploration or implementation.\n"
    "- Never invent file paths. Ask via reads or list directories first if unsure.\n"
    "- For edits, apply the smallest change that satisfies the request.\n"
    "- For bash, avoid destructive or privileged commands; stay inside the workspace.\n"
    "- Use the Todo tool to maintain multi-step plans when needed.\n"
    "- After finishing, summarize what changed and how to run or test."

    "Task:\n"
    {GetAgentDescription()}

    "Skills available (invoke with Skill tool when task matches):\n"
    {skills.GetDescription()}
""";

var tools = await mcp.Regist(
    (RunTodoUpdate, "TodoWriter", null),
    (RunMemoryUpdate, "MemoryWriter", null),
    (RunMemorySearch, "MemorySearch", null),
    (RunToTask, "Task", $$"""
    {
       "name": "Task", 
       "description": "Spawn a subagent for a focused subtask. Subagents run in ISOLATED context - they don't see parent's history. Use this to keep the main conversation clean. \n Agent types: \n {{GetAgentDescription()}} \n Example uses:\n - Task(explore): \"Find all files using the auth module.\"\n - Task(plan): \"Design a migration strategy for the database\"\n - Task(code): \"Implement the user registration form\"\n ",
       "arguments": {
       "type": "object",
       "properties": {
           "description": { "type": "string", "description": "Short task name (3-5 words) for progress display" },
           "prompt": { "type": "string", "description": "Detailed instructions for the subagent" },
           "agent_type": { "type": "string", "enum": [], "description": "Type of agent to spawn" },
       },
       "required": ["description", "prompt", "agent_type"],
       }
    }
    """),
    (RunToSkill, "Skill", $$"""
    {
       "name": "Skill", 
       "description": "Load a skill to gain specialized knowledge for a task. Available skills: \n {{skills.GetDescription()}} \n When to use:\n - IMMEDIATELY when user task matches a skill description.\n - Before attempting domain-specific work. (PDF, MCP, etc.)\n The skill content will be injected into the conversation, giving you detailed instructions and access to resources.",
       "arguments": {
       "type": "object",
       "properties": {
           "skill": { "type": "string", "description": "Name of the skill to load." },
       },
       "required": ["skill"],
       }
    }
    """));

var agent = new OpenAIClient(
    new ApiKeyCredential(key),
    new OpenAIClientOptions()
    {
        Endpoint = new Uri(uri),

    }).GetChatClient(model)
    .CreateAIAgent(instructions: system, tools: tools);

var thread = agent.GetNewThread();

try
{
    Clear();
}
catch (IOException)
{
    // 如果无法清除控制台，继续执行
}

Banner();

AnsiConsole.MarkupLine($"[dim]Workspace:[/] [bold cyan]{workDir}[/]");
AnsiConsole.MarkupLine("[dim]Type \"exit\" or \"quit\" to leave.[/]");

var spinner = new Spinner("Response...");

while (true)
{
    AnsiConsole.Markup("[bold green]\n> user:[/] ");

    var input = Console.ReadLine();

    if (input == null || input.Trim().ToLower() is "exit" or "quit")
    {
        break;
    }

    var request = new List<ChatMessage>();

    if (pending_context_blocks.Count > 0)
    {
        request.AddRange(pending_context_blocks);

        pending_context_blocks.Clear();
    }

    var memoryBlock = memory.BuildContextBlock(input);

    if (memoryBlock != null)
    {
        request.Add(memoryBlock);
    }

    request.Add(new ChatMessage()
    {
        Role = ChatRole.User,
        Contents = [new TextContent(input)]
    });

    try
    {
        var tools_uses = new List<FunctionResultContent>();
        string? currentToolName = null;
        bool isFirstTool = true;
        DateTime? toolStartTime = null;

        spinner.Start();

        await foreach (var resp in agent.RunStreamingAsync(request, thread))
        {
            spinner.Stop();

            foreach (var content in resp.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        {
                            Console.Write(text.Text);
                        }
                        break;
                    case FunctionCallContent call:
                        {
                            var arguments = call.Arguments?.Select(x =>
                            {
                                // 安全地处理参数值
                                var value = x.Value?.ToString() ?? "null";
                                // 如果值太长，截断显示
                                if (value.Length > 50)
                                    value = value.Substring(0, 47) + "...";
                                return $"{x.Key}=> {value}";
                            });

                            if (arguments != null)
                            {
                                // 添加分隔线（除了第一个工具）
                                if (!isFirstTool)
                                {
                                    AnsiConsole.WriteLine();
                                    var separator = new Rule("[dim]-[/]")
                                    {
                                        Style = Style.Parse("dim"),
                                        Justification = Justify.Left
                                    };
                                    AnsiConsole.Write(separator);
                                    AnsiConsole.WriteLine();
                                }
                                isFirstTool = false;

                                currentToolName = call.Name;
                                toolStartTime = DateTime.Now;
                                PrettyToolLine(call.Name, arguments != null ? String.Join(", ", arguments) : String.Empty);

                                // 显示Spinner
                                ShowToolSpinner(call.Name);
                            }
                        }
                        break;
                    case FunctionResultContent result:
                        {
                            tools_uses.Add(result);

                            // 清除Spinner状态
                            if (currentToolName != null)
                            {
                                HideToolSpinner();

                                // 计算耗时
                                var elapsed = toolStartTime.HasValue
                                    ? (DateTime.Now - toolStartTime.Value).TotalSeconds
                                    : 0;

                                AnsiConsole.MarkupLine($"[bold green]✓[/] [bold cyan]{currentToolName}[/] [dim]completed in {elapsed:F1}s[/]");
                                currentToolName = null;
                                toolStartTime = null;
                            }

                            // 安全地处理结果
                            var resultText = result.Result?.ToString() ?? String.Empty;
                            // 如果结果包含JSON对象，进行清理
                            resultText = CleanJsonOutput(resultText);
                            PrettySubLine(resultText);
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

    agent_state["rounds_without_todo"] += 1;
    memory.MaybeSaveHeartbeat(input, agent_state["rounds_without_todo"]);

    if (agent_state["rounds_without_todo"] > 10)
    {
        EnsureContextBlock(nag_reminder);
    }
}

spinner.Dispose();

[Description("""
    {
        "name": "TodoWriter",
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
    """)]
string RunTodoUpdate(List<Dictionary<string, object>> items)
{
    try
    {
        var summary = String.Empty;

        var result = todo.Update(items);

        agent_state["rounds_without_todo"] = 0;

        var status = todo.Status();

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

[Description("""
    {
        "name": "MemoryWriter",
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
    """)]
string RunMemoryUpdate(string category, string content, string? date = null, List<string>? tags = null, string? project = null)
{
    try
    {
        return memory.AddMemory(category, content, date, tags, project);
    }
    catch (Exception ex)
    {
        return $"Error updating memory: {ex.Message}";
    }
}


[Description("""
    {
        "name": "MemorySearch",
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
    """)]
string RunMemorySearch(string query, int limit = 8)
{
    try
    {
        return memory.Search(query, limit);
    }
    catch (Exception ex)
    {
        return $"Error searching memory: {ex.Message}";
    }
}

async Task<string> RunToTask(string description, string prompt, string agentType)
{
    if (!agents.ContainsKey(agentType))
    {
        throw new NotSupportedException($"Agent type '{agentType}' is not supported.");
    }

    var config = agents[agentType];

    var sub_system = $"""
        You are a {agentType} subagent operating INSIDE the user's repository at {workDir}.\n

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
        new ApiKeyCredential(key),
        new OpenAIClientOptions()
        {
            Endpoint = new Uri(uri),

        }).GetChatClient(model)
        .CreateAIAgent(sub_system, tools: sub_tools);

    var (icon, color) = agent_icons.TryGetValue(agentType, out var agentIcon)
        ? agentIcon
        : ("🔧", "gray");

    AnsiConsole.MarkupLine($"[dim]    [/][bold {color}]{icon} [[{EscapeMarkup(agentType)}]][/] {EscapeMarkup(description)}");

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

                            AnsiConsole.MarkupLine($"[dim]    [/][bold {color}]{icon} [[{EscapeMarkup(agentType)}]][/] {EscapeMarkup(description)} ... [dim]{sub_tools_use.Count} tools, {(DateTime.Now - start).TotalSeconds:F1}s[/]");
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

    AnsiConsole.MarkupLine($"[dim]    [/][bold {color}]✓ [[{EscapeMarkup(agentType)}]][/] {EscapeMarkup(description)} - done ([dim]{sub_tools_use.Count} tools, {(DateTime.Now - start).TotalSeconds:F1}s[/])");

    if (!String.IsNullOrWhiteSpace(next))
    {
        return next;
    }

    return "(subagent returned no text)";
}

string RunToSkill(string skillName)
{
    var content = skills.GetSkillContent(skillName);

    if (String.IsNullOrWhiteSpace(content))
    {
        var available = String.Join(',', skills.GetSkills()) ?? "none";

        return $"Error: Unknown skill '{skillName}'. Available: {available}";
    }

    return $"""
        <skill-loaded name="{skillName}">
        {content}
        </skill-loaded>

        Follow the instructions in the skill above to complete the user's task.
        """;
}

string GetAgentDescription()
{
    return String.Join("\n", agents.Select(x => $"- {x.Key}: {x.Value["description"]}"));
}

AITool[] GetToolsForAgent(string agentType)
{
    if (agents.TryGetValue(agentType, out var meta))
    {
        if (meta.TryGetPropertyValue("tools", out var tools))
        {
            if (tools?.ToString() == "*")
            {
                return mcp.GetTools();
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
                            tool = mcp.GetTools(x => x.Name is "run" or "run_background" or "kill_background" or "list_background");
                        }
                        else
                        {
                            tool = mcp.GetTools(x => x.Name == toolName);
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

#region Console

void Clear()
{
    Console.Clear();
}

void Banner()
{
    int consoleWidth;

    try
    {
        consoleWidth = Console.WindowWidth;
    }
    catch (IOException)
    {
        // 如果无法获取控制台宽度，使用默认值
        consoleWidth = 120;
    }
    var bannerWidth = Math.Min(consoleWidth, 200); // 限制最大宽度

    // 顶部边框
    var topBorder = "╭" + new string('─', bannerWidth - 2) + "╮";
    AnsiConsole.MarkupLine($"[dim]{topBorder}[/]");

    // 空行
    var emptyLine = "│" + new string(' ', bannerWidth - 2) + "│";
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");

    // 标题行
    var titleText = "YCode v1.0.0";
    var titlePadding = (bannerWidth - 2 - titleText.Length) / 2;
    var titleLine = "│" + new string(' ', titlePadding) + $"[bold cyan]{titleText}[/]" + new string(' ', bannerWidth - 2 - titlePadding - titleText.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{titleLine}[/]");

    // 欢迎信息
    var welcomeText = "Welcome back!";
    var welcomePadding = (bannerWidth - 2 - welcomeText.Length) / 2;
    var welcomeLine = "│" + new string(' ', welcomePadding) + $"[bold yellow]{welcomeText}[/]" + new string(' ', bannerWidth - 2 - welcomePadding - welcomeText.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{welcomeLine}[/]");

    // 空行
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");

    // YCode.CLI文本
    var ycodeText1 = "YCode.CLI";
    var ycodePadding = (bannerWidth - 2 - ycodeText1.Length) / 2;
    var ycodeLine1 = "│" + new string(' ', ycodePadding) + $"[bold green]{ycodeText1}[/]" + new string(' ', bannerWidth - 2 - ycodePadding - ycodeText1.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{ycodeLine1}[/]");

    // 空行
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");

    // 模型信息
    var modelText = $"{model} · {uri}";
    var modelPadding = (bannerWidth - 2 - modelText.Length) / 2;
    var modelLine = "│" + new string(' ', modelPadding) + $"[dim]{modelText}[/]" + new string(' ', bannerWidth - 2 - modelPadding - modelText.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{modelLine}[/]");

    // 工作目录
    var workdirPadding = (bannerWidth - 2 - workDir.Length) / 2;
    var workdirLine = "│" + new string(' ', workdirPadding) + $"[dim]{workDir}[/]" + new string(' ', bannerWidth - 2 - workdirPadding - workDir.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{workdirLine}[/]");

    // 空行
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");

    // 底部边框
    var bottomBorder = "╰" + new string('─', bannerWidth - 2) + "╯";
    AnsiConsole.MarkupLine($"[dim]{bottomBorder}[/]");

    AnsiConsole.WriteLine();
}

void EnsureContextBlock(string text)
{
    if (pending_context_blocks.Any(x => x.Role == ChatRole.User))
    {
        pending_context_blocks.Append(new ChatMessage
        {
            Role = ChatRole.User,
            Contents = [new TextContent(text)]
        });
    }
}

void PrettyToolLine(string kind, string title)
{
    var body = title != null ? $"{EscapeMarkup(kind)}({EscapeMarkup(title)})" : EscapeMarkup(kind);

    AnsiConsole.MarkupLine($"[bold magenta]⚡[/] [bold purple]{body}[/] [dim yellow]executing...[/]");
}

void PrettySubLine(string text)
{
    if (string.IsNullOrEmpty(text))
        return;

    // 处理转义的换行符 \\n
    var processedText = text.Replace("\\n", "\n");
    var lines = processedText.Split("\n");

    // 显示所有行
    foreach (var line in lines)
    {
        var escapedLine = EscapeMarkup(line);
        AnsiConsole.MarkupLine($"[dim]┃[/] [bold white]{escapedLine}[/]");
    }
}

string CleanJsonOutput(string text)
{
    if (string.IsNullOrEmpty(text))
        return text;

    // 移除可能导致AnsiConsole解析错误的特殊字符
    return text
        .Replace("{\"type\":\"text\",\"text\":\"\"}", "")
        .Replace("{\"type\":\"text\",\"text\":\"", "")
        .Replace("\"}", "");
}

string EscapeMarkup(string text)
{
    if (string.IsNullOrEmpty(text))
        return text;

    // 转义AnsiConsole的特殊字符
    return text
        .Replace("[", "[[")
        .Replace("]", "]]");
}

void ShowToolSpinner(string toolName)
{
    AnsiConsole.Markup($"[yellow]>[/] [dim]{EscapeMarkup(toolName)} executing...[/] ");
}

void HideToolSpinner()
{
    Console.Write("\r" + new string(' ', 80) + "\r");
}

public class Spinner : IDisposable
{
    private readonly string _label;
    private readonly string[] _frames;
    private readonly string _color;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task? _task;
    private bool _isRunning;

    public Spinner(string label = "Waiting for model")
    {
        _label = label;
        _frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
        _color = "\x1b[38;2;255;229;92m"; // RGB 颜色
        _cancellationTokenSource = new CancellationTokenSource();
        _isRunning = false;
    }

    public void Start()
    {
        // 检查是否是终端
        if (!Console.IsOutputRedirected && _task == null)
        {
            _isRunning = true;
            _task = Task.Run(Spin, _cancellationTokenSource.Token);
        }
    }

    public void Stop()
    {
        if (!_isRunning || _task == null) return;

        _cancellationTokenSource.Cancel();

        try
        {
            _task.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // 任务取消异常是预期的
        }
        finally
        {
            _task = null;
            _isRunning = false;

            // 清理控制台行
            try
            {
                Console.Write("\r\x1b[2K"); // 回车 + 清除整行
                Console.Out.Flush();
            }
            catch (Exception)
            {
                // 忽略清理时的异常
            }
        }
    }

    private void Spin()
    {
        var startTime = DateTime.Now;
        var index = 0;

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var elapsed = (DateTime.Now - startTime).TotalSeconds;
                var frame = _frames[index % _frames.Length];
                var styled = $"{_color}{frame} {_label} ({elapsed:F1}s)\x1b[0m";

                Console.Write("\r" + styled);
                Console.Out.Flush();

                index++;
                Thread.Sleep(80); // 0.08秒
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，正常退出
                break;
            }
            catch (Exception)
            {
                // 其他异常，退出循环
                break;
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cancellationTokenSource?.Dispose();
    }
}

#endregion
