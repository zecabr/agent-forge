using System.Text.Json.Nodes;
using AgentForge.Core.Chat;
using AgentForge.Core.Tools;
using AgentForge.Providers.Anthropic;
using Xunit;

namespace AgentForge.Providers.Tests.Anthropic;

public class AnthropicMapperTests
{
    [Fact]
    public void MapRequest_Hoists_System_Message_To_Top_Level_Field()
    {
        var request = new ChatRequest(
            Messages: [
                ChatMessage.System("você é um assistente"),
                ChatMessage.User("olá"),
            ],
            Model: "claude-3-5-sonnet-latest");

        var json = AnthropicMapper.MapRequestToJson(request);

        Assert.Equal("você é um assistente", json["system"]?.GetValue<string>());
        var messages = json["messages"]!.AsArray();
        Assert.Single(messages);
        Assert.Equal("user", messages[0]!["role"]?.GetValue<string>());
    }

    [Fact]
    public void MapRequest_Tool_Role_Becomes_User_With_ToolResult_Block()
    {
        var request = new ChatRequest(
            Messages: [
                ChatMessage.User("busca"),
                ChatMessage.Assistant([new ToolUseBlock("tu_1", "search", """{"q":"x"}""")]),
                ChatMessage.Tool([new ToolResultBlock("tu_1", """{"hits":3}""")]),
            ],
            Model: "claude-3-5-sonnet-latest");

        var json = AnthropicMapper.MapRequestToJson(request);
        var messages = json["messages"]!.AsArray();

        Assert.Equal(3, messages.Count);
        var toolResultMsg = messages[2]!;
        Assert.Equal("user", toolResultMsg["role"]?.GetValue<string>());
        var content = toolResultMsg["content"]!.AsArray();
        Assert.Equal("tool_result", content[0]!["type"]?.GetValue<string>());
        Assert.Equal("tu_1", content[0]!["tool_use_id"]?.GetValue<string>());
    }

    [Fact]
    public void MapRequest_Serializes_Tools_With_Parsed_Schema()
    {
        var request = new ChatRequest(
            Messages: [ChatMessage.User("hi")],
            Model: "claude-3-5-sonnet-latest",
            Tools: [new ToolDefinition("search", "web search", """{"type":"object","properties":{"q":{"type":"string"}}}""")]);

        var json = AnthropicMapper.MapRequestToJson(request);
        var tools = json["tools"]!.AsArray();

        Assert.Single(tools);
        var tool = tools[0]!;
        Assert.Equal("search", tool["name"]?.GetValue<string>());
        Assert.Equal("object", tool["input_schema"]!["type"]?.GetValue<string>());
    }

    [Fact]
    public void MapResponse_Parses_Simple_Text_Response()
    {
        var json = JsonNode.Parse("""
            {
              "id": "msg_1",
              "model": "claude-3-5-sonnet-20241022",
              "content": [{"type":"text","text":"olá!"}],
              "stop_reason": "end_turn",
              "usage": {"input_tokens": 10, "output_tokens": 5}
            }
            """)!;

        var response = AnthropicMapper.MapResponseFromJson(json);

        Assert.Single(response.Content);
        Assert.IsType<TextBlock>(response.Content[0]);
        Assert.Equal("olá!", ((TextBlock)response.Content[0]).Text);
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(10, response.Usage.InputTokens);
        Assert.Equal(5, response.Usage.OutputTokens);
        Assert.True(response.Usage.CostUsd > 0m);
        Assert.Equal("claude-3-5-sonnet-20241022", response.Model);
    }

    [Fact]
    public void MapResponse_Parses_ToolUse_Response()
    {
        var json = JsonNode.Parse("""
            {
              "id": "msg_1",
              "model": "claude-3-5-sonnet-latest",
              "content": [
                {"type":"text","text":"vou buscar"},
                {"type":"tool_use","id":"tu_1","name":"search","input":{"q":"mcp"}}
              ],
              "stop_reason": "tool_use",
              "usage": {"input_tokens": 20, "output_tokens": 15}
            }
            """)!;

        var response = AnthropicMapper.MapResponseFromJson(json);

        Assert.Equal(2, response.Content.Count);
        Assert.IsType<TextBlock>(response.Content[0]);
        var toolUse = Assert.IsType<ToolUseBlock>(response.Content[1]);
        Assert.Equal("tu_1", toolUse.Id);
        Assert.Equal("search", toolUse.Name);
        Assert.Contains("mcp", toolUse.InputJson);
        Assert.Equal(StopReason.ToolUse, response.StopReason);
    }
}
