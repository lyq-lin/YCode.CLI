# YCode CLI - .NET版代码助手

> 参考 [Kode](https://github.com/shareAI-lab/Kode) 实现的 .NET 版本，学习如何使用 Microsoft Agents AI 框架构建智能命令行工具

## 项目概述

这是一个学习项目，展示了如何使用 .NET 10.0 和 Microsoft Agents AI 框架构建一个智能命令行工具。核心功能包括任务管理、文件操作和交互式命令行界面。

## 核心实现

### TodoManager 系统

`TodoManager.cs` 实现了任务状态管理：

```csharp
// 任务状态定义
public static readonly string[] TODO_STATUSES = { "pending", "in_progress", "completed" };

// 核心更新方法
public string Update(List<Dictionary<string, object>> inputItems)
{
    // 验证和清理输入
    // 状态约束检查
    // 更新内部状态
    return Render();
}
```

**关键约束**：
- 最多支持20个任务项
- 同一时间只能有一个 `in_progress` 任务
- 自动状态跟踪和验证

### 主程序流程

`Program.cs` 中的核心流程：

1. **初始化阶段**
   - 设置工作目录和环境变量
   - 启动MCP文件系统服务器
   - 创建AI代理和工具列表

2. **交互循环**
   ```csharp
   while (true)
   {
       Console.Write("\n> user:");
       var input = Console.ReadLine();

       // 处理用户输入
       await foreach (var resp in agent.RunStreamingAsync(request, thread))
       {
           // 流式处理响应
       }
   }
   ```

3. **工具调用处理**
   - `FunctionCallContent`: 显示工具调用信息
   - `FunctionResultContent`: 显示执行结果
   - `TextContent`: 显示AI回复

### 工具集成

项目集成了两种类型的工具：

1. **MCP文件系统工具** - 通过 `npx @modelcontextprotocol/server-filesystem` 提供文件操作能力
2. **自定义Todo工具** - 通过 `RunTodoUpdate` 函数提供任务管理

## 安装和使用

### 环境要求
- .NET 10.0 SDK
- Node.js (用于MCP文件系统服务器)

### 方法一：从源代码安装

1. **克隆项目**
   ```bash
   git clone https://github.com/your-username/YCode.CLI.git
   cd YCode.CLI
   ```

2. **构建和打包**
   ```bash
   dotnet build
   dotnet pack
   ```

3. **安装为全局工具**
   ```bash
   dotnet tool install --global --add-source ./YCode.CLI/nupkg YCode.CLI
   ```

### 方法二：从NuGet安装（如果发布到NuGet）

```bash
dotnet tool install --global YCode.CLI
```

### 验证安装

安装完成后，验证工具是否正确安装：

```bash
# 查看已安装的工具
dotnet tool list --global

# 应该能看到类似输出：
# Package Id      Version      Commands
# ------------------------------------
# ycode.cli       1.0.0        ycode

# 测试运行
ycode --help
```

### 更新和卸载

```bash
# 更新工具
dotnet tool update --global YCode.CLI

# 卸载工具
dotnet tool uninstall --global YCode.CLI
```

### 使用方法

1. **设置环境变量**
   ```bash
   # Windows
   set ANTHROPIC_AUTH_TOKEN=your_api_key
   set ANTHROPIC_MODEL=your_model_name

   # Linux/macOS
   export ANTHROPIC_AUTH_TOKEN=your_api_key
   export ANTHROPIC_MODEL=your_model_name
   ```

2. **启动YCode**
   ```bash
   ycode
   ```

3. **在交互界面中使用**
   ```
   > user: 帮我创建一个控制台应用
   ```

### 使用示例

```
> user: 帮我创建一个简单的控制台应用

⏺ Write(file_path=> Program.cs, content=> ...)
    ⎿File created successfully

⏺ RunTodoUpdate(items=> [{"content": "创建Program.cs", "activeForm": "创建Program.cs", "status": "completed"}])
    ⎿Todo list updated

✓ 创建Program.cs
```

## 项目结构

```
YCode.CLI/
├── Program.cs          # 主程序入口和交互循环
├── TodoManager.cs      # 任务状态管理
└── YCode.CLI.csproj    # 项目配置
```

## 学习重点

1. **Microsoft Agents AI 框架使用** - 如何创建AI代理和工具
2. **MCP协议集成** - 如何通过标准协议扩展工具能力
3. **流式响应处理** - 实时显示AI回复和工具调用
4. **状态管理设计** - Todo系统的约束和验证逻辑

## 参考

- [Kode 项目](https://github.com/shareAI-lab/Kode) - 原始Python实现
- [Microsoft Agents AI 文档](https://learn.microsoft.com/en-us/dotnet/agents/)
- [MCP 协议](https://spec.modelcontextprotocol.io/)