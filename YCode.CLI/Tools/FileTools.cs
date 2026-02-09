using System;
using System.Text;

namespace YCode.CLI
{
    [Inject<IAgentTool>]
    internal sealed class ReadFileTool : IAgentTool
    {
        private readonly AppConfig _config;
        private readonly FileReadTracker _tracker;
        private readonly int _maxLine = 2000;
        private readonly int _maxLineLength = 2000;
        private readonly double _maxTokens = 0.25 * 1024 * 1024;

        public ReadFileTool(AppConfig config, FileReadTracker tracker)
        {
            _config = config;
            _tracker = tracker;
            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Read a local text file. Defaults to the first {{_maxLine}} lines, each line truncated to {{_maxLineLength}} chars, with a total output cap of {{_maxTokens / 1024}} KB. Use offset/limit for large files.",
                    "arguments": {
                        "path": { "type": "string", "description": "File path (absolute or relative to workDir)." },
                        "offset": { "type": "int", "description": "Start line offset.", "default": 0 },
                        "limit": { "type": "int", "description": "Maximum number of lines to read.", "nullable": true, "default": null }
                    }
                }
                """;
        }

        public string Name => "read_file";
        public string Description { get; }
        public bool IsReadOnly => true;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string path, int offset = 0, int? limit = null)
        {
            path = FileToolPaths.NormalizePath(path, _config);

            var (content, lineCount, total) = Read(path, offset, limit);

            var truncatedLines = content
                .Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => line.Length > _maxLineLength ? line[.._maxLineLength] : line);

            var result = String.Join("\n", truncatedLines);

            if (Encoding.UTF8.GetByteCount(result) is { } count && count > _maxTokens)
            {
                throw new Exception($"File output is too large ({count / 1024}KB). Limit is {_maxTokens / 1024}KB. Use offset/limit to read a smaller range.");
            }

            _tracker.MarkRead(path);

            return result;
        }

        private (string content, int lineCount, int total) Read(string path, int offset = 0, int? maxLines = null)
        {
            var lines = File.ReadAllLines(path, Encoding.UTF8);

            var to = maxLines.HasValue && lines.Length - offset > maxLines.Value
                ? lines.Skip(offset).Take(maxLines.Value)
                : lines.Skip(offset).Take(_maxLine);

            return (String.Join('\n', to), to.Count(), lines.Length);
        }
    }

    [Inject<IAgentTool>]
    internal sealed class WriteFileTool : IAgentTool
    {
        private readonly AppConfig _config;
        private readonly FileReadTracker _tracker;
        private readonly int _maxBytes = 256 * 1024;

        public WriteFileTool(AppConfig config, FileReadTracker tracker)
        {
            _config = config;

            _tracker = tracker;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Write content to a local file. Rules: If the file exists, you must read it first with read_file. Prefer editing existing files; do not create new files unless explicitly requested. Do not create documentation files (*.md) unless explicitly requested. Do not use emojis unless the user asks. Large writes must use mode=chunked with offset.",
                    "arguments": {
                        "path": { "type": "string", "description": "File path (absolute or relative to workDir)." },
                        "content": { "type": "string", "description": "Full file contents to write." },
                        "mode": { "type": "string", "enum": ["overwrite", "append", "chunked"], "description": "Write mode.", "default": "overwrite" },
                        "offset": { "type": "integer", "description": "Required for chunked writes; optional for append." }
                    }
                }
                """;
        }

        public string Name => "write_file";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string path, string content, string mode = "overwrite", long? offset = null)
        {
            path = FileToolPaths.NormalizePath(path, _config);

            _tracker.AssertFresh(path);

            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var bytes = Encoding.UTF8.GetBytes(content ?? string.Empty);

            if (bytes.Length > _maxBytes)
            {
                throw new Exception($"Content is too large ({bytes.Length / 1024}KB). Limit is {_maxBytes / 1024}KB. Use mode=chunked with offset.");
            }

            mode = (mode ?? "overwrite").Trim().ToLowerInvariant();

            switch (mode)
            {
                case "overwrite":
                    File.WriteAllBytes(path, bytes);
                    _tracker.ResetWriteOffset(path, bytes.LongLength);
                    return "File written.";
                case "append":
                    if (!offset.HasValue)
                    {
                        offset = File.Exists(path) ? new FileInfo(path).Length : 0;
                    }
                    _tracker.AssertWriteOffset(path, offset.Value);
                    using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                    {
                        stream.Seek(offset.Value, SeekOrigin.Begin);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    _tracker.AdvanceWriteOffset(path, bytes.LongLength);
                    return "File appended.";
                case "chunked":
                    if (!offset.HasValue)
                    {
                        throw new Exception("offset is required for chunked writes.");
                    }
                    _tracker.AssertWriteOffset(path, offset.Value);
                    using (var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                    {
                        stream.Seek(offset.Value, SeekOrigin.Begin);
                        stream.Write(bytes, 0, bytes.Length);
                    }
                    _tracker.AdvanceWriteOffset(path, bytes.LongLength);
                    return "Chunk written.";
                default:
                    throw new Exception("Invalid mode. Use overwrite, append, or chunked.");
            }
        }

    }

    [Inject<IAgentTool>]
    internal sealed class EditFileTool : IAgentTool
    {
        private readonly AppConfig _config;
        private readonly FileReadTracker _tracker;
        private readonly long _maxFileBytes = 5 * 1024 * 1024;

        public EditFileTool(AppConfig config, FileReadTracker tracker)
        {
            _config = config;

            _tracker = tracker;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Performs exact string replacements in files. Rules: You must use read_file at least once before editing. When editing text from read output, preserve exact indentation after the line number prefix (spaces + line number + tab); never include the prefix in find/replace. Prefer editing existing files; do not create new files unless explicitly requested. Do not use emojis unless the user asks. The edit fails if find is not unique unless all=true. Use all=true to replace every instance (e.g., renames).",
                    "arguments": {
                        "path": { "type": "string", "description": "File path (absolute or relative to workDir)." },
                        "find": { "type": "string", "description": "Text to find." },
                        "replace": { "type": "string", "description": "Replacement text." },
                        "all": { "type": "boolean", "description": "Replace all occurrences when true.", "default": true }
                    }
                }
                """;
        }

        public string Name => "edit_file";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string path, string find, string replace, bool all = true)
        {
            path = FileToolPaths.NormalizePath(path, _config);

            _tracker.AssertFresh(path);

            if (find == replace)
            {
                throw new Exception("No changes to make: find and replace are exactly the same.");
            }

            if (!File.Exists(path))
            {
                throw new Exception("File not found.");
            }

            var size = new FileInfo(path).Length;
            if (size > _maxFileBytes)
            {
                throw new Exception($"File is too large ({size / 1024}KB). Use write_file with mode=chunked for large changes.");
            }

            var content = File.ReadAllText(path, Encoding.UTF8);

            if (string.IsNullOrEmpty(find))
            {
                throw new Exception("find is required.");
            }

            var matches = CountMatches(content, find);
            if (matches == 0)
            {
                throw new Exception("String to replace not found in file.");
            }

            if (matches > 1 && !all)
            {
                throw new Exception($"Found {matches} matches. Set all=true to replace all, or provide a more specific find string.");
            }

            var updated = all
                ? content.Replace(find, replace ?? string.Empty)
                : ReplaceFirst(content, find, replace ?? string.Empty);

            File.WriteAllText(path, updated, Encoding.UTF8);

            return "File updated.";
        }

        private string ReplaceFirst(string text, string find, string replace)
        {
            var index = text.IndexOf(find, StringComparison.Ordinal);
            if (index < 0)
            {
                return text;
            }

            return text[..index] + replace + text[(index + find.Length)..];
        }

        private int CountMatches(string text, string find)
        {
            var count = 0;
            var index = 0;
            while (true)
            {
                index = text.IndexOf(find, index, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                count++;
                index += find.Length;
            }

            return count;
        }

    }

    [Inject<IAgentTool>]
    internal sealed class DeleteFileTool : IAgentTool
    {
        private readonly AppConfig _config;
        private readonly FileReadTracker _tracker;

        public DeleteFileTool(AppConfig config, FileReadTracker tracker)
        {
            _config = config;
            _tracker = tracker;

            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "Delete a local file. Rules: You must use read_file at least once before deleting. Prefer editing over deleting unless the user explicitly requests removal. Do not delete documentation files (*.md) unless explicitly requested.",
                    "arguments": {
                        "path": { "type": "string", "description": "File path (absolute or relative to workDir)." }
                    }
                }
                """;
        }

        public string Name => "delete_file";
        public string Description { get; }
        public bool IsReadOnly => false;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string path)
        {
            path = FileToolPaths.NormalizePath(path, _config);

            _tracker.AssertFresh(path);

            if (!File.Exists(path))
            {
                throw new Exception("File not found.");
            }

            File.Delete(path);

            return "File deleted.";
        }

    }

    [Inject<IAgentTool>]
    internal sealed class ListDirectoryTool : IAgentTool
    {
        private readonly AppConfig _config;
        private readonly int _maxEntries = 2000;
        private readonly int _maxLineLength = 2000;
        private readonly double _maxTokens = 0.25 * 1024 * 1024;

        public ListDirectoryTool(AppConfig config)
        {
            _config = config;
            this.Description = $$"""
                {
                    "name": "{{this.Name}}",
                    "description": "List entries in a local directory. Use this to discover paths before read/write/edit/delete. Prefer non-recursive listing unless recursive is explicitly requested.",
                    "arguments": {
                        "path": { "type": "string", "description": "Directory path (absolute or relative to workDir)." },
                        "recursive": { "type": "boolean", "description": "List recursively when true.", "default": false }
                    }
                }
                """;
        }

        public string Name => "list_directory";
        public string Description { get; }
        public bool IsReadOnly => true;
        public bool IsEnable => true;
        public Delegate Handler => this.Run;

        private string Run(string path, bool recursive = false)
        {
            path = FileToolPaths.NormalizePath(path, _config);

            if (!Directory.Exists(path))
            {
                throw new Exception("Directory not found.");
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            var entries = Directory.GetFileSystemEntries(path, "*", option)
                .Take(_maxEntries)
                .Select(e => e.Length > _maxLineLength ? e[.._maxLineLength] : e)
                .ToArray();

            var result = entries.Length == 0 ? "Directory is empty." : string.Join(Environment.NewLine, entries);

            if (Encoding.UTF8.GetByteCount(result) is { } count && count > _maxTokens)
            {
                throw new Exception($"Directory listing is too large ({count / 1024}KB). Limit is {_maxTokens / 1024}KB. Try a narrower path or non-recursive listing.");
            }

            return result;
        }

    }

    [Inject]
    internal sealed class FileReadTracker
    {
        private readonly Dictionary<string, long> _readTimestamps = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _writeOffsets = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        public void MarkRead(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            var stamp = File.GetLastWriteTimeUtc(path).Ticks;

            lock (_lock)
            {
                _readTimestamps[path] = stamp;
            }
        }

        public void AssertFresh(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new Exception("path is required.");
            }

            if (!File.Exists(path))
            {
                return;
            }

            long recorded;

            lock (_lock)
            {
                if (!_readTimestamps.TryGetValue(path, out recorded))
                {
                    throw new Exception("File has not been read yet. Read it first before writing to it.");
                }
            }

            var current = File.GetLastWriteTimeUtc(path).Ticks;
            if (current > recorded)
            {
                throw new Exception("File has changed since last read. Read it again before writing.");
            }
        }

        public void AssertWriteOffset(string path, long offset)
        {
            lock (_lock)
            {
                if (!_writeOffsets.TryGetValue(path, out var current))
                {
                    current = File.Exists(path) ? new FileInfo(path).Length : 0;
                    _writeOffsets[path] = current;
                }

                if (offset != current)
                {
                    throw new Exception($"Invalid offset. Expected {current}.");
                }
            }
        }

        public void AdvanceWriteOffset(string path, long delta)
        {
            lock (_lock)
            {
                var current = _writeOffsets.TryGetValue(path, out var value) ? value : 0;
                _writeOffsets[path] = current + delta;
            }
        }

        public void ResetWriteOffset(string path, long offset)
        {
            lock (_lock)
            {
                _writeOffsets[path] = offset;
            }
        }
    }

    internal static class FileToolPaths
    {
        public static string NormalizePath(string path, AppConfig config)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new Exception("path is required.");
            }

            var fullPath = Path.IsPathRooted(path)
                ? Path.GetFullPath(path)
                : Path.GetFullPath(Path.Combine(config.WorkDir, path));

            return fullPath;
        }
    }
}



