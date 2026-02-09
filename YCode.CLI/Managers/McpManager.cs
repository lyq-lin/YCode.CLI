using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace YCode.CLI
{
    [Inject]
    internal class McpManager
    {
        private readonly string _workdir;
        private readonly Dictionary<string, List<AITool>> _tools;

        public McpManager(string workdir)
        {
            _tools = [];

            _workdir = workdir;
        }

        public McpManager(AppConfig config) : this(config.WorkDir)
        {
        }

        public async Task<AITool[]> Regist(params (Delegate @delegate, string? name, string? description)[] methods)
        {
            if (_tools.Count == 0)
            {
                var bash = new StdioClientTransport(new StdioClientTransportOptions()
                {
                    Name = "bash",
                    Command = "npx",
                    Arguments = ["bash-mcp"],
                });

                var bashClient = await McpClient.CreateAsync(bash);

                var bashes = await bashClient.ListToolsAsync();

                if (bashes.Count > 0)
                {
                    _tools.TryAdd("bash", [.. bashes]);
                }

                var context7Key = Environment.GetEnvironmentVariable("YCODE_CONTEXT7");

                if (!String.IsNullOrWhiteSpace(context7Key))
                {
                    var context7 = new StdioClientTransport(new StdioClientTransportOptions()
                    {
                        Name = "context7",
                        Command = "npx",
                        Arguments = ["-y", "@upstash/context7-mcp", "--api-key", context7Key],
                    });

                    var context7Client = await McpClient.CreateAsync(context7);

                    var context7Tools = await context7Client.ListToolsAsync();

                    _tools.TryAdd("context7", [.. context7Tools]);
                }
            }

            foreach (var method in methods)
            {
                if (_tools.TryGetValue("bulletin", out var tools))
                {
                    tools.Add(AIFunctionFactory.Create(method.@delegate, method.name, method.description));
                }
                else
                {
                    _tools.Add("bulletin", [AIFunctionFactory.Create(method.@delegate, method.name, method.description)]);
                }
            }

            return [.. this.GetTools()];
        }

        public AITool[] GetTools()
        {
            return [.. _tools.SelectMany(x => x.Value)];
        }

        public AITool[] GetTools(Func<AITool, bool> filter)
        {
            return [.. _tools.SelectMany(x => x.Value).Where(filter)];
        }
    }
}




