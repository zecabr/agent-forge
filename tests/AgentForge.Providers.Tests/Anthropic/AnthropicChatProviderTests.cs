using System.Net;
using AgentForge.Core.Chat;
using AgentForge.Providers.Anthropic;
using AgentForge.Providers.Tests.Fakes;
using Xunit;

namespace AgentForge.Providers.Tests.Anthropic;

public class AnthropicChatProviderTests
{
    private const string OkResponse = """
        {
          "id": "msg_1",
          "model": "claude-3-5-sonnet-latest",
          "content": [{"type":"text","text":"pronto"}],
          "stop_reason": "end_turn",
          "usage": {"input_tokens": 10, "output_tokens": 5}
        }
        """;

    [Fact]
    public async Task Sends_ApiKey_And_Version_Headers()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new AnthropicChatProvider("sk-fake-key", http);

        _ = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "claude-3-5-sonnet-latest"));

        Assert.Single(handler.Calls);
        var req = handler.Calls[0].Request;
        Assert.Equal("sk-fake-key", req.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", req.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task Posts_To_V1_Messages_Endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new AnthropicChatProvider("sk-fake-key", http);

        _ = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "claude-3-5-sonnet-latest"));

        var req = handler.Calls[0].Request;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://api.anthropic.com/v1/messages", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Uses_Custom_BaseUri_When_Provided()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new AnthropicChatProvider(
            "sk-fake-key", http, baseUri: "https://proxy.internal.example.com");

        _ = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "claude-3-5-sonnet-latest"));

        var req = handler.Calls[0].Request;
        Assert.Equal("https://proxy.internal.example.com/v1/messages", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Returns_Mapped_ChatResponse()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new AnthropicChatProvider("sk-fake-key", http);

        var response = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "claude-3-5-sonnet-latest"));

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Single(response.Content);
        Assert.Equal("pronto", ((TextBlock)response.Content[0]).Text);
        Assert.Equal(10, response.Usage.InputTokens);
    }

    [Fact]
    public async Task Throws_AnthropicApiException_On_NonSuccess_Status()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, """{"error":"invalid api key"}""");
        using var http = new HttpClient(handler);
        using var provider = new AnthropicChatProvider("sk-fake-key", http);

        var ex = await Assert.ThrowsAsync<AnthropicApiException>(() =>
            provider.CompleteAsync(new ChatRequest(
                [ChatMessage.User("oi")],
                Model: "claude-3-5-sonnet-latest")));

        Assert.Equal(401, ex.StatusCode);
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public void Constructor_Throws_On_Empty_ApiKey()
    {
        Assert.Throws<ArgumentException>(() => new AnthropicChatProvider(""));
        Assert.Throws<ArgumentException>(() => new AnthropicChatProvider("   "));
        Assert.Throws<ArgumentException>(() => new AnthropicChatProvider(null!));
    }
}
