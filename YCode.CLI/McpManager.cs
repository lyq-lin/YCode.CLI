using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace YCode.CLI
{
    internal class McpManager
    {
        private readonly string _workdir;
        private readonly List<AITool> _tools;

        public McpManager(string workdir)
        {
            _tools = [];

            _workdir = workdir;
        }

        public async Task<AITool[]> Regist(params Delegate[] methods)
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

                _tools.AddRange(files.Concat(bashes));
            }

            foreach (var method in methods)
            {
                _tools.Add(AIFunctionFactory.Create(method));
            }

            return [.. this.GetTools()];
        }

        public List<AITool> GetTools()
        {
            return _tools;
        }
    }
}
