# YCode CLI - .NET AI代理学习项目

> 基于 Microsoft Agents AI 框架构建的智能命令行工具，实现Subagent系统和技能加载机制

## 📖 项目简介

YCode CLI 是一个学习项目，展示了如何在 .NET 10.0 中使用 Microsoft Agents AI 框架构建功能完整的智能命令行工具。通过这个项目，你可以学习：

- **AI代理开发**：使用Microsoft Agents AI框架创建智能代理
- **工具集成**：通过MCP协议扩展代理能力
- **任务分解**：实现Subagent系统处理复杂任务
- **知识管理**：设计技能加载机制支持专业知识复用

## 🏗️ 核心功能

### Subagent 系统：任务分解与上下文管理

在实际开发中，复杂的任务（如"先探索代码库再重构系统"）会导致**上下文污染**——探索阶段的文件内容会占用大量上下文空间，影响后续任务执行。

YCode CLI 通过Subagent系统解决这个问题：

```
主代理 (任务协调)
    ↓
Subagent 系统
    ├── explore代理: 只读文件搜索和分析
    ├── plan代理:   策略设计和架构规划
    └── code代理:   代码实现和修改
```

**技术实现**：
- **上下文隔离**：每个Subagent有独立的对话历史，避免相互污染
- **工具权限控制**：不同代理类型有不同的工具访问权限
- **摘要返回**：子代理只返回任务结果，不传递中间过程

**代理类型定义**：
```csharp
var AGENTS = new Dictionary<string, JsonObject>()
{
    ["explore"] = new JsonObject  // 只读探索代理
    {
        ["description"] = "Read-only agent for exploring code, finding files, searching",
        ["tools"] = new JsonArray("bash", "read_file"),
        ["prompt"] = "You are an exploration agent. Search and analyze, but never modify files.",
    },
    ["code"] = new JsonObject     // 代码实现代理
    {
        ["description"] = "Full agent for implementing features and fixing bugs",
        ["tools"] = "*",
        ["prompt"] = "You are a coding agent. Implement the requested changes efficiently.",
    },
    ["plan"] = new JsonObject     // 规划设计代理
    {
        ["description"] = "Planning agent for designing implementation strategies",
        ["tools"] = new JsonArray("bash", "read_file"),
        ["prompt"] = "You are a planning agent. Analyze the codebase and output a numbered implementation plan.",
    }
};
```

### 技能加载机制：专业知识即文件

传统的AI代理需要通过重新训练模型来学习新专业知识，成本高昂。YCode CLI 采用更灵活的方式：**将专业知识存储在文件中，按需加载**。

**技能文件结构**：
```
skills/
├── code-review/          # 代码审查技能包
│   ├── SKILL.md         # YAML元数据 + Markdown专业知识
│   └── references/      # 参考文档和资源
└── ...                 # 其他技能包
```

**技能文件格式** (SKILL.md)：
```yaml
---
name: code-review
description: 代码审查专业知识和检查清单
---
# 代码审查指南

## 安全检查清单
- SQL注入防护
- XSS防护
- 输入验证...

## 性能优化建议
- 数据库查询优化
- 内存使用分析...
```

**技能管理器** (`SkillsManager.cs`)：
- 自动扫描 `skills/` 目录下的技能包
- 解析YAML元数据和Markdown内容
- 支持按需加载完整技能内容
- 管理技能相关资源文件

**平台定位说明**：
- **YCode CLI**：提供技能加载平台和代理管理系统
- **技能包**：独立的知识模块，用户可按需创建和添加
- **解耦设计**：CLI不绑定特定技能，可自由扩展

## 🔧 技术栈

- **运行时**：.NET 10.0
- **AI框架**：Microsoft Agents AI
- **工具协议**：MCP (Model Context Protocol)
- **UI库**：Spectre.Console (命令行美化)
- **数据格式**：YAML + Markdown (技能文件)

## 🚀 快速开始

### 环境要求
- .NET 10.0 SDK
- Node.js (用于MCP文件系统服务器)

### 安装方法
```bash
# 克隆项目
git clone https://github.com/your-username/YCode.CLI.git
cd YCode.CLI

# 构建项目
dotnet build

# 安装为全局工具
dotnet pack
dotnet tool install --global --add-source ./YCode.CLI/nupkg YCode.CLI
```

### 配置使用
```bash
# 设置API凭证 (示例使用DeepSeek)
export YCODE_AUTH_TOKEN=your_api_key
export YCODE_API_BASE_URI=https://api.deepseek.com
export YCODE_MODEL=deepseek-chat

# 可选：配置Context7 MCP工具 (Upstash Context7 API)
export YCODE_CONTEXT7=your_context7_api_key

# 启动交互界面
ycode
```

### 基本使用示例
```
> ycode
> user: 查找项目中所有的API端点

⏺ Task(description=> "API端点探索", prompt=> "查找所有API端点", subagent_type=> "explore")
    ⎿[explore代理] 正在搜索...
    ⎿找到12个端点分布在3个文件中

> user: 修复LoginController的空指针问题

⏺ Task(description=> "修复空指针", prompt=> "修复LoginController第45行的空指针", subagent_type=> "code")
    ⎿[code代理] 分析代码...
    ⎿添加空值检查逻辑

✓ 问题已修复
```

## 🛠️ 扩展开发

### 添加新技能
```bash
# 1. 创建技能目录
mkdir -p skills/my-skill

# 2. 创建SKILL.md文件
cat > skills/my-skill/SKILL.md << 'EOF'
---
name: my-skill
description: 我的专业技能描述
---
# 详细专业知识

这里编写具体的专业知识内容...
EOF

# 3. 添加辅助资源 (可选)
mkdir skills/my-skill/references
echo "参考文档..." > skills/my-skill/references/guide.md
```

YCode CLI会自动发现并加载新的技能包。

### 自定义代理类型
修改 `Program.cs` 中的 `AGENTS` 字典即可添加新的代理类型：

```csharp
// 添加新的代理类型
var AGENTS = new Dictionary<string, JsonObject>()
{
    // 现有代理类型...
    ["review"] = new JsonObject  // 新增代码审查代理
    {
        ["description"] = "Specialized agent for code review",
        ["tools"] = new JsonArray("bash", "read_file"),
        ["prompt"] = "You are a code review expert focusing on security and performance.",
    }
};
```

## 📚 项目结构

```
YCode.CLI/
├── Program.cs              # 主程序入口，代理管理和交互循环
├── SkillsManager.cs        # 技能管理器，加载和解析技能文件
├── TodoManager.cs          # 任务状态管理器，展示状态机设计
├── McpManager.cs           # MCP服务器生命周期管理
├── YCode.CLI.csproj        # 项目配置文件
└── skills/                 # 示例技能包目录 (可选)
    ├── code-review/        # 代码审查技能示例
    ├── mcp-builder/        # MCP开发技能示例
    └── pdf/               # PDF处理技能示例
```

## 🎓 学习价值

这个项目适合以下学习目标：

### 1. Microsoft Agents AI 框架实践
- AI代理的创建和管理
- 工具调用和结果处理
- 流式响应和实时交互

### 2. 架构模式学习
- Subagent系统设计：复杂任务的分解策略
- 上下文管理：避免对话历史污染的实践
- 技能加载机制：专业知识的外化和管理

### 3. 实际工程问题解决
- 工具权限控制：不同类型代理的安全访问
- 状态机设计：TodoManager的约束验证逻辑
- 协议集成：MCP标准化工具生态

### 4. 可扩展性设计
- 插件式架构：技能即插件的实现
- 配置驱动：代理类型的灵活定义
- 资源管理：技能相关文件的自动发现

## 📖 实现细节

### Subagent 工作流程
```csharp
// 1. 创建独立上下文的子代理
var sub_system = $"""
    You are a {agentType} subagent.
    {config["prompt"]}
    Complete the task and return a concise summary.
    """;

// 2. 过滤工具集
var sub_tools = GetToolsForAgent(agentType);

// 3. 独立对话历史
var sub_messages = new List<ChatMessage>()
{
    new ChatMessage() { Role = ChatRole.User, Contents = [new TextContent(prompt)] }
};

// 4. 执行并返回摘要
var sub_agent = client.CreateAIAgent(sub_system, tools: sub_tools);
```

### 技能文件解析
```csharp
// 解析YAML frontmatter + Markdown内容
var match = Regex.Match(content, @"^---\s*\n(.*?)\n---\s*\n(.*)$", RegexOptions.Singleline);
if (match.Success)
{
    // 解析YAML元数据
    var yamlObject = _builder.Deserialize<Dictionary<string, object>>(match.Groups[1].Value);
    // 提取Markdown内容
    var markdownContent = match.Groups[2].Value;
}
```

## 🔄 开发演进

根据git历史，项目的主要功能演进如下：

1. **基础交互**：Console UI实现，基础代理系统
2. **工具扩展**：集成MCP文件系统工具
3. **任务分解**：实现Subagent系统，支持explore/code/plan代理
4. **知识管理**：添加技能加载机制，支持专业知识复用

每个阶段都基于实际需求逐步添加功能，体现了**渐进式复杂度**的设计理念。

## 🤝 贡献与反馈

这是一个学习项目，欢迎：

- **提出问题**：报告bug或建议改进
- **分享经验**：交流Microsoft Agents AI使用心得
- **扩展功能**：基于项目实现更多代理类型或技能
- **改进文档**：帮助完善使用说明和示例

## 📄 许可证

MIT License

## 🙏 参考资源

- [Microsoft Agents AI 文档](https://learn.microsoft.com/en-us/dotnet/agents/)
- [MCP 协议规范](https://spec.modelcontextprotocol.io/)
- [Kode 项目](https://github.com/shareAI-lab/Kode) (Python实现的参考)