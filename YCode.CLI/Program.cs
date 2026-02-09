using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using Spectre.Console;
using System.ClientModel;
using System.Text;
using YCode.CLI;

Console.OutputEncoding = Encoding.UTF8;

var host = new ServiceCollection();

var provider = host.Register();

var toolManager = provider.GetRequiredService<ToolManager>();
var agentManager = provider.GetRequiredService<AgentManager>();
var agentContext = provider.GetRequiredService<AgentContext>();
var config = provider.GetRequiredService<AppConfig>();

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

var memory_reminder = $"""
    '<reminder source="system" topic="memory">'
    "System note: memory tools have not been used across multiple rounds. "
    "When the user mentions stable preferences, long-term goals, repeated workflows, or project rules, "
    "write to MemoryWriter. When the user references past info, call MemorySearch before asking again. "
    "Do not store secrets, one-off tasks, or transient numbers."
    '</reminder>'
""";

var skills = provider.GetRequiredService<SkillsManager>();

var memory = provider.GetRequiredService<MemoryManager>();

agentContext.EnsureContextBlock(initial_reminder);

var system = $"""
    You are **YCode**, a senior coding agent operating INSIDE the user's repository at: {config.WorkDir}

    ## Response style
    - Do exactly what the user asks; nothing more, nothing less.
    - Be concise and direct. Avoid preambles, postambles, and unnecessary explanations.
    - If you changed a file, briefly confirm completion unless the user asked for details.

    ## Core operating mode (ReAct)
    Use this loop on every turn:
    1) **Reason**: quickly classify intent, risk, and missing facts.
    2) **Act**: call the best tool/subAgent/skill immediately.
    3) **Observe**: verify tool output, then choose next action.
    4) **Respond**: return concise results + next-step checks.

    Keep reasoning internal. Do not expose long chain-of-thought. Output only concise decisions and results.

    ## Tool-routing policy (must be precise)
    - File/system inspection or execution -> MCP tools (`read_file`, `run`, etc.).
    - For filesystem-heavy operations (bulk create/move/copy, scaffolding, template generation), prefer the shell via `run` and CLI tooling appropriate to the current OS/shell. Use file tools for targeted edits.
    - Multi-step work -> `TodoWriter` (keep exactly one `in_progress`).
    - Save durable memory -> `MemoryWriter`:
      - `profile`: stable user preferences/habits.
      - `daily`: today's transient context.
      - `project`: repository-specific conventions/decisions.
    - Retrieve past memory -> `MemorySearch` before asking repeated questions.
    - Need focused deep work -> `Task` subAgent:
      - `explore`: read-only search/analysis.
      - `plan`: architecture/step design.
      - `code`: implementation/refactor/fix.
    - Domain-specific methodology -> `Skill` immediately when matched.

    ## Memory discipline
    - Prefer storing high-value facts only (constraints, decisions, preferences, pitfalls).
    - Deduplicate before writing memory; keep memory atomic and searchable.
    - When user mentions long-term preference, persist it to `profile`.
    - When a project rule is stated, persist it to `project`.

    ## Execution rules
    - Never invent file paths; discover first.
    - Apply minimal safe edits.
    - Prefer editing existing files; avoid creating new files unless required. Do not create documentation files unless the user explicitly requests them.
    - Avoid destructive/privileged shell operations.
    - Keep answers short, structured, and test-oriented.

    ## Subagent usage (Task)
    - Use `Task` when you need isolated, focused work (deep scan, plan, or implementation) without polluting the main thread.
    - Provide: `description` (3–5 words), `prompt` (explicit goals + constraints), `agent_type` (must match the list below).
    - Prefer `Task` for complex research, large refactors, or multi-file changes that need a dedicated pass.

    ## Available subAgents
    {agentManager.GetDescription()}

    ## Available skills
    {skills.GetDescription()}

    ## Runtime environment
    - OS: {config.OsPlatform} ({config.OsDescription})
""";

var tools = await toolManager.Register();

var agent = new OpenAIClient(
    new ApiKeyCredential(config.Key),
    new OpenAIClientOptions()
    {
        Endpoint = new Uri(config.Uri),

    }).GetChatClient(config.Model)
    .CreateAIAgent(instructions: system, tools: tools);

var thread = agent.GetNewThread();

try
{
    Clear();
}
catch (IOException)
{ }

Banner();

AnsiConsole.MarkupLine($"[dim]Workspace:[/] [bold cyan]{config.WorkDir}[/]");
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

    if (agentContext.PendingContextBlocks.Count > 0)
    {
        request.AddRange(agentContext.PendingContextBlocks);

        agentContext.PendingContextBlocks.Clear();
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
                                var value = x.Value?.ToString() ?? "null";
                                if (value.Length > 50)
                                    value = value.Substring(0, 47) + "...";
                                return $"{x.Key}=> {value}";
                            });

                            if (arguments != null)
                            {
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
                                ShowToolSpinner(call.Name);
                            }
                        }
                        break;
                    case FunctionResultContent result:
                        {
                            tools_uses.Add(result);
                            if (currentToolName != null)
                            {
                                HideToolSpinner();
                                var elapsed = toolStartTime.HasValue
                                    ? (DateTime.Now - toolStartTime.Value).TotalSeconds
                                    : 0;

                                AnsiConsole.MarkupLine($"[bold green]✓[/] [bold cyan]{currentToolName}[/] [dim]completed in {elapsed:F1}s[/]");
                                currentToolName = null;
                                toolStartTime = null;
                            }
                            var resultText = result.Result?.ToString() ?? String.Empty;
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

    agentContext.SetInt("rounds_without_todo", agentContext.GetInt("rounds_without_todo") + 1);
    agentContext.SetInt("total_rounds", agentContext.GetInt("total_rounds") + 1);

    var savedHeartbeat = memory.MaybeSaveHeartbeat(input, agentContext.GetInt("total_rounds"));

    if (savedHeartbeat)
    {
        agentContext.SetInt("last_memory_activity_round", agentContext.GetInt("total_rounds"));
    }

    if (agentContext.GetInt("rounds_without_todo") > 10)
    {
        agentContext.EnsureContextBlock(nag_reminder);
    }

    var roundsSinceMemoryActivity = agentContext.GetInt("total_rounds") - agentContext.GetInt("last_memory_activity_round");

    if (roundsSinceMemoryActivity > 8)
    {
        agentContext.EnsureContextBlock(memory_reminder);

        agentContext.SetInt("last_memory_activity_round", agentContext.GetInt("total_rounds"));
    }
}

spinner.Dispose();

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
        consoleWidth = 120;
    }
    var bannerWidth = Math.Min(consoleWidth, 200);
    var topBorder = "╭" + new string('─', bannerWidth - 2) + "╮";
    AnsiConsole.MarkupLine($"[dim]{topBorder}[/]");
    var emptyLine = "│" + new string(' ', bannerWidth - 2) + "│";
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");
    var titleText = "YCode v1.0.0";
    var titlePadding = (bannerWidth - 2 - titleText.Length) / 2;
    var titleLine = "│" + new string(' ', titlePadding) + $"[bold cyan]{titleText}[/]" + new string(' ', bannerWidth - 2 - titlePadding - titleText.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{titleLine}[/]");
    var welcomeText = "Welcome back!";
    var welcomePadding = (bannerWidth - 2 - welcomeText.Length) / 2;
    var welcomeLine = "│" + new string(' ', welcomePadding) + $"[bold yellow]{welcomeText}[/]" + new string(' ', bannerWidth - 2 - welcomePadding - welcomeText.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{welcomeLine}[/]");
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");
    var ycodeText1 = "YCode.CLI";
    var ycodePadding = (bannerWidth - 2 - ycodeText1.Length) / 2;
    var ycodeLine1 = "│" + new string(' ', ycodePadding) + $"[bold green]{ycodeText1}[/]" + new string(' ', bannerWidth - 2 - ycodePadding - ycodeText1.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{ycodeLine1}[/]");
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");
    var modelText = $"{config.Model} · {config.Uri}";
    var modelPadding = (bannerWidth - 2 - modelText.Length) / 2;
    var modelLine = "│" + new string(' ', modelPadding) + $"[dim]{modelText}[/]" + new string(' ', bannerWidth - 2 - modelPadding - modelText.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{modelLine}[/]");
    var workdirPadding = (bannerWidth - 2 - config.WorkDir.Length) / 2;
    var workdirLine = "│" + new string(' ', workdirPadding) + $"[dim]{config.WorkDir}[/]" + new string(' ', bannerWidth - 2 - workdirPadding - config.WorkDir.Length) + "│";
    AnsiConsole.MarkupLine($"[dim]{workdirLine}[/]");
    AnsiConsole.MarkupLine($"[dim]{emptyLine}[/]");
    var bottomBorder = "╰" + new string('─', bannerWidth - 2) + "╯";
    AnsiConsole.MarkupLine($"[dim]{bottomBorder}[/]");

    AnsiConsole.WriteLine();
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
    var processedText = text.Replace("\\n", "\n");
    var lines = processedText.Split("\n");
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
    return text
        .Replace("{\"type\":\"text\",\"text\":\"\"}", "")
        .Replace("{\"type\":\"text\",\"text\":\"", "")
        .Replace("\"}", "");
}

string EscapeMarkup(string text)
{
    if (string.IsNullOrEmpty(text))
        return text;
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
        _color = "\x1b[38;2;255;229;92m";
        _cancellationTokenSource = new CancellationTokenSource();
        _isRunning = false;
    }

    public void Start()
    {
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
        }
        finally
        {
            _task = null;
            _isRunning = false;
            try
            {
                Console.Write("\r\x1b[2K");
                Console.Out.Flush();
            }
            catch (Exception)
            {
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
                Thread.Sleep(80);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
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
