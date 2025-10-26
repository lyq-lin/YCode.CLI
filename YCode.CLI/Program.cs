using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using OpenAI;
using System.ClientModel;
using System.ComponentModel;
using YCode.CLI;

var key = Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN")!;
var uri = "https://api.deepseek.com";
var model = Environment.GetEnvironmentVariable("ANTHROPIC_MODEL")!;

var WORKDIR = Directory.GetCurrentDirectory();

var RESET = "\x1b[0m";
var ACCENT_COLOR = "\x1b[38;2;150;140;255m";

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

Clear();

Banner();

Console.WriteLine($"Workspace: {WORKDIR}");
Console.WriteLine("Type \"exit\" or \"quit\" to leave.");

while (true)
{
    Console.Write("\n> user:");

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

        await foreach (var resp in agent.RunStreamingAsync(request, thread))
        {
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
                            var arguments = call.Arguments?.Select(x => $"{x.Key}=> {x.Value}");

                            if (arguments != null)
                            {
                                PrettyToolLine(call.Name, String.Join("\n", arguments) ?? String.Empty);
                            }
                        }
                        break;
                    case FunctionResultContent result:
                        {
                            tools_uses.Add(result);

                            PrettySubLine(result.Result?.ToString() ?? String.Empty);
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

void Clear()
{
    Console.Clear();
}

void Banner()
{
    Console.WriteLine("Welcome to YCode!");
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
    var body = title != null ? $"{kind}({title})…" : kind;

    var glow = $"{ACCENT_COLOR}\x1b[1m";

    Console.WriteLine($"{glow}⏺ {body}{RESET}");
}

void PrettySubLine(string text)
{
    var lines = text.Split("\n");

    foreach (var line in lines)
    {
        Console.WriteLine($"    ⎿{line}");
    }
}

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

