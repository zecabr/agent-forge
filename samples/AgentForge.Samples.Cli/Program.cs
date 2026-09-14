using System.Globalization;
using AgentForge.Core;
using AgentForge.Core.Abstractions;
using AgentForge.Core.Tools;
using AgentForge.Guardrails;
using AgentForge.Mcp;
using AgentForge.Providers.Anthropic;
using AgentForge.Providers.Gemini;
using AgentForge.Providers.Resilience;
using AgentForge.Samples.Cli;

using var httpClient = new HttpClient(new RetryingHttpHandler(new HttpClientHandler()));

var providerName = (Environment.GetEnvironmentVariable("AGENT_FORGE_PROVIDER") ?? "anthropic")
    .Trim().ToLowerInvariant();

IChatProvider provider;
IDisposable? providerDisposable;
string defaultModel;
string? extraLine = null;

switch (providerName)
{
    case "gemini":
        var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            Console.Error.WriteLine("[erro] provider=gemini requer GEMINI_API_KEY definida.");
            Console.Error.WriteLine("  PowerShell: $env:GEMINI_API_KEY = \"...\"");
            Console.Error.WriteLine("  bash/zsh:   export GEMINI_API_KEY=...");
            return 1;
        }

        var gemini = new GeminiChatProvider(geminiKey, httpClient);
        provider = gemini;
        providerDisposable = gemini;
        defaultModel = "gemini-flash-lite-latest";
        break;

    case "anthropic":
        var anthKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrWhiteSpace(anthKey))
        {
            Console.Error.WriteLine("[erro] provider=anthropic requer ANTHROPIC_API_KEY definida.");
            Console.Error.WriteLine("  PowerShell: $env:ANTHROPIC_API_KEY = \"sk-ant-...\"");
            Console.Error.WriteLine("  bash/zsh:   export ANTHROPIC_API_KEY=sk-ant-...");
            return 1;
        }

        var workspaceId = Environment.GetEnvironmentVariable("ANTHROPIC_WORKSPACE_ID");
        var anthropic = new AnthropicChatProvider(anthKey, httpClient, workspaceId: workspaceId);
        provider = anthropic;
        providerDisposable = anthropic;
        defaultModel = "claude-3-5-sonnet-latest";
        if (!string.IsNullOrWhiteSpace(workspaceId))
        {
            extraLine = $"workspace:  {workspaceId}";
        }

        break;

    default:
        Console.Error.WriteLine($"[erro] provider '{providerName}' desconhecido. Use 'anthropic' ou 'gemini'.");
        return 1;
}

var model = Environment.GetEnvironmentVariable("AGENT_FORGE_MODEL") ?? defaultModel;
var costCap = ParseDecimal(Environment.GetEnvironmentVariable("AGENT_FORGE_COST_CAP_USD"), fallback: 1.00m);
var maxSteps = ParseInt(Environment.GetEnvironmentVariable("AGENT_FORGE_MAX_STEPS"), fallback: 5);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// MCP: carrega config (--mcp <path>, env AGENT_FORGE_MCP_CONFIG ou ./mcp.json)
// e spawna os servidores declarados. Sem config = sem MCP (backward compat).
McpHost? mcpHost = null;
IReadOnlyList<ToolDefinition> mcpTools = Array.Empty<ToolDefinition>();
string? mcpSummary = null;

var mcpConfigPath = McpConfigLoader.ResolveConfigPath(args);
if (mcpConfigPath is not null)
{
    IReadOnlyList<McpServerConfig> mcpConfigs;
    try
    {
        mcpConfigs = McpConfigLoader.Load(mcpConfigPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[erro] falha ao carregar MCP config '{mcpConfigPath}': {ex.Message}");
        return 1;
    }

    if (mcpConfigs.Count > 0)
    {
        mcpHost = new McpHost(mcpConfigs);
        try
        {
            mcpTools = await mcpHost.DiscoverToolsAsync(cts.Token);
            mcpSummary = $"{mcpConfigs.Count} server(s), {mcpTools.Count} tool(s) · {Path.GetFileName(mcpConfigPath)}";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[erro] falha ao inicializar MCP servers: {ex.Message}");
            await mcpHost.DisposeAsync();
            return 1;
        }
    }
}

try
{
    using (providerDisposable)
    {
        var guardrails = new IGuardrail[]
        {
            new PromptInjectionGuardrail(),
            new CostCapGuardrail(thresholdUsd: costCap * 0.9m),
        };

        var options = new AgentOptions(model, MaxTokens: 2048, MaxSteps: maxSteps);

        // AGENT_FORGE_VERBOSE=1 embrulha o McpHost pra logar cada tool call no stderr.
        // Diagnostica loops, payloads gigantes, tools erradas.
        IMcpClient? agentMcp = mcpHost;
        var verbose = Environment.GetEnvironmentVariable("AGENT_FORGE_VERBOSE") == "1";
        if (verbose && mcpHost is not null)
        {
            agentMcp = new VerboseMcpClient(mcpHost);
        }

        var agent = new Agent(provider, mcp: agentMcp, guardrails: guardrails, options: options);
        var session = new AgentSession(costCapUsd: costCap);

        Banner(providerName, model, costCap, maxSteps, extraLine, mcpSummary, mcpTools);

        while (!cts.IsCancellationRequested)
        {
            Console.Write("você > ");
            string? input;
            try
            {
                input = Console.ReadLine();
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (input is null || input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            Console.WriteLine();

            try
            {
                var result = await agent.RunAsync(session, input, cts.Token);
                PrintResult(result, providerName);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[cancelado]");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[falha] {ex.GetType().Name}: {ex.Message}");
            }

            PrintSessionFooter(session);
            Console.WriteLine();
        }

        Console.WriteLine();
        Console.WriteLine($"encerrando · sessão custou {FormatUsd(session.CumulativeUsage.CostUsd)} · {session.CumulativeUsage.InputTokens}in / {session.CumulativeUsage.OutputTokens}out");
    }
}
finally
{
    if (mcpHost is not null)
    {
        await mcpHost.DisposeAsync();
    }
}

return 0;

static void Banner(string providerName, string model, decimal costCap, int maxSteps, string? extra, string? mcpSummary, IReadOnlyList<ToolDefinition> mcpTools)
{
    Console.WriteLine("agent-forge · sample CLI (v0.1)");
    Console.WriteLine($"  provider:   {providerName}");
    Console.WriteLine($"  model:      {model}");
    Console.WriteLine($"  cost cap:   {FormatUsd(costCap)}  (early-brake em 90%)");
    Console.WriteLine($"  max steps:  {maxSteps}");
    if (extra is not null)
    {
        Console.WriteLine($"  {extra}");
    }

    if (mcpSummary is not null)
    {
        Console.WriteLine($"  mcp:        {mcpSummary}");
        foreach (var tool in mcpTools)
        {
            Console.WriteLine($"                · {tool.Name}");
        }
    }
    else
    {
        Console.WriteLine("  mcp:        (nenhum servidor configurado — use --mcp <path> ou mcp.json no cwd)");
    }

    Console.WriteLine();
    Console.WriteLine("digite sua mensagem, 'exit' pra sair, ou Ctrl+C.");
    Console.WriteLine(new string('-', 60));
    Console.WriteLine();
}

static void PrintResult(AgentResult result, string providerName)
{
    var speakerName = providerName == "gemini" ? "gemini" : "claude";

    switch (result.Outcome)
    {
        case AgentOutcome.Success:
            Console.WriteLine($"{speakerName} > {result.FinalText}");
            break;
        case AgentOutcome.BlockedByGuardrail:
            Console.WriteLine($"[BLOQUEADO] {result.Reason}");
            break;
        case AgentOutcome.CostCapExceeded:
            Console.WriteLine($"[COST-CAP] custo acumulado {FormatUsd(result.Usage.CostUsd)} estourou o teto");
            break;
        case AgentOutcome.MaxStepsReached:
            Console.WriteLine($"[MAX-STEPS] {result.Steps} passos sem convergir");
            break;
        case AgentOutcome.Error:
            Console.WriteLine($"[ERROR] {result.Reason}");
            break;
    }
}

static void PrintSessionFooter(AgentSession session)
{
    Console.WriteLine();
    Console.WriteLine(
        $"  session · {session.CumulativeUsage.InputTokens}in / {session.CumulativeUsage.OutputTokens}out · {FormatUsd(session.CumulativeUsage.CostUsd)}");
}

static string FormatUsd(decimal amount) =>
    string.Create(CultureInfo.InvariantCulture, $"${amount:F4}");

static decimal ParseDecimal(string? raw, decimal fallback) =>
    decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : fallback;

static int ParseInt(string? raw, int fallback) =>
    int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
