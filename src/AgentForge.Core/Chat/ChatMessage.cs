namespace AgentForge.Core.Chat;

/// <summary>
/// Mensagem em uma conversa com um provider de LLM.
/// Use os factory methods para construir os casos comuns.
/// </summary>
public sealed record ChatMessage(ChatRole Role, IReadOnlyList<ContentBlock> Content)
{
    public static ChatMessage System(string text) =>
        new(ChatRole.System, [new TextBlock(text)]);

    public static ChatMessage User(string text) =>
        new(ChatRole.User, [new TextBlock(text)]);

    public static ChatMessage Assistant(string text) =>
        new(ChatRole.Assistant, [new TextBlock(text)]);

    public static ChatMessage Assistant(IReadOnlyList<ContentBlock> blocks) =>
        new(ChatRole.Assistant, blocks);

    public static ChatMessage Tool(IReadOnlyList<ToolResultBlock> results) =>
        new(ChatRole.Tool, results);
}

public enum ChatRole
{
    System,
    User,
    Assistant,
    Tool,
}
