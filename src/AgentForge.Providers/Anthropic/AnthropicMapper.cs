using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Core.Chat;

namespace AgentForge.Providers.Anthropic;

/// <summary>
/// Mapeia entre os tipos do Core e o JSON esperado/produzido pela Messages API da Anthropic.
/// Ponto-chave: <see cref="ChatRole.System"/> vira o campo <c>system</c> no body raiz
/// (Anthropic não aceita role="system" no array de messages), e <see cref="ChatRole.Tool"/>
/// vira role="user" com blocos <c>tool_result</c>.
/// </summary>
internal static class AnthropicMapper
{
    public static JsonObject MapRequestToJson(ChatRequest request)
    {
        var root = new JsonObject
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxTokens,
        };

        var systemBuilder = new StringBuilder();
        var apiMessages = new JsonArray();

        foreach (var msg in request.Messages)
        {
            if (msg.Role == ChatRole.System)
            {
                foreach (var text in msg.Content.OfType<TextBlock>())
                {
                    if (systemBuilder.Length > 0)
                    {
                        systemBuilder.Append('\n');
                    }

                    systemBuilder.Append(text.Text);
                }
            }
            else
            {
                apiMessages.Add(MapMessage(msg));
            }
        }

        if (systemBuilder.Length > 0)
        {
            root["system"] = systemBuilder.ToString();
        }

        root["messages"] = apiMessages;

        if (request.Tools is { Count: > 0 })
        {
            var toolsArr = new JsonArray();
            foreach (var tool in request.Tools)
            {
                toolsArr.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["input_schema"] = JsonNode.Parse(tool.InputSchemaJson),
                });
            }

            root["tools"] = toolsArr;
        }

        if (request.Temperature is not null)
        {
            root["temperature"] = request.Temperature;
        }

        if (request.StopSequences is { Count: > 0 })
        {
            var stopArr = new JsonArray();
            foreach (var s in request.StopSequences)
            {
                stopArr.Add(s);
            }

            root["stop_sequences"] = stopArr;
        }

        return root;
    }

    private static JsonObject MapMessage(ChatMessage msg)
    {
        var role = msg.Role switch
        {
            ChatRole.User => "user",
            ChatRole.Assistant => "assistant",
            ChatRole.Tool => "user",
            ChatRole.System => throw new InvalidOperationException(
                "System messages are hoisted to the 'system' field — should not reach MapMessage."),
            _ => throw new InvalidOperationException($"Unexpected role: {msg.Role}"),
        };

        var content = new JsonArray();
        foreach (var block in msg.Content)
        {
            content.Add(MapContentBlock(block));
        }

        return new JsonObject
        {
            ["role"] = role,
            ["content"] = content,
        };
    }

    private static JsonObject MapContentBlock(ContentBlock block) => block switch
    {
        TextBlock t => new JsonObject
        {
            ["type"] = "text",
            ["text"] = t.Text,
        },
        ToolUseBlock tu => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = tu.Id,
            ["name"] = tu.Name,
            ["input"] = JsonNode.Parse(tu.InputJson),
        },
        ToolResultBlock tr => new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = tr.ToolUseId,
            ["content"] = tr.ResultJson,
            ["is_error"] = tr.IsError,
        },
        _ => throw new InvalidOperationException($"Unknown content block: {block.GetType()}"),
    };

    public static ChatResponse MapResponseFromJson(JsonNode json)
    {
        var content = new List<ContentBlock>();
        var contentArr = json["content"]?.AsArray();

        if (contentArr is not null)
        {
            foreach (var blockNode in contentArr)
            {
                if (blockNode is null)
                {
                    continue;
                }

                var type = blockNode["type"]?.GetValue<string>();
                switch (type)
                {
                    case "text":
                        content.Add(new TextBlock(blockNode["text"]?.GetValue<string>() ?? string.Empty));
                        break;
                    case "tool_use":
                        content.Add(new ToolUseBlock(
                            Id: blockNode["id"]?.GetValue<string>() ?? string.Empty,
                            Name: blockNode["name"]?.GetValue<string>() ?? string.Empty,
                            InputJson: blockNode["input"]?.ToJsonString() ?? "{}"));
                        break;
                    // outros tipos futuros da Anthropic (thinking, redacted_thinking) são ignorados por ora
                }
            }
        }

        var stopReason = json["stop_reason"]?.GetValue<string>() switch
        {
            "max_tokens" => StopReason.MaxTokens,
            "tool_use" => StopReason.ToolUse,
            "stop_sequence" => StopReason.StopSequence,
            _ => StopReason.EndTurn,
        };

        var usageNode = json["usage"];
        var inputTokens = usageNode?["input_tokens"]?.GetValue<int>() ?? 0;
        var outputTokens = usageNode?["output_tokens"]?.GetValue<int>() ?? 0;
        var model = json["model"]?.GetValue<string>() ?? "unknown";
        var cost = AnthropicPricing.Estimate(model, inputTokens, outputTokens);

        return new ChatResponse(
            Content: content,
            StopReason: stopReason,
            Usage: new UsageStats(inputTokens, outputTokens, cost),
            Model: model);
    }
}
