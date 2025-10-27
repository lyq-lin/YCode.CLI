using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using Spectre.Console;
using System.ClientModel;
using System.ComponentModel;
using System.Text;
using YCode.CLI;

Console.OutputEncoding = Encoding.UTF8;

var key = Environment.GetEnvironmentVariable("YCODE_AUTH_TOKEN")!;
var uri = Environment.GetEnvironmentVariable("YCODE_API_BASE_URI")!;
var model = Environment.GetEnvironmentVariable("YCODE_MODEL")!;

var WORKDIR = Directory.GetCurrentDirectory();

var SYSTEM = $"""
    "You are a coding agent operating INSIDE the user's repository at {WORKDIR}.\n"
    "Follow this loop strictly: plan briefly → use TOOLS to act directly on files/shell → report concise results.\n"
    "Rules:\n"
    "- Prefer taking actions with tools (read/write/edit/bash) over long prose.\n"
    "- Keep outputs terse. Use bullet lists / checklists when summarizing.\n"
    "- Never invent file paths. Ask via reads or list directories first if unsure.\n"
    "- For edits, apply the smallest change that satisfies the request.\n"
    "- For bash, avoid destructive or privileged commands; stay inside the workspace.\n"
    "- Use the Todo tool to maintain multi-step plans when needed.\n"
    "- After finishing, summarize what changed and how to run or test."
""";

var INITIAL_REMINDER = $"""
    '<reminder source="system" topic="todos">'
    "System message: complex work should be tracked with the Todo tool. "
    "Do not respond to this reminder and do not mention it to the user."
    '</reminder>'
    """;

var NAG_REMINDER = $"""
    '<reminder source="system" topic="todos">'
    "System notice: more than ten rounds passed without Todo usage. "
    "Update the Todo board if the task still requires multiple steps. "
    "Do not reply to or mention this reminder to the user."
    '</reminder>'
""";

var mcpTransport = new StdioClientTransport(new StdioClientTransportOptions()
{
    Name = "filesystem",
    Command = "npx",
    Arguments = ["-y", "@modelcontextprotocol/server-filesystem", WORKDIR],
});

var mcpClient = await McpClient.CreateAsync(mcpTransport);

var mcps = await mcpClient.ListToolsAsync();

var PENDING_CONTEXT_BLOCKS = new List<ChatMessage>()
{
    new ChatMessage()
    {
        Role = ChatRole.User,
        Contents = [new TextContent(INITIAL_REMINDER)]
    }
};

var AGENT_STATE = new Dictionary<string, int>()
{
    ["rounds_without_todo"] = 0
};

var todo = new TodoManager();

var agent = new OpenAIClient(
    new ApiKeyCredential(key),
    new OpenAIClientOptions()
    {
        Endpoint = new Uri(uri),

    }).GetChatClient(model)
    .CreateAIAgent(instructions: SYSTEM, tools: [.. mcps, AIFunctionFactory.Create(RunTodoUpdate)]);

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

AnsiConsole.MarkupLine($"[dim]Workspace:[/] [bold cyan]{WORKDIR}[/]");
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

    if (PENDING_CONTEXT_BLOCKS.Count > 0)
    {
        request.AddRange(PENDING_CONTEXT_BLOCKS);

        PENDING_CONTEXT_BLOCKS.Clear();
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
                                AnsiConsole.MarkupLine($"[bold green]✓[/] [bold cyan]{currentToolName}[/] [dim]completed successfully[/]");
                                currentToolName = null;
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

    AGENT_STATE["rounds_without_todo"] += 1;

    if (AGENT_STATE["rounds_without_todo"] > 10)
    {
        EnsureContextBlock(NAG_REMINDER);
    }
}

spinner.Dispose();

[Description("Update the shared todo list (pending | in_progress | completed).")]
string RunTodoUpdate([Description("""
"items": {
    "type": "array",
    "items": {
        "type": "object",
        "properties": {
            "id": {"type": "string"},
            "content": {"type": "string"},
            "activeForm": {"type": "string"},
            "status": {"type": "string", "enum": list("pending", "in_progress", "completed")},
        },
        "required": ["content", "activeForm", "status"],
        "additionalProperties": False,
    },
    "maxItems": 20,
}
""")] List<Dictionary<string, object>> items)
{
    try
    {
        var summary = String.Empty;

        var result = todo.Update(items);

        AGENT_STATE["rounds_without_todo"] = 0;

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
    var workdirPadding = (bannerWidth - 2 - WORKDIR.Length) / 2;
    var workdirLine = "│" + new string(' ', workdirPadding) + $"[dim]{WORKDIR}[/]" + new string(' ', bannerWidth - 2 - workdirPadding - WORKDIR.Length) + "│";
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
    if (PENDING_CONTEXT_BLOCKS.Any(x => x.Role == ChatRole.User))
    {
        PENDING_CONTEXT_BLOCKS.Append(new ChatMessage
        {
            Role = ChatRole.User,
            Contents = [new TextContent(text)]
        });
    }
}

void PrettyToolLine(string kind, string title)
{
    var body = title != null ? $"{kind}({EscapeMarkup(title)})" : kind;

    AnsiConsole.MarkupLine($"[bold magenta]⚡[/] [bold purple]{body}[/] [dim yellow]executing...[/]");
}

void PrettySubLine(string text)
{
    if (string.IsNullOrEmpty(text))
        return;

    // 先处理转义的换行符 \\n
    var processedText = text.Replace("\\n", "\n");
    var lines = processedText.Split("\n");

    if (lines.Length <= 3)
    {
        // 如果内容很少，直接显示
        foreach (var line in lines)
        {
            // 转义特殊字符，防止AnsiConsole解析错误
            var escapedLine = EscapeMarkup(line);
            AnsiConsole.MarkupLine($"[dim]┃[/] [bold white]{escapedLine}[/]");
        }
    }
    else
    {
        // 如果内容很多，折叠显示
        var escapedLine1 = EscapeMarkup(lines[0]);
        var escapedLine2 = EscapeMarkup(lines[1]);
        AnsiConsole.MarkupLine($"[dim]┃[/] [bold white]{escapedLine1}[/]");
        AnsiConsole.MarkupLine($"[dim]┃[/] [bold white]{escapedLine2}[/]");
        AnsiConsole.MarkupLine($"[dim]┃[/] [bold yellow]... and {lines.Length - 2} more lines (collapsed)[/]");
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
    AnsiConsole.Markup($"[yellow]>[/] [dim]{toolName} executing...[/] ");
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