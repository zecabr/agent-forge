using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace AgentForge.Mcp.Transport;

/// <summary>
/// Transporte MCP via stdio: spawn de um processo filho e comunicação por
/// linhas JSON-RPC 2.0 (line-delimited) sobre stdin/stdout do processo.
/// </summary>
public sealed class StdioTransport : IMcpTransport
{
    private readonly McpServerConfig _config;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private Process? _process;
    private StreamReader? _stdout;
    private StreamWriter? _stdin;
    private Task? _readLoop;
    private int _nextId;
    private bool _disposed;

    public StdioTransport(McpServerConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public string Name => _config.Name;

    public Task StartAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();

        var psi = new ProcessStartInfo(_config.Command)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (_config.Args is not null)
        {
            foreach (var arg in _config.Args)
            {
                psi.ArgumentList.Add(arg);
            }
        }

        if (_config.EnvironmentVariables is not null)
        {
            foreach (var kv in _config.EnvironmentVariables)
            {
                psi.EnvironmentVariables[kv.Key] = kv.Value;
            }
        }

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start MCP server process for '{_config.Name}' (command: {_config.Command}).");

        _stdout = _process.StandardOutput;
        _stdin = _process.StandardInput;

        // drena stderr pra evitar deadlock por buffer cheio
        _ = Task.Run(async () =>
        {
            try
            {
                await _process.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // ignora — não é canal crítico
            }
        }, ct);

        _readLoop = Task.Run(() => ReadLoopAsync(ct), ct);
        return Task.CompletedTask;
    }

    public async Task<JsonNode?> SendRequestAsync(string method, JsonNode? @params, CancellationToken ct = default)
    {
        ThrowIfNotStarted();

        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = @params,
        };

        await WriteLineAsync(envelope.ToJsonString(), ct).ConfigureAwait(false);

        using var registration = ct.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
            {
                pending.TrySetCanceled(ct);
            }
        });

        return await tcs.Task.ConfigureAwait(false);
    }

    public Task SendNotificationAsync(string method, JsonNode? @params, CancellationToken ct = default)
    {
        ThrowIfNotStarted();

        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method,
            ["params"] = @params,
        };

        return WriteLineAsync(envelope.ToJsonString(), ct);
    }

    private async Task WriteLineAsync(string line, CancellationToken ct)
    {
        await _stdin!.WriteLineAsync(line.AsMemory(), ct).ConfigureAwait(false);
        await _stdin.FlushAsync(ct).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await _stdout!.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
            {
                JsonNode? envelope;
                try
                {
                    envelope = JsonNode.Parse(line);
                }
                catch
                {
                    continue; // ignora ruído — servidores mal comportados às vezes logam em stdout
                }

                if (envelope is null)
                {
                    continue;
                }

                var idNode = envelope["id"];
                if (idNode is null)
                {
                    // notification do servidor — ignoramos por ora (roadmap: emitir evento)
                    continue;
                }

                var id = idNode.GetValue<int>();
                if (!_pending.TryRemove(id, out var pending))
                {
                    continue;
                }

                var errorNode = envelope["error"];
                if (errorNode is not null)
                {
                    var msg = errorNode["message"]?.GetValue<string>() ?? "MCP server returned an error";
                    var code = errorNode["code"]?.GetValue<int>() ?? -1;
                    pending.TrySetException(new McpProtocolException(msg, code));
                }
                else
                {
                    pending.TrySetResult(envelope["result"]);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown normal
        }
        catch (Exception ex)
        {
            // qualquer request pendente é interrompido
            foreach (var kv in _pending)
            {
                kv.Value.TrySetException(ex);
            }

            _pending.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _stdin?.Close();
        }
        catch
        {
            // ignora
        }

        if (_process is not null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch
            {
                // processo já saiu
            }
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // não bloqueia dispose
            }
        }

        _process?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNotStarted()
    {
        ThrowIfDisposed();
        if (_stdin is null || _stdout is null)
        {
            throw new InvalidOperationException("Transport not started. Call StartAsync first.");
        }
    }
}
