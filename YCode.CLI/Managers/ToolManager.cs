using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;

namespace YCode.CLI
{
    [Inject]
    internal sealed class ToolManager
    {
        private readonly IReadOnlyList<IAgentTool> _tools;
        private readonly McpManager _mcp;

        public ToolManager(McpManager mcp, IEnumerable<IAgentTool> tools)
        {
            _mcp = mcp;
            _tools = tools.Where(t => t.IsEnable).ToList();
        }

        public IReadOnlyList<IAgentTool> GetAll() => _tools;

        public Task<AITool[]> Register(params (Delegate @delegate, string name, string description)[] overrides)
        {
            var methods = _tools
                .Select(t => (t.Handler, t.Name, t.Description))
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .ToDictionary(t => t.Name!, t => t, StringComparer.Ordinal);

            foreach (var method in overrides)
            {
                if (string.IsNullOrWhiteSpace(method.name))
                {
                    continue;
                }

                methods[method.name] = method;
            }

            return _mcp.Regist([.. methods.Values]);
        }

        public AITool[] GetTools() => _mcp.GetTools();

        public AITool[] GetTools(Func<AITool, bool> filter) => _mcp.GetTools(filter);
    }
}




