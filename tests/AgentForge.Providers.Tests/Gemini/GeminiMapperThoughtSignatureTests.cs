// Testes de round-trip da thoughtSignature entre response e request.
// Cobrem o contrato do ADR-005: modelos Gemini "com thinking" (2.5 Pro, 3.x, alias -latest)
// emitem thoughtSignature junto de cada functionCall, e exigem echo em toda request
// subsequente. Sem echo, 400 INVALID_ARGUMENT.

using System.Text.Json.Nodes;
using AgentForge.Core.Chat;
using AgentForge.Providers.Gemini;
using Xunit;

namespace AgentForge.Providers.Tests.Gemini;

public class GeminiMapperThoughtSignatureTests
{
    [Fact]
    public void MapResponse_Captures_ThoughtSignature_Into_ProviderMetadata()
    {
        var responseJson = JsonNode.Parse("""
            {
              "candidates": [{
                "content": {
                  "parts": [{
                    "functionCall": { "name": "search", "args": {"q": "mcp"} },
                    "thoughtSignature": "opaque-signature-abc123"
                  }]
                },
                "finishReason": "STOP"
              }],
              "usageMetadata": { "promptTokenCount": 10, "candidatesTokenCount": 5 }
            }
            """)!;

        var chatResponse = GeminiMapper.MapResponseFromJson(responseJson, "gemini-flash-lite-latest");

        var toolUse = Assert.IsType<ToolUseBlock>(Assert.Single(chatResponse.Content));
        Assert.Equal("search", toolUse.Name);
        Assert.NotNull(toolUse.ProviderMetadata);
        Assert.Equal("opaque-signature-abc123", toolUse.ProviderMetadata!["gemini.thoughtSignature"]);
    }

    [Fact]
    public void MapResponse_Sets_ProviderMetadata_Null_When_No_ThoughtSignature()
    {
        var responseJson = JsonNode.Parse("""
            {
              "candidates": [{
                "content": {
                  "parts": [{
                    "functionCall": { "name": "search", "args": {"q": "mcp"} }
                  }]
                },
                "finishReason": "STOP"
              }],
              "usageMetadata": { "promptTokenCount": 10, "candidatesTokenCount": 5 }
            }
            """)!;

        var chatResponse = GeminiMapper.MapResponseFromJson(responseJson, "gemini-flash-lite-latest");

        var toolUse = Assert.IsType<ToolUseBlock>(Assert.Single(chatResponse.Content));
        Assert.Null(toolUse.ProviderMetadata);
    }

    [Fact]
    public void MapRequest_Echoes_ThoughtSignature_From_ProviderMetadata()
    {
        var metadata = new Dictionary<string, string>
        {
            ["gemini.thoughtSignature"] = "opaque-signature-xyz789",
        };

        var request = new ChatRequest(
            Messages: [
                ChatMessage.User("busca"),
                ChatMessage.Assistant([
                    new ToolUseBlock(
                        Id: "tu_1",
                        Name: "search",
                        InputJson: """{"q":"mcp"}""",
                        ProviderMetadata: metadata),
                ]),
                ChatMessage.Tool([new ToolResultBlock("tu_1", """{"hits":3}""")]),
            ],
            Model: "gemini-flash-lite-latest");

        var json = GeminiMapper.MapRequestToJson(request);
        var contents = json["contents"]!.AsArray();

        // A msg do assistant vira role=model com um part que carrega functionCall + thoughtSignature.
        var assistantMsg = contents[1]!;
        Assert.Equal("model", assistantMsg["role"]?.GetValue<string>());

        var part = assistantMsg["parts"]![0]!;
        Assert.Equal("search", part["functionCall"]!["name"]?.GetValue<string>());
        Assert.Equal("opaque-signature-xyz789", part["thoughtSignature"]?.GetValue<string>());
    }

    [Fact]
    public void MapRequest_Omits_ThoughtSignature_When_ProviderMetadata_Missing()
    {
        // Modelos sem thinking não emitem signature; o ToolUseBlock vem sem metadata.
        // Request não deve conter thoughtSignature — caso contrário o modelo pode reclamar
        // por receber signature "inventada".
        var request = new ChatRequest(
            Messages: [
                ChatMessage.User("busca"),
                ChatMessage.Assistant([
                    new ToolUseBlock(Id: "tu_1", Name: "search", InputJson: "{}"),
                ]),
                ChatMessage.Tool([new ToolResultBlock("tu_1", "{}")]),
            ],
            Model: "gemini-2.0-flash");

        var json = GeminiMapper.MapRequestToJson(request);
        var part = json["contents"]![1]!["parts"]![0]!;

        Assert.NotNull(part["functionCall"]);
        Assert.Null(part["thoughtSignature"]);
    }
}
