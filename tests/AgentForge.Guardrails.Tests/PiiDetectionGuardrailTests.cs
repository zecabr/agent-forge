using AgentForge.Core.Abstractions;
using AgentForge.Core.Chat;
using AgentForge.Guardrails;
using Xunit;

namespace AgentForge.Guardrails.Tests;

public class PiiDetectionGuardrailTests
{
    private static GuardrailContext Ctx(GuardrailStage stage, params ChatMessage[] messages) =>
        new(stage, messages, CumulativeCostUsd: 0m);

    [Fact]
    public async Task Passes_On_Message_Without_Pii()
    {
        var guard = new PiiDetectionGuardrail();
        var ctx = Ctx(GuardrailStage.PreTurn, ChatMessage.User("qual é a capital do Brasil?"));

        var result = await guard.CheckAsync(ctx);

        Assert.True(result.Passed);
    }

    [Theory]
    [InlineData("meu email é zeca@example.com pode responder?", "email")]
    [InlineData("meu cpf é 123.456.789-00", "cpf")]
    [InlineData("cnpj da empresa 12.345.678/0001-90", "cnpj")]
    [InlineData("cartão 4532 1234 5678 9010 é meu", "credit-card")]
    [InlineData("SSN 123-45-6789 for reference", "ssn")]
    public async Task Fails_On_Detected_Pii_Kinds(string userText, string expectedKind)
    {
        var guard = new PiiDetectionGuardrail();
        var ctx = Ctx(GuardrailStage.PreTurn, ChatMessage.User(userText));

        var result = await guard.CheckAsync(ctx);

        Assert.False(result.Passed);
        Assert.NotNull(result.Reason);
        Assert.Contains(expectedKind, result.Reason);
    }

    [Fact]
    public async Task Only_Detects_Configured_Kinds()
    {
        var guard = new PiiDetectionGuardrail(kindsToDetect: ["email"]);
        var ctx = Ctx(GuardrailStage.PreTurn, ChatMessage.User("cpf 123.456.789-00 e mais nada"));

        var result = await guard.CheckAsync(ctx);

        Assert.True(result.Passed); // CPF ignorado porque só email está habilitado
    }

    [Fact]
    public async Task Checks_Assistant_Message_On_PostTurn()
    {
        var guard = new PiiDetectionGuardrail();
        var ctx = Ctx(
            GuardrailStage.PostTurn,
            ChatMessage.User("me dê um exemplo de dado"),
            ChatMessage.Assistant("aqui está: zeca@example.com"));

        var result = await guard.CheckAsync(ctx);

        Assert.False(result.Passed);
        Assert.Contains("email", result.Reason!);
    }

    [Fact]
    public async Task Detects_Multiple_Kinds_In_Same_Message()
    {
        var guard = new PiiDetectionGuardrail();
        var ctx = Ctx(GuardrailStage.PreTurn,
            ChatMessage.User("email zeca@example.com e cpf 123.456.789-00"));

        var result = await guard.CheckAsync(ctx);

        Assert.False(result.Passed);
        Assert.Contains("email", result.Reason!);
        Assert.Contains("cpf", result.Reason!);
    }
}
