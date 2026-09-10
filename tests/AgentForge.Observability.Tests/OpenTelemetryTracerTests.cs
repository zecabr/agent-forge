using System.Diagnostics;
using AgentForge.Observability;
using Xunit;

namespace AgentForge.Observability.Tests;

public class OpenTelemetryTracerTests
{
    private const string TestSourceName = "AgentForge.Test";

    /// <summary>
    /// Cria um listener temporário que captura tudo — o padrão pra testes
    /// que verificam emissão de spans via ActivitySource.
    /// </summary>
    private static (ActivityListener listener, List<Activity> captured) StartListener()
    {
        var captured = new List<Activity>();
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TestSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity => captured.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        return (listener, captured);
    }

    [Fact]
    public void StartSpan_Emits_Activity_With_Name_And_Initial_Attributes()
    {
        var (listener, captured) = StartListener();
        using var _ = listener;

        using var tracer = new OpenTelemetryTracer(TestSourceName);
        using (var span = tracer.StartSpan("test.op", new Dictionary<string, object?>
        {
            ["agent.session_id"] = "sess_1",
            ["agent.model"] = "test-model",
        }))
        {
            span.SetAttribute("agent.step", 3);
        }

        Assert.Single(captured);
        var activity = captured[0];
        Assert.Equal("test.op", activity.DisplayName);
        Assert.Equal("sess_1", activity.GetTagItem("agent.session_id"));
        Assert.Equal("test-model", activity.GetTagItem("agent.model"));
        Assert.Equal(3, activity.GetTagItem("agent.step"));
    }

    [Fact]
    public void SetError_Sets_Status_And_Exception_Tags()
    {
        var (listener, captured) = StartListener();
        using var _ = listener;

        using var tracer = new OpenTelemetryTracer(TestSourceName);
        using (var span = tracer.StartSpan("test.error"))
        {
            span.SetError(new InvalidOperationException("boom"));
        }

        Assert.Single(captured);
        var activity = captured[0];
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        Assert.Equal("System.InvalidOperationException", activity.GetTagItem("exception.type"));
        Assert.Equal("boom", activity.GetTagItem("exception.message"));
    }

    [Fact]
    public void Span_Is_NoOp_When_No_Listener_Registered()
    {
        // sem listener registrado — StartActivity retorna null e o span deve ser silencioso
        using var tracer = new OpenTelemetryTracer("Source.WithoutListener");
        using var span = tracer.StartSpan("test.op", new Dictionary<string, object?> { ["k"] = "v" });

        // não deve lançar exception em nenhuma operação
        span.SetAttribute("extra", 42);
        span.SetError(new Exception("test"));
    }

    [Fact]
    public void SourceName_Reflects_Constructor_Argument()
    {
        using var tracer = new OpenTelemetryTracer("Custom.Source", version: "1.2.3");

        Assert.Equal("Custom.Source", tracer.SourceName);
    }

    [Fact]
    public void Constructor_Rejects_Null_Or_Whitespace_SourceName()
    {
        Assert.ThrowsAny<ArgumentException>(() => new OpenTelemetryTracer(""));
        Assert.ThrowsAny<ArgumentException>(() => new OpenTelemetryTracer("   "));
        Assert.ThrowsAny<ArgumentException>(() => new OpenTelemetryTracer(null!));
    }

    [Fact]
    public void Dispose_Prevents_Further_Span_Creation()
    {
        var tracer = new OpenTelemetryTracer(TestSourceName);
        tracer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => tracer.StartSpan("test.op"));
    }
}
