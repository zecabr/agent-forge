using System.Diagnostics;
using AgentForge.Core.Abstractions;

namespace AgentForge.Observability;

/// <summary>
/// Implementação de <see cref="ITracer"/> sobre <see cref="ActivitySource"/> —
/// o mecanismo nativo de tracing do .NET, compatível com OpenTelemetry.
/// <para>
/// Este tracer <b>não</b> inclui exportador. Cabe ao consumidor configurar o
/// TracerProvider da OpenTelemetry SDK na aplicação — ver ADR-003 no repositório.
/// </para>
/// <para>
/// Zero overhead quando nenhum <see cref="ActivityListener"/> está registrado
/// para o <see cref="ActivitySource"/>: <see cref="ActivitySource.StartActivity(string, ActivityKind)"/>
/// retorna null nesse caso e o span vira no-op.
/// </para>
/// </summary>
public sealed class OpenTelemetryTracer : ITracer, IDisposable
{
    /// <summary>Nome default do <see cref="ActivitySource"/>. Use este no seu TracerProvider.</summary>
    public const string DefaultSourceName = "AgentForge";

    private readonly ActivitySource _source;
    private bool _disposed;

    public OpenTelemetryTracer(string sourceName = DefaultSourceName, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        _source = version is null ? new ActivitySource(sourceName) : new ActivitySource(sourceName, version);
    }

    public string SourceName => _source.Name;

    public ITraceSpan StartSpan(string name, IReadOnlyDictionary<string, object?>? attributes = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var activity = _source.StartActivity(name, ActivityKind.Internal);

        if (activity is not null && attributes is not null)
        {
            foreach (var (key, value) in attributes)
            {
                activity.SetTag(key, value);
            }
        }

        return new ActivitySpan(activity);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _source.Dispose();
    }
}
