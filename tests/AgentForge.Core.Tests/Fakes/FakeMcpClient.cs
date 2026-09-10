using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Core.Tools;

namespace AgentForge.Core.Tests.Fakes;

/// <summary>
/// Cliente MCP fake: expõe tools declaradas em <see cref="Tools"/> e
/// resolve invocações via handlers registrados em <see cref="Invocations"/>.
/// Tool sem handler devolve resultado com IsError=true.
/// </summary>
public sealed class FakeMcpClient : IMcpClient
{
    private readonly List<(string ToolUseId, string ToolName, string ArgumentsJson)> _invocationLog = [];

    public IReadOnlyList<ToolDefinition> Tools { get; init; } = [];

    public Dictionary<string, Func<string, string>> Invocations { get; } = [];

    public IReadOnlyList<(string ToolUseId, string ToolName, string ArgumentsJson)> InvocationLog =>
        _invocationLog;

    public Task<IReadOnlyList<ToolDefinition>> DiscoverToolsAsync(CancellationToken ct = default) =>
        Task.FromResult(Tools);

    public Task<ToolResultBlock> InvokeAsync(
        string toolUseId,
        string toolName,
        string argumentsJson,
        CancellationToken ct = default)
    {
        _invocationLog.Add((toolUseId, toolName, argumentsJson));

        if (!Invocations.TryGetValue(toolName, out var handler))
        {
            return Task.FromResult(new ToolResultBlock(
                toolUseId,
                $"tool '{toolName}' not registered in FakeMcpClient",
                IsError: true));
        }

        var result = handler(argumentsJson);
        return Task.FromResult(new ToolResultBlock(toolUseId, result));
    }
}
