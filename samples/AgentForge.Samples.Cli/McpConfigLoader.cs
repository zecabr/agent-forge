using System.Text.Json;
using AgentForge.Mcp;

namespace AgentForge.Samples.Cli;

/// <summary>
/// Carrega config de servidores MCP no formato padrão da comunidade
/// (Claude Desktop, Continue). Formato:
/// <code>
/// {
///   "mcpServers": {
///     "docs-reader": {
///       "command": "dotnet",
///       "args": ["path/to/agent-forge-docs-reader.dll", "--root", "."],
///       "env": { "FOO": "bar" }
///     }
///   }
/// }
/// </code>
/// </summary>
internal static class McpConfigLoader
{
    /// <summary>
    /// Resolve o path de config na ordem: argumento CLI <c>--mcp &lt;path&gt;</c>
    /// → env var <c>AGENT_FORGE_MCP_CONFIG</c> → <c>./mcp.json</c> no CWD.
    /// Retorna null se nenhum resolveu — Sample CLI roda sem MCP nesse caso.
    /// </summary>
    public static string? ResolveConfigPath(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--mcp")
            {
                return args[i + 1];
            }
        }

        var envPath = Environment.GetEnvironmentVariable("AGENT_FORGE_MCP_CONFIG");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return envPath;
        }

        var cwdPath = Path.Combine(Environment.CurrentDirectory, "mcp.json");
        return File.Exists(cwdPath) ? cwdPath : null;
    }

    /// <summary>
    /// Lê e parseia o arquivo em uma lista de <see cref="McpServerConfig"/>.
    /// Joga <see cref="InvalidDataException"/> se o JSON estiver malformado ou
    /// se algum servidor não tiver <c>command</c>.
    /// </summary>
    public static IReadOnlyList<McpServerConfig> Load(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"MCP config not found: {configPath}");
        }

        var json = File.ReadAllText(configPath);

        McpConfigRoot? root;
        try
        {
            root = JsonSerializer.Deserialize(json, McpConfigJsonContext.Default.McpConfigRoot);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"MCP config at '{configPath}' is not valid JSON: {ex.Message}", ex);
        }

        if (root?.McpServers is null || root.McpServers.Count == 0)
        {
            return Array.Empty<McpServerConfig>();
        }

        var configs = new List<McpServerConfig>(root.McpServers.Count);
        foreach (var (name, entry) in root.McpServers)
        {
            if (string.IsNullOrWhiteSpace(entry?.Command))
            {
                throw new InvalidDataException(
                    $"MCP server '{name}' in '{configPath}' has no 'command'.");
            }

            configs.Add(new McpServerConfig(
                Name: name,
                Command: entry.Command,
                Args: entry.Args,
                EnvironmentVariables: entry.Env));
        }

        return configs;
    }
}

// Envelope DTO — mantido internal e separado pra permitir source generation.

internal sealed class McpConfigRoot
{
    public Dictionary<string, McpConfigEntry>? McpServers { get; set; }
}

internal sealed class McpConfigEntry
{
    public string? Command { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(McpConfigRoot))]
internal partial class McpConfigJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
