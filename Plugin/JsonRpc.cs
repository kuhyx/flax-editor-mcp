#if FLAX_EDITOR
using System.Collections.Generic;
using System.Text;
using FlaxEngine.Json;

namespace FlaxEditorMcp
{
    /// <summary>
    /// JSON-RPC 2.0 dispatch for the MCP method set this server implements:
    /// initialize, tools/list, tools/call, ping. See
    /// https://modelcontextprotocol.io/specification for the wire format.
    /// </summary>
    public static class JsonRpc
    {
        /// <summary>Handles one request body. Returns null for a notification (no id -> no reply).</summary>
        public static string Dispatch(
            string body, ToolRegistry tools, string protocolVersion, string serverName, string serverVersion)
        {
            var request = JsonSerializer.Deserialize(body) as Dictionary<string, object>;
            if (request == null || !request.TryGetValue("jsonrpc", out var version) || version as string != "2.0")
                return Error(null, -32600, "Invalid Request: jsonrpc must be \"2.0\".");

            request.TryGetValue("id", out var id);
            request.TryGetValue("method", out var methodObj);
            var method = methodObj as string;

            if (id == null)
                return null; // notification: no response expected

            switch (method)
            {
                case "initialize":
                    return Result(id, Obj(
                        "protocolVersion", Str(protocolVersion),
                        "capabilities", Obj("tools", Obj("listChanged", "false")),
                        "serverInfo", Obj("name", Str(serverName), "version", Str(serverVersion))));

                case "ping":
                    return Result(id, "{}");

                case "tools/list":
                    return Result(id, ToolsListJson(tools));

                case "tools/call":
                    return HandleToolsCall(id, request, tools);

                default:
                    return Error(id, -32601, $"Method not found: {method}");
            }
        }

        private static string HandleToolsCall(object id, Dictionary<string, object> request, ToolRegistry tools)
        {
            var rpcParams = request.TryGetValue("params", out var p) ? p as Dictionary<string, object> : null;
            var name = rpcParams != null && rpcParams.TryGetValue("name", out var n) ? n as string : null;
            if (string.IsNullOrEmpty(name))
                return Error(id, -32602, "Invalid params: missing 'name'.");
            if (!tools.TryGet(name, out var tool))
                return Error(id, -32602, $"Unknown tool: {name}");

            var args = rpcParams.TryGetValue("arguments", out var a) ? a as Dictionary<string, object> : null;
            args = args ?? new Dictionary<string, object>();

            string toolOutput;
            try
            {
                toolOutput = tool.Handler(args);
            }
            catch (System.Exception ex)
            {
                return Error(id, -1, $"Tool '{name}' failed: {ex.Message}");
            }
            return Result(id, Obj("content", $"[{Obj("type", Str("text"), "text", Str(toolOutput))}]"));
        }

        private static string ToolsListJson(ToolRegistry tools)
        {
            var sb = new StringBuilder("[");
            for (var i = 0; i < tools.All.Count; i++)
            {
                var t = tools.All[i];
                if (i > 0)
                    sb.Append(',');
                sb.Append(Obj("name", Str(t.Name), "description", Str(t.Description), "inputSchema", t.InputSchemaJson));
            }
            sb.Append(']');
            return Obj("tools", sb.ToString());
        }

        // ---- minimal JSON emission: values passed in are already valid JSON fragments ----

        private static string Str(string s) => JsonSerializer.Serialize(s ?? string.Empty);

        private static string Obj(params string[] rawKeyValuePairs)
        {
            var sb = new StringBuilder("{");
            for (var i = 0; i < rawKeyValuePairs.Length; i += 2)
            {
                if (i > 0)
                    sb.Append(',');
                sb.Append(Str(rawKeyValuePairs[i])).Append(':').Append(rawKeyValuePairs[i + 1]);
            }
            sb.Append('}');
            return sb.ToString();
        }

        private static string Result(object id, string resultJson) =>
            $"{{\"jsonrpc\":\"2.0\",\"id\":{IdJson(id)},\"result\":{resultJson}}}";

        private static string Error(object id, int code, string message) =>
            $"{{\"jsonrpc\":\"2.0\",\"id\":{IdJson(id)},\"error\":{{\"code\":{code},\"message\":{Str(message)}}}}}";

        private static string IdJson(object id)
        {
            if (id == null)
                return "null";
            if (id is string s)
                return Str(s);
            return id.ToString();
        }
    }
}
#endif
