using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Core.Tools;

namespace AgentForge.Samples.Cli;

/// <summary>
/// Decorator sobre <see cref="IMcpClient"/> que printa cada chamada no stderr.
/// Ativado via env var <c>AGENT_FORGE_VERBOSE=1</c>. Útil pra diagnosticar
/// "por que o agente estourou max_steps" ou "por que o input inflou".
/// </summary>
internal sealed class VerboseMcpClient : IMcpClient
{
    private const int InputPreviewChars = 200;
    private const int OutputPreviewChars = 200;

    private readonly IMcpClient _inner;
    private int _callSeq;

    public VerboseMcpClient(IMcpClient inner)
    {
        _inner = inner;
    }

    public Task<IReadOnlyList<ToolDefinition>> DiscoverToolsAsync(CancellationToken ct = default)
        => _inner.DiscoverToolsAsync(ct);

    public async Task<ToolResultBlock> InvokeAsync(
        string toolUseId,
        string toolName,
        string argumentsJson,
        CancellationToken ct = default)
    {
        var seq = Interlocked.Increment(ref _callSeq);
        var argsPreview = Truncate(argumentsJson, InputPreviewChars);
        Console.Error.WriteLine($"[tool #{seq}] {toolName}({argsPreview})");

        var start = DateTime.UtcNow;
        var result = await _inner.InvokeAsync(toolUseId, toolName, argumentsJson, ct).ConfigureAwait(false);
        var elapsedMs = (int)(DateTime.UtcNow - start).TotalMilliseconds;

        var status = result.IsError ? "ERROR" : "ok";
        var outLen = result.ResultJson.Length;
        var outPreview = Truncate(result.ResultJson.Replace('\n', ' '), OutputPreviewChars);

        Console.Error.WriteLine($"[tool #{seq}] -> {status} · {outLen} chars · {elapsedMs}ms · {outPreview}");
        return result;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s))
        {
            return "(empty)";
        }

        return s.Length <= max ? s : s[..max] + "...(+" + (s.Length - max) + ")";
    }
}
