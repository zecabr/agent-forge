using System.Diagnostics;
using AgentForge.Core.Abstractions;

namespace AgentForge.Observability;

/// <summary>
/// Wrapper de <see cref="ITraceSpan"/> sobre <see cref="Activity"/>.
/// Quando a activity subjacente é null (nenhum listener ativo), todas as
/// operações são no-op e o overhead é desprezível.
/// </summary>
internal sealed class ActivitySpan : ITraceSpan
{
    private readonly Activity? _activity;
    private bool _disposed;

    public ActivitySpan(Activity? activity)
    {
        _activity = activity;
    }

    public void SetAttribute(string key, object? value)
    {
        _activity?.SetTag(key, value);
    }

    public void SetError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (_activity is null)
        {
            return;
        }

        _activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        _activity.SetTag("exception.type", exception.GetType().FullName);
        _activity.SetTag("exception.message", exception.Message);

        if (exception.StackTrace is { } stack)
        {
            _activity.SetTag("exception.stacktrace", stack);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _activity?.Dispose();
    }
}
