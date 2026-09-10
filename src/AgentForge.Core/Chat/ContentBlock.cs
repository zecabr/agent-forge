namespace AgentForge.Core.Chat;

/// <summary>
/// Bloco de conteúdo de uma mensagem. Espelha o modelo de content blocks
/// usado pela Anthropic Messages API e por implementações compatíveis.
/// </summary>
public abstract record ContentBlock;

/// <summary>Texto simples.</summary>
public sealed record TextBlock(string Text) : ContentBlock;

/// <summary>
/// Assistente pediu para invocar uma tool. <paramref name="Id"/> é o handle
/// que o próximo <see cref="ToolResultBlock"/> deve referenciar.
/// </summary>
public sealed record ToolUseBlock(string Id, string Name, string InputJson) : ContentBlock;

/// <summary>
/// Resultado de uma tool anterior. <paramref name="IsError"/> sinaliza falha
/// sem travar o loop — o modelo decide o próximo passo.
/// </summary>
public sealed record ToolResultBlock(string ToolUseId, string ResultJson, bool IsError = false) : ContentBlock;
