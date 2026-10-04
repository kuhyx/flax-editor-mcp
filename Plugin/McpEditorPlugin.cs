#if FLAX_EDITOR
using System;
using FlaxEditor;
using FlaxEngine;

namespace FlaxEditorMcp
{
    /// <summary>
    /// Editor entry point: owns the <see cref="McpServer"/> lifecycle.
    /// Add this project as a git submodule under Source/&lt;YourEditorModule&gt;
    /// and reference FlaxEditorMcp.McpEditorPlugin.
    /// </summary>
    public class McpEditorPlugin : EditorPlugin
    {
        private McpServer _server;

        public override void InitializeEditor()
        {
            base.InitializeEditor();
            var port = 19810;
            var portEnv = Environment.GetEnvironmentVariable("FLAX_EDITOR_MCP_PORT");
            if (!string.IsNullOrEmpty(portEnv) && int.TryParse(portEnv, out var parsed))
                port = parsed;
            _server = new McpServer(port);
            _server.Start();
        }

        public override void DeinitializeEditor()
        {
            _server?.Dispose();
            _server = null;
            base.DeinitializeEditor();
        }
    }
}
#endif
