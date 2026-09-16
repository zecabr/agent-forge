using System.Diagnostics;
using AgentForge.Core;
using AgentForge.Core.Abstractions;
using AgentForge.Guardrails;
using AgentForge.Mcp;
using AgentForge.Providers.Gemini;
using AgentForge.Providers.Resilience;
using Xunit;

namespace AgentForge.Evals.Suite;

/// <summary>
/// Fixture compartilhada pela suite de evals. Garante o banco de exemplo,
/// spawna os 3 MCP servers (docs-reader, file-tools, sql-reader), monta
/// Agent + Judge (ambos Gemini, ver nota abaixo), aplica cost cap único
/// pra toda a suite. Se <c>GEMINI_API_KEY</c> não está presente,
/// <see cref="SkipReason"/> é setado — os testes decidem se skipam ou falham.
/// </summary>
/// <remarks>
/// <para><b>Nota sobre o judge:</b> por padrão a suite usa Gemini pro agent
/// (flash-lite) E pro judge (flash-latest, um degrau acima). Free tier
/// do Gemini limita Pro a limit=0, por isso não usamos pro-latest.
/// Bias forte (mesma família, força quase igual): aceitável só pra dev.
/// A ADR-002 recomenda judge de outra família (Claude Opus) pro CI real.
/// Pra alternar, injete outro provider aqui.
/// </para>
/// </remarks>
public sealed class EvalFixture : IAsyncLifetime
{
    // Cost cap total pra suite inteira (6 casos + 6 judgments).
    // Custo esperado por run: ~$0.08 (Gemini flash-lite agent + Gemini pro judge); 2x safety.
    public const decimal SuiteBudgetUsd = 0.20m;

    private HttpClient? _httpClient;
    private GeminiChatProvider? _geminiProvider;
    private McpHost? _mcpHost;

    /// <summary>Motivo do skip; null se a fixture está pronta.</summary>
    public string? SkipReason { get; private set; }

    /// <summary>Runner configurado. Null se <see cref="SkipReason"/> ≠ null.</summary>
    public EvalRunner? Runner { get; private set; }

    /// <summary>Root do repo — útil se algum teste quiser resolver paths.</summary>
    public string RepoRoot { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        RepoRoot = FindRepoRoot();

        var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");

        if (string.IsNullOrWhiteSpace(geminiKey))
        {
            SkipReason = "GEMINI_API_KEY não setada — eval suite skipped.";
            return;
        }

        await EnsureSampleDbAsync(RepoRoot).ConfigureAwait(false);

        // Retry policy endurecida pra tolerar 503 sustentado do free tier do Gemini
        // (o judge processa payloads grandes das respostas do agente e pode saturar por 30-60s).
        var evalRetryPolicy = new RetryPolicy(
            MaxAttempts: 6,
            InitialDelay: TimeSpan.FromSeconds(2),
            BackoffMultiplier: 2.0,
            MaxDelay: TimeSpan.FromSeconds(30),
            JitterFactor: 0.3);
        _httpClient = new HttpClient(new RetryingHttpHandler(new HttpClientHandler(), evalRetryPolicy))
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        _geminiProvider = new GeminiChatProvider(geminiKey, _httpClient);

        var mcpConfigs = BuildMcpConfigs(RepoRoot);
        _mcpHost = new McpHost(mcpConfigs);
        _ = await _mcpHost.DiscoverToolsAsync().ConfigureAwait(false);

        var guardrails = new IGuardrail[]
        {
            new PromptInjectionGuardrail(),
            new CostCapGuardrail(thresholdUsd: SuiteBudgetUsd * 0.9m),
        };

        var agentModel = Environment.GetEnvironmentVariable("AGENT_FORGE_EVAL_AGENT_MODEL")
            ?? "gemini-flash-lite-latest";
        var judgeModel = Environment.GetEnvironmentVariable("AGENT_FORGE_EVAL_JUDGE_MODEL")
            ?? "gemini-flash-latest";

        var options = new AgentOptions(agentModel, MaxTokens: 2048, MaxSteps: 8);
        var agent = new Agent(_geminiProvider, mcp: _mcpHost, guardrails: guardrails, options: options);
        var judge = new LlmJudge(_geminiProvider, judgeModel);
        var budget = EvalBudget.FromUsd(SuiteBudgetUsd);

        Runner = new EvalRunner(agent, judge, budget);
    }

    public async Task DisposeAsync()
    {
        if (_mcpHost is not null)
        {
            await _mcpHost.DisposeAsync().ConfigureAwait(false);
        }

        _geminiProvider?.Dispose();
        _httpClient?.Dispose();
    }

    private static IReadOnlyList<McpServerConfig> BuildMcpConfigs(string repoRoot)
    {
        var config = FindBuildConfig();
        string SampleDll(string project, string dllName) => Path.Combine(
            repoRoot, "samples", project, "bin", config, "net9.0", dllName);

        return
        [
            new McpServerConfig(
                Name: "docs-reader",
                Command: "dotnet",
                Args: [SampleDll("AgentForge.Capabilities.DocsReader", "agent-forge-docs-reader.dll"), "--root", repoRoot]),

            new McpServerConfig(
                Name: "file-tools",
                Command: "dotnet",
                Args: [SampleDll("AgentForge.Capabilities.FileTools", "agent-forge-file-tools.dll"), "--root", repoRoot]),

            new McpServerConfig(
                Name: "sql-reader",
                Command: "dotnet",
                Args: [SampleDll("AgentForge.Capabilities.SqlReader", "agent-forge-sql-reader.dll"), "--db", Path.Combine(repoRoot, "data", "app.db")]),
        ];
    }

    private static string FindBuildConfig()
    {
        var location = typeof(EvalFixture).Assembly.Location;
        return location.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? "Release"
            : "Debug";
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "agent-forge.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Não achei repo root — arquivo agent-forge.sln não encontrado acima do test binary.");
    }

    private static async Task EnsureSampleDbAsync(string repoRoot)
    {
        var dbPath = Path.Combine(repoRoot, "data", "app.db");
        if (File.Exists(dbPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        var config = FindBuildConfig();
        var createDbDll = Path.Combine(
            repoRoot, "tools", "CreateSampleDb", "bin", config, "net9.0", "agent-forge-create-sample-db.dll");
        if (!File.Exists(createDbDll))
        {
            throw new InvalidOperationException(
                $"CreateSampleDb dll não encontrado em {createDbDll} — rode 'dotnet build' antes da eval suite.");
        }

        var psi = new ProcessStartInfo("dotnet", $"\"{createDbDll}\" --db \"{dbPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Falha ao spawnar CreateSampleDb");

        await proc.WaitForExitAsync().ConfigureAwait(false);
        if (proc.ExitCode != 0)
        {
            var stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(false);
            throw new InvalidOperationException(
                $"CreateSampleDb exit {proc.ExitCode}: {stderr}");
        }
    }
}

/// <summary>Marker collection: EvalFixture é única e compartilhada.</summary>
[CollectionDefinition(nameof(EvalCollection))]
public sealed class EvalCollection : ICollectionFixture<EvalFixture> { }
