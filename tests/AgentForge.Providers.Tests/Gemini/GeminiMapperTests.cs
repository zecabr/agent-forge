using System.Text.Json.Nodes;
using AgentForge.Core.Chat;
using AgentForge.Core.Tools;
using AgentForge.Providers.Gemini;
using Xunit;

namespace AgentForge.Providers.Tests.Gemini;

public class GeminiMapperTests
{
    [Fact]
    public void MapRequest_Hoists_System_Message_To_SystemInstruction()
    {
        var request = new ChatRequest(
            Messages: [
                ChatMessage.System("você é um assistente"),
                ChatMessage.User("olá"),
            ],
            Model: "gemini-2.0-flash");

        var json = GeminiMapper.MapRequestToJson(request);

        var sysText = json["systemInstruction"]?["parts"]?[0]?["text"]?.GetValue<string>();
        Assert.Equal("você é um assistente", sysText);

        var contents = json["contents"]!.AsArray();
        Assert.Single(contents);
        Assert.Equal("user", contents[0]!["role"]?.GetValue<string>());
    }

    [Fact]
    public void MapRequest_Assistant_Becomes_Role_Model()
    {
        var request = new ChatRequest(
            Messages: [
                ChatMessage.User("oi"),
                ChatMessage.Assistant("olá"),
            ],
            Model: "gemini-2.0-flash");

        var json = GeminiMapper.MapRequestToJson(request);
        var contents = json["contents"]!.AsArray();

        Assert.Equal("model", contents[1]!["role"]?.GetValue<string>());
    }

    [Fact]
    public void MapRequest_Tool_Result_Becomes_User_With_FunctionResponse_Using_Tool_Name()
    {
        var request = new ChatRequest(
            Messages: [
                ChatMessage.User("busca"),
                ChatMessage.Assistant([new ToolUseBlock("tu_1", "search", """{"q":"mcp"}""")]),
                ChatMessage.Tool([new ToolResultBlock("tu_1", """{"hits":3}""")]),
            ],
            Model: "gemini-2.0-flash");

        var json = GeminiMapper.MapRequestToJson(request);
        var contents = json["contents"]!.AsArray();

        Assert.Equal(3, contents.Count);
        var toolResultMsg = contents[2]!;
        Assert.Equal("user", toolResultMsg["role"]?.GetValue<string>());
        var funcResp = toolResultMsg["parts"]![0]!["functionResponse"]!;
        Assert.Equal("search", funcResp["name"]?.GetValue<string>()); // resolvido pelo lookup
        Assert.Equal(3, funcResp["response"]!["hits"]?.GetValue<int>());
    }

    [Fact]
    public void MapRequest_Tool_As_FunctionDeclarations_Under_Tools_Array()
    {
        var request = new ChatRequest(
            Messages: [ChatMessage.User("hi")],
            Model: "gemini-2.0-flash",
            Tools: [new ToolDefinition("search", "web search", """{"type":"object"}""")]);

        var json = GeminiMapper.MapRequestToJson(request);
        var funcDecls = json["tools"]![0]!["functionDeclarations"]!.AsArray();

        Assert.Single(funcDecls);
        Assert.Equal("search", funcDecls[0]!["name"]?.GetValue<string>());
    }

    [Fact]
    public void MapResponse_Parses_Text_Response()
    {
        var json = JsonNode.Parse("""
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [{"text": "olá!"}]
                },
                "finishReason": "STOP"
              }],
              "usageMetadata": {
                "promptTokenCount": 10,
                "candidatesTokenCount": 5,
                "totalTokenCount": 15
              }
            }
            """)!;

        var response = GeminiMapper.MapResponseFromJson(json, "gemini-2.0-flash");

        Assert.Single(response.Content);
        Assert.Equal("olá!", ((TextBlock)response.Content[0]).Text);
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(10, response.Usage.InputTokens);
        Assert.Equal(5, response.Usage.OutputTokens);
    }

    [Fact]
    public void MapResponse_Detects_ToolUse_Via_FunctionCall_Part_Even_With_STOP_Reason()
    {
        var json = JsonNode.Parse("""
            {
              "candidates": [{
                "content": {
                  "role": "model",
                  "parts": [
                    {"text": "vou buscar"},
                    {"functionCall": {"name": "search", "args": {"q": "mcp"}}}
                  ]
                },
                "finishReason": "STOP"
              }],
              "usageMetadata": {"promptTokenCount": 20, "candidatesTokenCount": 15}
            }
            """)!;

        var response = GeminiMapper.MapResponseFromJson(json, "gemini-2.0-flash");

        Assert.Equal(2, response.Content.Count);
        Assert.Equal(StopReason.ToolUse, response.StopReason);   // detectado pelo conteúdo, não pelo finishReason
        var toolUse = Assert.IsType<ToolUseBlock>(response.Content[1]);
        Assert.Equal("search", toolUse.Name);
        Assert.Contains("mcp", toolUse.InputJson);
    }
}
