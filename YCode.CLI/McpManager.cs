using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace YCode.CLI
{
    internal class McpManager
    {
        private readonly string _workdir;
        private readonly Dictionary<string, List<AITool>> _tools;

        public McpManager(string workdir)
        {
            _tools = [];

            _workdir = workdir;
        }

        public async Task<AITool[]> Regist(params (Delegate @delegate, string? name, string? description)[] methods)
        {
            if (_tools.Count == 0)
            {
                var filesystem = new StdioClientTransport(new StdioClientTransportOptions()
                {
                    Name = "filesystem",
                    Command = "npx",
                    Arguments = ["-y", "@modelcontextprotocol/server-filesystem", _workdir],
                });

                var bash = new StdioClientTransport(new StdioClientTransportOptions()
                {
                    Name = "bash",
                    Command = "npx",
                    Arguments = ["bash-mcp"],
                });

                var fileClient = await McpClient.CreateAsync(filesystem);

                var bashClient = await McpClient.CreateAsync(bash);

                var files = await fileClient.ListToolsAsync();

                var bashes = await bashClient.ListToolsAsync();

                if (files.Count > 0)
                {
                    _tools.TryAdd("file-system", [.. files]);
                }

                if (bashes.Count > 0)
                {
                    _tools.TryAdd("bash", [.. bashes]);
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
