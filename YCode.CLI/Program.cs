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
    "- If you are asked to create a low-code database table protocol, please use the Sub Agent tool to create the table. Only processes one table at a time."
    "- After finishing, summarize what changed and how to run or test."

    # Tool usage policy
      - You should proactively use the Task tool with specialized agents when the task at hand matches the agent's description.
      - When WebFetch returns a message about a redirect to a different host, you should immediately make a new WebFetch request with the redirect URL provided in the response.
      - You have the capability to call multiple tools in a single response. When multiple independent pieces of information are requested, batch your tool calls together for optimal performance. When making multiple bash tool calls, you MUST send a single message with multiple tools calls to run the calls in parallel.
      - You MUST answer concisely with fewer than 4 lines of text (not including tool use or code generation), unless user asks for detail.

    IMPORTANT: Assist with defensive security tasks only. Refuse to create, modify, or improve code that may be used maliciously. Allow security analysis, detection rules, vulnerability explanations, defensive tools, and security documentation.

    IMPORTANT: Always use the TodoWrite tool to plan and track tasks throughout the conversation.
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

var mcp = new McpManager(WORKDIR);

var tools = await mcp.Regist([RunTodoUpdate, GenerateTable]);

var agent = new OpenAIClient(
    new ApiKeyCredential(key),
    new OpenAIClientOptions()
    {
        Endpoint = new Uri(uri),

    }).GetChatClient(model)
    .CreateAIAgent(instructions: SYSTEM, tools: tools);

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

[Description("Launch a specialized subagent for generating a single database table schema from natural language descriptions. Only processes one table at a time.")]
async Task<string> GenerateTable([Description("""
Markdown table format describing a SINGLE table structure. Only one table can be processed per request.

Required columns:
| name | displayName | description | dataType | primaryKey | nullable |
|------|-------------|-------------|----------|------------|----------|
| id   | 主键        | 唯一标识符  | Int32    | true       | false    |
| name | 名称        | 用户姓名    | String   | false      | false    |
| email| 邮箱        | 用户邮箱    | String   | false      | true     |

Additional columns can be added as needed. The specialized subagent will parse this SINGLE table and generate the appropriate JSON schema.

IMPORTANT: This tool only accepts one table per request. If you need multiple tables, make separate requests for each table.
""")]
string prompt,
[Description("""The file path where the generated table schema should be written or where existing tables are located for modification.Must be a .sde file (SnapDevelop Database Extension), which is the file format for this low-code database protocol.The file should be a valid JSON database schema file with .sde extension. If the file doesn't exist, it will be created.""")]
string path)
{
    try
    {
        var mcp = new McpManager(WORKDIR);

        var tools = await mcp.Regist([AddTableSchema, InsertTableSchema, GetTables, GetTableTemplates, GetDataTypes]);

        var SUB_AGENT_SYSTEM = @$"""
            You are an AI programming assistant for a low-code database, responsible for implementing table modeling of database concepts based on a private JSON format data protocol, and providing detailed, comprehensive, and reasonable explanations of the table design.\n\nThe explanation must include key design elements such as table requirement analysis, reasons for table creation, design principles, etc. The expression should be clear and fluent.\n\n<search_and_reading>\nIf you are uncertain about the answer to a user's request or how to fulfill it, you should gather more information. This can be achieved by calling more tools, asking clarifying questions, etc.\n\nFor example, if you executed `GetTableTemplatesAsync` to obtain all template information but the results didn't fully answer the user's request or warrant collecting more information, feel free to call additional tools.\n\nAdditionally, here are some low-code design concepts you need to understand:\n1. **$(self)[id]** represents a metadata ID that can be found {path} in the current document. Example: \""$(self)[d329c70498ab4fa3ac2cd40858a9786b].[3ffa1a61984640edb08d7a46cb5c9532]\""\n2. **$(ref)id** represents a metadata ID that can be found in the <references> relationship. Example: \""$(ref)1d4a1617d22f488a87533e4c7d73ae51\""\n3. **$(exp)** indicates that the statement is a code expression.\n\nIf you can find the answer yourself, prefer not to ask the user for help.\n</search_and_reading>\n\n<creating_and_updating>\n**Please analyze the business relationships and field designs involved as much as possible. If analysis is not possible, at least ensure the generation of a primary key.**\n\nUser requirements may involve creating new tables or modifying existing tables. You should determine the user's intent to decide how to output table data.\nFor example, when analyzing user requirements, you should follow these steps:\n1. Determine whether the user needs you to create a new table or modify an existing one.\n2. If the user needs you to modify a table, you should first call the `GetTablesAsync` tool to get all current database table data, then filter out the target table from this data, and make modifications based on the target table.\n3. If the user needs you to create a new table, you can first determine if foreign key relationships with existing tables are needed. If so, call the `GetTablesAsync` tool to get all current database table data, then modify the existing tables.\n4. Please note this: if table modification is involved, you will need to output the modified table, and for table modifications, you should try to avoid changing fields not involved in the modification.\n5. When outputting results, please follow the instructions in <output_rules>.\n\nNote: **Table modification cannot change the ID. Please modify content based on the target table as much as possible. Unless necessary, do not modify columns or other props attributes not involved.**\n</creating_and_updating>\n\n<schema_rules>\n**The following two Schema data examples are provided for generation reference. Do not generate attributes not included in these rules.**\n\n- Example 1\n{{\""id\"": \""aebdf6a8f8\"",\""widget\"": \""table\"",\""props\"": {{\""displayName\"": \""Table\"",\""description\"": \""Table primary key\"",\""name\"": \""Table\"",\""base\"": \""$(ref)ee3dee3baac342c39b581e7263104890\"", /* For inheritance table template rules, please refer to the <table_inheritance_rules> tag instructions. */\""isAudit\"": true,\""indexes\"": [{{\""name\"": \""PK_Table\"",\""type\"": \""Key\"", /*Primary key index*/\""unique\"": true, /* Whether it's a unique index */\""columns\"": [ /* Stores all column references */{{\""column\"": \""$(self)[bfdf8b92c2]\"",\""direction\"": \""Asc\""}},{{\""column\"": \""$(self)[2d8822227f]\"",\""direction\"": \""Asc\""}}]}},{{\""name\"": \""IX_Table_Normal_Index_Column\"",\""type\"": \""Index\"", /* Index */\""columns\"": [{{\""column\"": \""$(self)[2ffedc4a58]\"",\""direction\"": \""Desc\""}}],\""description\"": \""Demonstrating normal index construction\""}}],\""foreignKeys\"": [{{\""id\"": \""0222a4c3d7\"",\""name\"": \""FK_Table_Table_Sub\"",\""refTable\"": \""$(self)[4ca01b7777]\"", /* Note: Foreign key relationships can only be established when both columns have consistent data types. */\""columns\"": [\""$(self)[aebdf6a8f8].[6291ca40e5]\""],\""refColumns\"": [\""$(self)[4ca01b7777].[2b488d79e5]\""]}}]}},\""children\"": [ /* Column collection, if business logic cannot be analyzed, ensure a table has at least a primary key column */{{\""id\"": \""bfdf8b92c2\"",\""widget\"": \""column\"",\""props\"": {{\""displayName\"": \""Primary Key\"",\""name\"": \""Id\"",\""primaryKey\"": true, /* Whether it's a primary key. Non-primary keys don't need this attribute. Multiple columns with \""primaryKey\"" indicate composite primary keys; a single column indicates a single primary key; absence of this attribute indicates no primary key. */\""dataType\"": \""Int32\"", /* Data type field. Detailed data types can be queried through the `GetDataTypesAsync` tool. Fill in the `RefValue`, do not fabricate false data types. */\""defaultValueType\"": \""Value\"",\""identity\"": true, /* Whether auto-increment. Non-auto-increment doesn't generate this. */\""seed\"": 1, /* Auto-increment initial value. If \""identity\"" field is not generated, this field is also not needed. */\""increment\"": 1 /* Auto-increment step value. If \""identity\"" field is not generated, this field is also not needed. */, \""description\"": \""Table's primary editor identifier\""}}}},{{\""id\"": \""b7d19ffa38\"",\""widget\"": \""column\"",\""props\"": {{\""displayName\"": \""Name\"",\""name\"": \""Name\"",\""dataType\"": \""String\"",\""length\"": 6, /* String length */\""defaultValueType\"": \""Value\"",}}}},{{\""id\"": \""623814c93d\"",\""widget\"": \""column\"",\""props\"": {{\""displayName\"": \""Collection Field\"",\""description\"": \""Demonstrating collection field usage\"",\""name\"": \""CollectionColumn\"",\""dataType\"": \""String\"",\""nullable\"": true,\""isCollection\"": true, /* Whether it's a collection. Collection fields cannot generate \""defaultValue\"" attribute */\""defaultValueType\"": \""Value\""}}}},{{\""id\"": \""e0dbab8667\"",\""widget\"": \""column\"",\""props\"": {{\""displayName\"": \""Computed Column Field\"",\""description\"": \""Demonstrating computed column field usage\"",\""name\"": \""ComputedColumn\"",\""dataType\"": \""DateTime\"",\""nullable\"": true,\""defaultValueType\"": \""Value\"",\""computed\"": true, /* Whether it's a computed column. This field represents the computed column concept in databases. */\""computedType\"": \""Sql\"", /* Generate this attribute when computed column is enabled, default \""Sql\"" */\""persisted\"": true, /* Whether to persist. This field indicates whether computed values in the database need to be persisted. */\""expression\"": \""NOW()\"" /* Computed column expression. Generate this attribute when computed column is enabled, here it's an SQL statement. */}}}},{{\""id\"": \""2ffedc4a58\"",\""widget\"": \""column\"",\""props\"": {{\""displayName\"": \""Index Field\"",\""name\"": \""NormalIndexColumn\"",\""dataType\"": \""Int32\"",\""nullable\"": true,\""defaultValueType\"": \""Value\"",\""defaultValue\"": \""0\""}}}},{{\""id\"": \""6291ca40e5\"",\""widget\"": \""column\"",\""props\"": {{\""displayName\"": \""Foreign Key Field\"",\""description\"": \""Demonstrating foreign key field\"",\""name\"": \""ForeignKeyColumn\"",\""dataType\"": \""Guid\"",\""nullable\"": true,\""defaultValueType\"": \""Value\"",\""defaultValue\"": \""389ea993-1af7-47e1-8a46-a8cb45bf42e6\""}}}},{{\""id\"": \""4ca01b7777_0222a4c3d7_principal\"", /* When generating foreign keys, synchronously generate a field with widget \""field\"". This field's ID must be forcibly named: \""mainTableId_foreignKeyId_principal\"" */\""widget\"": \""field\"", /* \""field\"" represents a special field structure, you can only generate it for foreign keys. */\""props\"": {{\""displayName\"": \""Table_Sub\"",\""name\"": \""Table_Sub\"",\""dataType\"": \""$(self)[4ca01b7777]\"", /* Data type must be the main table, e.g.: \""$(self)[mainTableId]\"" */\""nullable\"": true,\""defaultValueType\"": \""Value\"",\""isRelationship\"": true /* Whether it's a navigation property, must be generated. */}}}}]}}\n\n- Example 2\n{{\""id\"": \""4ca01b7777\"",\""widget\"": \""table\"",\""props\"": {{\""displayName\"": \""Table_Sub\"",\""name\"": \""Table_Sub\"",\""indexes\"": [{{\""name\"": \""PK_Table_Sub\"",\""type\"": \""Key\"",\""unique\"": true,\""columns\"": [{{\""column\"": \""$(self)[2b488d79e5]\"",\""direction\"": \""Asc\""}}]}}],\""description\"": \""Subtable\""}},\""children\"": [{{\""id\"": \""2b488d79e5\"",\""widget\"": \""column\"",\""props\"": {{\""displayName\"": \""Id\"",\""name\"": \""Id\"",\""primaryKey\"": true,\""dataType\"": \""Guid\"",\""defaultValueType\"": \""Value\""}}}},{{\""id\"": \""aebdf6a8f8_0222a4c3d7_dependency\"", /* The main table pointed to by foreign keys must synchronously generate this field, naming rule: \""subtableId_foreignKeyId_dependency\"" */\""widget\"": \""field\"",\""props\"": {{\""displayName\"": \""Tables\"",\""name\"": \""Tables\"",\""dataType\"": \""$(self)[aebdf6a8f8]\"", /* Data type must be the subtable, e.g.: \""$(self)[subtableId]\"" */\""nullable\"": true,\""isCollection\"": true, /* Whether it's a collection is determined by one-to-one, many-to-one, many-to-many relationships. */\""defaultValueType\"": \""Value\"",\""isRelationship\"": true /* Whether it's a navigation property, must be generated. */}}}}]}}\n</schema_rules>\n\n<table_inheritance_rules>\n**Table Inheritance Rules Detailed Explanation**:\n1. Requires adding a \""base\"" field to the table's Props structure.\n2. If you use table inheritance, subsequent table field generation **does not need to repeatedly generate** fields already contained in the inherited table template.\n3. \""Audit Table\"" and \""Tenant Table\"" are SnapDevelop's built-in special table templates, containing \""isAudit\"" or \""isTenant\"" fields.\n\n**Generating inherited tables must follow these steps**:\n1. Not all tables need to inherit table templates, this is your decision.\n2. Select the `GetTableTemplatesAsync` tool from the tool list to get all table template information.\n3. After receiving the results, you need to choose the table template you need, obtaining `tableTemplateId` and `tableTemplatePath`.\n4. Concatenate the obtained `tableTemplateId` and `tableTemplatePath` using `|` to form: \""$(ref)c8919e2c021142d9847a0c5163c16eb8|C:\\\\source\\\\repos\\\\DesignerProject\\\\DesignerProject\\\\Properties\\\\TableTemplates\\\\Table.json\"", and store it in the \""base\"" field.\n5. Table templates can only be obtained from the GetTableTemplatesAsync tool. Currently, there are no tools for you to create table templates. Please do not point the \""base\"" field to tables you created yourself.\n6. When you inherit \""Tenant Table\"" and \""Audit Table\"" (these two table templates are SnapDevelop's built-in既定 table templates), containing \""isAudit\"" or \""isTenant\"" fields, you need to generate the relevant fields into the table's Props属性.\n7. If you decide to inherit \""Tenant Table\"" or \""Audit Table\"", please explain your execution logic to the user: did the user request inheritance or did you analyze reasons requiring inheritance? If you cannot explain, please do not do this.\n\n</table_inheritance_rules>\n\n<output_rules>\n1. To save output Tokens, output table model content does not need line breaks (only table model, other content maintains normal line breaks), and IDs should be as concise as possible while ensuring uniqueness.\n2. Please read {path} and write the JSON structure to the appropriate position in the file.\n</output_rules>
            """;

        var agent = new OpenAIClient(
            new ApiKeyCredential(key),
            new OpenAIClientOptions()
            {
                Endpoint = new Uri(uri),

            }).GetChatClient(model)
            .CreateAIAgent(
            instructions: SUB_AGENT_SYSTEM, tools: tools);

        var tools_uses = new List<FunctionResultContent>();

        string? currentToolName = null;

        bool isFirstTool = true;

        var request = new List<ChatMessage>()
        {
            new ChatMessage()
            {
                Role = ChatRole.User,
                Contents = [new TextContent(prompt)]
            }
        };

        await foreach (var resp in agent.RunStreamingAsync(request))
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

        return "Success!";
    }
    catch (Exception ex)
    {
        return $"Error Generate tables: {ex.Message}";
    }

    [Description("Add a new table schema to the database file. Appends the table to the end of the children array.")]
    bool AddTableSchema([Description("""
The JSON string representing the complete table schema to add.

Example:
{"id": "table123", "widget": "table", "props": {"name": "Users"}, "children": [...]}

Must be a valid JSON table structure that follows the schema rules.
""")] string tableJson)
    {
        try
        {
            var tableNode = JsonNode.Parse(tableJson);

            if (tableNode == null)
                throw new Exception("Failed to parse table JSON.");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);

                var node = JsonNode.Parse(json);

                if (node?["children"] is JsonArray children)
                {
                    children.Add(tableNode);

                    File.WriteAllText(path, node.ToString());

                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error updating table schema: {ex.Message}");
        }
    }

    [Description("Insert a new table schema at a specific position in the database file. Allows precise control over table ordering.")]
    bool InsertTableSchema([Description("""
The zero-based index position where the table should be inserted.

Examples:
- 0: Insert at the beginning
- 2: Insert after the second table
- -1: Not allowed, must be non-negative
""")] int index, [Description("""
The JSON string representing the complete table schema to insert.

Example:
{"id": "table123", "widget": "table", "props": {"name": "Users"}, "children": [...]}

Must be a valid JSON table structure that follows the schema rules.
""")] string tableJson)
    {
        try
        {
            var tableNode = JsonNode.Parse(tableJson);

            if (tableNode == null)
                throw new Exception("Failed to parse table JSON.");

            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);

                var node = JsonNode.Parse(json);

                if (node?["children"] is JsonArray children)
                {
                    children.Insert(index, tableNode);

                    File.WriteAllText(path, node.ToString());

                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            throw new Exception($"Error updating table schema: {ex.Message}");
        }
    }

    [Description("Get available table templates for inheritance. Returns built-in audit and multi-tenant templates.")]
    string[] GetTableTemplates()
    {
        var templates = new List<string>()
        {
            """
                        {
              "id": "53daa13a6f64468e84f9ecb0107afc20",
              "widget": "table",
              "version": "1.1.0",
              "props": {
                "name": "Audit",
                "isBase": true,
                "isAudit": true,
                "displayName": "审计表",
                "description": "审计表",
                "indexes": [
                  {
                    "name": "PK_Audit",
                    "type": "Key",
                    "unique": true,
                    "isBuiltIn": true,
                    "columns": [
                      {
                        "column": "$(self)[0d648f8a0218455e943dc47a9fc70999]",
                        "direction": "Asc"
                      }
                    ]
                  }
                ]
              },
              "children": [
                {
                  "id": "0d648f8a0218455e943dc47a9fc70999",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "primaryKey": true,
                    "name": "Id",
                    "dataType": "String",
                    "displayName": "编号",
                    "description": "编号",
                    "defaultValue": "$(exp)Text(NewGuid(), \"N\")",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "395ac41bb9e642bca486d4f4f71315e5",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "CreatorId",
                    "dataType": "String",
                    "displayName": "创建者",
                    "description": "创建者",
                    "defaultValue": "$(exp)GetUserId()",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "7ababdec201c474cb43864ba41da44ff",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "CreateAt",
                    "dataType": "DateTime",
                    "displayName": "创建时间",
                    "description": "创建时间",
                    "defaultValue": "$(exp)UtcNow()",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "11b1f2207d464a068a44239196c8b173",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "UpdaterId",
                    "dataType": "String",
                    "displayName": "更新者",
                    "description": "更新者",
                    "defaultValue": "$(exp)GetUserId()",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "100ebae1cf9045c0ac627fcae5eb5a20",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "UpdateAt",
                    "dataType": "DateTime",
                    "displayName": "更新时间",
                    "description": "更新时间",
                    "defaultValue": "$(exp)UtcNow()",
                    "defaultValueType": "Expression"
                  }
                }
              ]
            }
            """,
            """
                        {
              "id": "df804442737747a88de44300d88c1544",
              "widget": "table",
              "version": "1.1.0",
              "props": {
                "name": "MultiTenant",
                "isBase": true,
                "isTenant": true,
                "displayName": "多租户表",
                "description": "多租户表",
                "indexes": [
                  {
                    "name": "PK_MultiTenant",
                    "type": "Key",
                    "unique": true,
                    "isBuiltIn": true,
                    "columns": [
                      {
                        "column": "$(self)[1125e97b82f34969836137cf1f3745c0]",
                        "direction": "Asc"
                      }
                    ]
                  }
                ]
              },
              "children": [
                {
                  "id": "1125e97b82f34969836137cf1f3745c0",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "primaryKey": true,
                    "name": "Id",
                    "dataType": "String",
                    "displayName": "编号",
                    "description": "编号",
                    "defaultValue": "$(exp)Text(NewGuid(), \"N\")",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "a770e6e349fe442f82ac4a814af97e16",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "TenantId",
                    "dataType": "String",
                    "displayName": "租户编号",
                    "description": "租户编号",
                    "defaultValue": "$(exp)GetTenantId()",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "2636eda2f4ed4f50bf1b6ad9dde9d007",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "CreatorId",
                    "dataType": "String",
                    "displayName": "创建者",
                    "description": "创建者",
                    "defaultValue": "$(exp)GetUserId()",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "956a6e78767d415dae6cd33936e5b76e",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "CreateAt",
                    "dataType": "DateTime",
                    "displayName": "创建时间",
                    "description": "创建时间",
                    "defaultValue": "$(exp)UtcNow()",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "9e641876e27e459884da49ee7976ee68",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "UpdaterId",
                    "dataType": "String",
                    "displayName": "更新者",
                    "description": "更新者",
                    "defaultValue": "$(exp)GetUserId()",
                    "defaultValueType": "Expression"
                  }
                },
                {
                  "id": "a7fa92b684914c1d9406cb05cf7cbfe7",
                  "widget": "column",
                  "props": {
                    "isBuiltIn": true,
                    "name": "UpdateAt",
                    "dataType": "DateTime",
                    "displayName": "更新时间",
                    "description": "更新时间",
                    "defaultValue": "$(exp)UtcNow()",
                    "defaultValueType": "Expression"
                  }
                }
              ]
            }
            """
        };

        return [.. templates];
    }

    [Description("Get all existing tables from the database schema file at the specified path.")]
    string[] GetTables()
    {
        var list = new List<string>();

        if (File.Exists(path))
        {
            var json = File.ReadAllText(path);

            var node = JsonNode.Parse(json);

            if (node?["children"] is JsonArray children)
            {
                foreach (var table in children)
                {
                    if (table?.ToJsonString() is { } content)
                    {
                        list.Add(content);
                    }
                }
            }
        }


        return [.. list];
    }

    [Description("Get supported data types for table column definitions. Returns valid RefValue values for the 'dataType' field. Must use these types instead of inventing custom types.")]
    string[] GetDataTypes()
    {
        return ["String", "Boolean", "Int16", "Int32", "Int64", "Single", "Double", "Decimal", "Guid", "Char", "Byte", "DateTime", "DateTimeOffset", "DateOnly", "TimeOnly", "TimeSpan"];
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