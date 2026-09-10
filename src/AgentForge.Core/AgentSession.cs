using AgentForge.Core.Chat;

namespace AgentForge.Core;

/// <summary>
/// Estado mutável de uma conversa em curso: histórico de mensagens,
/// consumo acumulado e teto de custo por sessão.
/// </summary>
public sealed class AgentSession
{
    private readonly List<ChatMessage> _messages = [];

    public AgentSession(string? id = null, decimal costCapUsd = 1.00m)
    {
        Id = id ?? Guid.NewGuid().ToString("N");
        CostCapUsd = costCapUsd;
    }

    public string Id { get; }

    public decimal CostCapUsd { get; }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    public UsageStats CumulativeUsage { get; private set; } = UsageStats.Empty;

    /// <summary>true quando <see cref="CumulativeUsage"/>.CostUsd &gt; <see cref="CostCapUsd"/>.</summary>
    public bool ExceededCostCap => CumulativeUsage.CostUsd > CostCapUsd;

    public void AppendMessage(ChatMessage message) => _messages.Add(message);

    public void RecordUsage(UsageStats usage) => CumulativeUsage = CumulativeUsage.Add(usage);
}
