namespace AgentForge.Core.Tools;

/// <summary>
/// Descrição de uma tool disponibilizada ao modelo.
/// <paramref name="InputSchemaJson"/> é o JSON Schema do input, como string —
/// mantido cru pra evitar acoplar a uma lib de schema específica.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, string InputSchemaJson);
