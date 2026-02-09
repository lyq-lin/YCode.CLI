using System;

namespace YCode.CLI
{
    //[Inject<IAgentTool>]
    internal sealed class ReadFileTool : IAgentTool
    {
        public ReadFileTool()
        {
            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Read a file from the workspace.",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string", "description": "Relative path to the file." }
                        },
                        "required": ["path"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "read_file";
        public string Description { get; }
        public bool IsReadOnly => true;
        public bool IsEnable => false;
        public Delegate Handler => this.Run;

        private string Run()
        {
            throw new NotImplementedException("read_file handler is not implemented yet.");
        }
    }

    //[Inject<IAgentTool>]
    internal sealed class WriteFileTool : IAgentTool
    {
        public WriteFileTool()
        {
            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Write content to a file (create or overwrite).",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string", "description": "Relative path to the file." },
                            "content": { "type": "string", "description": "Full file contents to write." },
                            "overwrite": { "type": "boolean", "description": "Whether to overwrite if the file exists.", "default": true }
                        },
                        "required": ["path", "content"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "write_file";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => false;
        public Delegate Handler => this.Run;

        private string Run()
        {
            throw new NotImplementedException("write_file handler is not implemented yet.");
        }
    }

    //[Inject<IAgentTool>]
    internal sealed class EditFileTool : IAgentTool
    {
        public EditFileTool()
        {
            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Edit a file using search-and-replace operations.",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string", "description": "Relative path to the file." },
                            "find": { "type": "string", "description": "Text to find." },
                            "replace": { "type": "string", "description": "Replacement text." },
                            "all": { "type": "boolean", "description": "Replace all occurrences when true.", "default": true }
                        },
                        "required": ["path", "find", "replace"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "edit_file";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => false;
        public Delegate Handler => this.Run;

        private string Run()
        {
            throw new NotImplementedException("edit_file handler is not implemented yet.");
        }
    }

    //[Inject<IAgentTool>]
    internal sealed class DeleteFileTool : IAgentTool
    {
        public DeleteFileTool()
        {
            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Delete a file from the workspace.",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string", "description": "Relative path to the file." }
                        },
                        "required": ["path"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "delete_file";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => false;
        public Delegate Handler => this.Run;

        private string Run()
        {
            throw new NotImplementedException("delete_file handler is not implemented yet.");
        }
    }

    //[Inject<IAgentTool>]
    internal sealed class ListDirectoryTool : IAgentTool
    {
        public ListDirectoryTool()
        {
            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "List entries in a directory.",
                    "arguments": {
                        "type": "object",
                        "properties": {
                            "path": { "type": "string", "description": "Relative path to the directory." },
                            "recursive": { "type": "boolean", "description": "Whether to list recursively.", "default": false }
                        },
                        "required": ["path"],
                        "additionalProperties": false
                    }
                }
                """;
        }

        public string Name => "list_directory";
        public string Description { get; }
        public bool IsReadOnly => true;
        public bool IsEnable => false;
        public Delegate Handler => this.Run;

        private string Run()
        {
            throw new NotImplementedException("list_directory handler is not implemented yet.");
        }
    }
}



