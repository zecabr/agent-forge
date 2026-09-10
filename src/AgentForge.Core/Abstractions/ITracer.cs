namespace AgentForge.Core.Abstractions;

/// <summary>
/// Emissor de spans de tracing. Implementação padrão é um wrapper fino
/// sobre System.Diagnostics.ActivitySource (compatível com OpenTelemetry).
/// </summary>
public interface ITracer
{
    ITraceSpan StartSpan(string name, IReadOnlyDictionary<string, object?>? attributes = null);
}

public interface ITraceSpan : IDisposable
{
    void SetAttribute(string key, object? value);

    void SetError(Exception exception);
}
