#if FLAX_EDITOR
using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using FlaxEditor;
using FlaxEngine;
using FlaxEngine.Json;

namespace FlaxEditorMcp
{
    /// <summary>
    /// Minimal MCP (Model Context Protocol) server over the Streamable HTTP
    /// transport: JSON-RPC 2.0 POSTed to <c>/mcp</c>. Runs a background thread
    /// accepting connections via <see cref="HttpListener"/>; every tool call
    /// that touches editor state is marshaled onto the main thread through
    /// <see cref="InvokeOnMainThread{T}"/> before it runs.
    /// </summary>
    public sealed class McpServer : IDisposable
    {
        private const string ProtocolVersion = "2025-03-26";
        private const string ServerName = "flax-editor-mcp";
        private const string ServerVersion = "0.1.0";

        private readonly int _port;
        private readonly ToolRegistry _tools = new ToolRegistry();
        private HttpListener _listener;
        private Thread _thread;
        private volatile bool _running;

        public McpServer(int port)
        {
            _port = port;
            _tools.Register(
                name: "flax_ping",
                description: "Check the editor is reachable and report its play-mode state.",
                inputSchema: "{\"type\":\"object\",\"properties\":{}}",
                handler: _ => InvokeOnMainThread(() =>
                {
                    var isPlaying = Editor.Instance.StateMachine.IsPlayMode;
                    return $"{{\"ok\":true,\"isPlaying\":{(isPlaying ? "true" : "false")}}}";
                })
            );
        }

        public void Start()
        {
            if (_running)
                return;

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Start();
            _running = true;

            _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "flax-editor-mcp" };
            _thread.Start();
            Debug.Log($"[flax-editor-mcp] listening on 127.0.0.1:{_port}");
        }

        public void Stop()
        {
            if (!_running)
                return;
            // Clear the flag before tearing down the listener: GetContext() on a
            // disposed listener throws, and the loop must read _running=false to
            // treat that as a clean shutdown rather than log a spurious error.
            _running = false;
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch (ObjectDisposedException)
            {
            }
            _thread?.Join(TimeSpan.FromSeconds(2));
            Debug.Log("[flax-editor-mcp] stopped");
        }

        public void Dispose() => Stop();

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext context;
                try
                {
                    context = _listener.GetContext();
                }
                catch (Exception)
                {
                    if (_running)
                        Debug.LogWarning("[flax-editor-mcp] listener error, stopping accept loop");
                    break;
                }
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
        }

        private void HandleRequest(HttpListenerContext context)
        {
            try
            {
                if (context.Request.Url.AbsolutePath.TrimEnd('/') != "/mcp"
                    || context.Request.HttpMethod != "POST")
                {
                    WriteResponse(context.Response, 404, "{\"error\":\"not found; POST /mcp\"}");
                    return;
                }
                string body;
                using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                var reply = JsonRpc.Dispatch(body, _tools, ProtocolVersion, ServerName, ServerVersion);
                if (reply == null)
                    WriteResponse(context.Response, 204, string.Empty); // notification, no reply
                else
                    WriteResponse(context.Response, 200, reply);
            }
            catch (Exception ex)
            {
                WriteResponse(context.Response, 500, $"{{\"error\":{JsonSerializer.Serialize(ex.Message)}}}");
            }
        }

        private static void WriteResponse(HttpListenerResponse response, int status, string json)
        {
            try
            {
                response.StatusCode = status;
                response.ContentType = "application/json; charset=utf-8";
                var bytes = Encoding.UTF8.GetBytes(json);
                response.ContentLength64 = bytes.Length;
                if (bytes.Length > 0)
                    response.OutputStream.Write(bytes, 0, bytes.Length);
            }
            finally
            {
                response.Close();
            }
        }

        /// <summary>
        /// Runs <paramref name="func"/> on the main thread via <see cref="Scripting.InvokeOnUpdate"/>
        /// and blocks the calling worker thread until it completes. Every tool handler that touches
        /// Flax editor/engine API must go through this -- those APIs are not thread-safe.
        /// </summary>
        public static string InvokeOnMainThread(Func<string> func, int timeoutMs = 10000)
        {
            string result = null;
            Exception caught = null;
            using (var done = new ManualResetEventSlim(false))
            {
                Scripting.InvokeOnUpdate(() =>
                {
                    try
                    {
                        result = func();
                    }
                    catch (Exception ex)
                    {
                        caught = ex;
                    }
                    finally
                    {
                        done.Set();
                    }
                });
                if (!done.Wait(timeoutMs))
                    throw new TimeoutException("Main-thread invocation timed out.");
            }
            if (caught != null)
                throw caught;
            return result;
        }
    }
}
#endif
