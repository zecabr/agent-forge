using System.Net;
using AgentForge.Core.Chat;
using AgentForge.Providers.Gemini;
using AgentForge.Providers.Tests.Fakes;
using Xunit;

namespace AgentForge.Providers.Tests.Gemini;

public class GeminiChatProviderTests
{
    private const string OkResponse = """
        {
          "candidates": [{
            "content": {
              "role": "model",
              "parts": [{"text": "pronto"}]
            },
            "finishReason": "STOP"
          }],
          "usageMetadata": {"promptTokenCount": 10, "candidatesTokenCount": 5}
        }
        """;

    [Fact]
    public async Task Sends_ApiKey_Header()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new GeminiChatProvider("fake-google-key", http);

        _ = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "gemini-2.0-flash"));

        var req = handler.Calls[0].Request;
        Assert.Equal("fake-google-key", req.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task Posts_To_Model_Specific_GenerateContent_Endpoint()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new GeminiChatProvider("fake-google-key", http);

        _ = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "gemini-2.0-flash"));

        var req = handler.Calls[0].Request;
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Contains("/v1beta/models/gemini-2.0-flash:generateContent", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Uses_Custom_BaseUri_When_Provided()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new GeminiChatProvider(
            "fake-key", http, baseUri: "https://proxy.internal.example.com");

        _ = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "gemini-2.0-flash"));

        var req = handler.Calls[0].Request;
        Assert.Contains("proxy.internal.example.com", req.RequestUri!.ToString());
    }

    [Fact]
    public async Task Returns_Mapped_ChatResponse()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, OkResponse);
        using var http = new HttpClient(handler);
        using var provider = new GeminiChatProvider("fake-key", http);

        var response = await provider.CompleteAsync(new ChatRequest(
            [ChatMessage.User("oi")],
            Model: "gemini-2.0-flash"));

        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Single(response.Content);
        Assert.Equal("pronto", ((TextBlock)response.Content[0]).Text);
        Assert.Equal(10, response.Usage.InputTokens);
    }

    [Fact]
    public async Task Throws_GeminiApiException_On_NonSuccess_Status()
    {
        var handler = new RecordingHandler(HttpStatusCode.Unauthorized, """{"error":{"message":"invalid api key"}}""");
        using var http = new HttpClient(handler);
        using var provider = new GeminiChatProvider("fake-key", http);

        var ex = await Assert.ThrowsAsync<GeminiApiException>(() =>
            provider.CompleteAsync(new ChatRequest(
                [ChatMessage.User("oi")],
                Model: "gemini-2.0-flash")));

        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public void Constructor_Rejects_Empty_ApiKey()
    {
        Assert.Throws<ArgumentException>(() => new GeminiChatProvider(""));
        Assert.Throws<ArgumentException>(() => new GeminiChatProvider("   "));
        Assert.Throws<ArgumentException>(() => new GeminiChatProvider(null!));
    }
}
