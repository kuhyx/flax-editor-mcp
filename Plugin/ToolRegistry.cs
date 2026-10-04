#if FLAX_EDITOR
using System;
using System.Collections.Generic;

namespace FlaxEditorMcp
{
    /// <summary>One MCP tool: its schema plus the handler that runs it.</summary>
    public sealed class Tool
    {
        public string Name;
        public string Description;
        public string InputSchemaJson;
        public Func<Dictionary<string, object>, string> Handler;
    }

    /// <summary>Name -> Tool lookup, in registration order for tools/list.</summary>
    public sealed class ToolRegistry
    {
        private readonly List<Tool> _ordered = new List<Tool>();
        private readonly Dictionary<string, Tool> _byName = new Dictionary<string, Tool>();

        public void Register(
            string name, string description, string inputSchema,
            Func<Dictionary<string, object>, string> handler)
        {
            var tool = new Tool
            {
                Name = name,
                Description = description,
                InputSchemaJson = inputSchema,
                Handler = handler,
            };
            _ordered.Add(tool);
            _byName[name] = tool;
        }

        public IReadOnlyList<Tool> All => _ordered;

        public bool TryGet(string name, out Tool tool) => _byName.TryGetValue(name, out tool);
    }
}
#endif
