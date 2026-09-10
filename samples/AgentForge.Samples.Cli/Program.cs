using System.Globalization;
using AgentForge.Core;
using AgentForge.Core.Abstractions;
using AgentForge.Guardrails;
using AgentForge.Providers.Anthropic;

var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.Error.WriteLine("[erro] defina a variável de ambiente ANTHROPIC_API_KEY antes de rodar.");
    Console.Error.WriteLine();
    Console.Error.WriteLine("  PowerShell: $env:ANTHROPIC_API_KEY = \"sk-ant-...\"");
    Console.Error.WriteLine("  bash/zsh:   export ANTHROPIC_API_KEY=sk-ant-...");
    return 1;
}

var workspaceId = Environment.GetEnvironmentVariable("ANTHROPIC_WORKSPACE_ID");
var model = Environment.GetEnvironmentVariable("AGENT_FORGE_MODEL") ?? "claude-3-5-sonnet-latest";
var costCap = ParseDecimal(Environment.GetEnvironmentVariable("AGENT_FORGE_COST_CAP_USD"), fallback: 1.00m);
var maxSteps = ParseInt(Environment.GetEnvironmentVariable("AGENT_FORGE_MAX_STEPS"), fallback: 5);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

using var provider = new AnthropicChatProvider(apiKey, workspaceId: workspaceId);

var guardrails = new IGuardrail[]
{
    new PromptInjectionGuardrail(),
    new CostCapGuardrail(thresholdUsd: costCap * 0.9m),
};

var options = new AgentOptions(model, MaxTokens: 2048, MaxSteps: maxSteps);
var agent = new Agent(provider, guardrails: guardrails, options: options);
var session = new AgentSession(costCapUsd: costCap);

Banner(model, costCap, maxSteps, workspaceId);

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
        PrintResult(result);
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
return 0;

static void Banner(string model, decimal costCap, int maxSteps, string? workspaceId)
{
    Console.WriteLine("agent-forge · sample CLI (v0.1)");
    Console.WriteLine($"  model:      {model}");
    Console.WriteLine($"  cost cap:   {FormatUsd(costCap)}  (early-brake em 90%)");
    Console.WriteLine($"  max steps:  {maxSteps}");
    if (!string.IsNullOrWhiteSpace(workspaceId))
    {
        Console.WriteLine($"  workspace:  {workspaceId}");
    }
    Console.WriteLine();
    Console.WriteLine("digite sua mensagem, 'exit' pra sair, ou Ctrl+C.");
    Console.WriteLine(new string('-', 60));
    Console.WriteLine();
}

static void PrintResult(AgentResult result)
{
    switch (result.Outcome)
    {
        case AgentOutcome.Success:
            Console.WriteLine($"claude > {result.FinalText}");
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
