using System.Text;
using System.Text.Json.Nodes;
using AgentForge.Core.Chat;

namespace AgentForge.Providers.Gemini;

/// <summary>
/// Mapeia entre os tipos do Core e o JSON esperado/produzido pela Gemini API.
/// <para>
/// Diferenças-chave em relação à Anthropic:
/// <list type="bullet">
///   <item>Role names: <c>user</c> e <c>model</c> (não <c>assistant</c>). Tool results também
///   viram role <c>user</c> — Gemini não tem role dedicado a tool.</item>
///   <item>System prompt vai em campo separado <c>systemInstruction</c>, não no array.</item>
///   <item>Tool call é <c>functionCall</c> com <c>args</c> como objeto (não string).</item>
///   <item>Tool result é <c>functionResponse</c> — precisa do <em>name</em> da função, não do id.
///   Correlacionamos via lookup pela última mensagem Assistant que emitiu o tool_use.</item>
/// </list>
/// </para>
/// </summary>
internal static class GeminiMapper
{
    public static JsonObject MapRequestToJson(ChatRequest request)
    {
        var root = new JsonObject();

        // Precompute tool_id -> tool_name lookup pra mapear ToolResultBlock
        var toolNameById = new Dictionary<string, string>();
        foreach (var msg in request.Messages.Where(m => m.Role == ChatRole.Assistant))
        {
            foreach (var use in msg.Content.OfType<ToolUseBlock>())
            {
                toolNameById[use.Id] = use.Name;
            }
        }

        var systemBuilder = new StringBuilder();
        var contents = new JsonArray();

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
                contents.Add(MapContent(msg, toolNameById));
            }
        }

        if (systemBuilder.Length > 0)
        {
            root["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray { new JsonObject { ["text"] = systemBuilder.ToString() } },
            };
        }

        root["contents"] = contents;

        if (request.Tools is { Count: > 0 })
        {
            var functionDeclarations = new JsonArray();
            foreach (var tool in request.Tools)
            {
                functionDeclarations.Add(new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.InputSchemaJson),
                });
            }

            root["tools"] = new JsonArray { new JsonObject { ["functionDeclarations"] = functionDeclarations } };
        }

        var generationConfig = new JsonObject
        {
            ["maxOutputTokens"] = request.MaxTokens,
        };

        if (request.Temperature is not null)
        {
            generationConfig["temperature"] = request.Temperature;
        }

        if (request.StopSequences is { Count: > 0 })
        {
            var stopArr = new JsonArray();
            foreach (var s in request.StopSequences)
            {
                stopArr.Add(s);
            }

            generationConfig["stopSequences"] = stopArr;
        }

        root["generationConfig"] = generationConfig;

        return root;
    }

    private static JsonObject MapContent(ChatMessage msg, IReadOnlyDictionary<string, string> toolNameById)
    {
        var role = msg.Role switch
        {
            ChatRole.User => "user",
            ChatRole.Assistant => "model",
            ChatRole.Tool => "user",
            ChatRole.System => throw new InvalidOperationException(
                "System messages are hoisted to systemInstruction — should not reach MapContent."),
            _ => throw new InvalidOperationException($"Unexpected role: {msg.Role}"),
        };

        var parts = new JsonArray();
        foreach (var block in msg.Content)
        {
            parts.Add(MapPart(block, toolNameById));
        }

        return new JsonObject
        {
            ["role"] = role,
            ["parts"] = parts,
        };
    }

    private static JsonObject MapPart(ContentBlock block, IReadOnlyDictionary<string, string> toolNameById)
    {
        return block switch
        {
            TextBlock t => new JsonObject { ["text"] = t.Text },

            ToolUseBlock tu => new JsonObject
            {
                ["functionCall"] = new JsonObject
                {
                    ["name"] = tu.Name,
                    ["args"] = JsonNode.Parse(tu.InputJson),
                },
            },

            ToolResultBlock tr => new JsonObject
            {
                ["functionResponse"] = new JsonObject
                {
                    ["name"] = toolNameById.TryGetValue(tr.ToolUseId, out var name) ? name : tr.ToolUseId,
                    ["response"] = WrapToolResponse(tr.ResultJson, tr.IsError),
                },
            },

            _ => throw new InvalidOperationException($"Unknown content block: {block.GetType()}"),
        };
    }

    private static JsonObject WrapToolResponse(string resultJson, bool isError)
    {
        // Gemini espera 'response' como objeto. Se o result é JSON válido de objeto, usa direto;
        // senão embrulha em {"content": "..."} pra passar como string.
        JsonNode? parsed = null;
        try
        {
            parsed = JsonNode.Parse(resultJson);
        }
        catch
        {
            // não é JSON — trata como string
        }

        var response = parsed is JsonObject obj
            ? obj
            : new JsonObject { ["content"] = resultJson };

        if (isError)
        {
            response["isError"] = true;
        }

        return response;
    }

    public static ChatResponse MapResponseFromJson(JsonNode json, string requestedModel)
    {
        var content = new List<ContentBlock>();
        var candidate = json["candidates"]?.AsArray().FirstOrDefault();
        var contentArr = candidate?["content"]?["parts"]?.AsArray();
        var hasFunctionCall = false;

        if (contentArr is not null)
        {
            foreach (var partNode in contentArr)
            {
                if (partNode is null)
                {
                    continue;
                }

                if (partNode["text"]?.GetValue<string>() is string text)
                {
                    content.Add(new TextBlock(text));
                }
                else if (partNode["functionCall"] is JsonNode fc)
                {
                    hasFunctionCall = true;
                    content.Add(new ToolUseBlock(
                        Id: fc["name"]?.GetValue<string>() ?? "unknown",  // Gemini não devolve id — usa name como proxy
                        Name: fc["name"]?.GetValue<string>() ?? "unknown",
                        InputJson: fc["args"]?.ToJsonString() ?? "{}"));
                }
            }
        }

        // Gemini usa "STOP" como finishReason mesmo em tool call — detectamos pelo conteúdo
        var finishReason = candidate?["finishReason"]?.GetValue<string>();
        var stopReason = (hasFunctionCall, finishReason) switch
        {
            (true, _) => StopReason.ToolUse,
            (_, "MAX_TOKENS") => StopReason.MaxTokens,
            (_, "STOP_SEQUENCE") => StopReason.StopSequence,
            _ => StopReason.EndTurn,
        };

        var usage = json["usageMetadata"];
        var inputTokens = usage?["promptTokenCount"]?.GetValue<int>() ?? 0;
        var outputTokens = usage?["candidatesTokenCount"]?.GetValue<int>() ?? 0;
        var cost = GeminiPricing.Estimate(requestedModel, inputTokens, outputTokens);

        return new ChatResponse(
            Content: content,
            StopReason: stopReason,
            Usage: new UsageStats(inputTokens, outputTokens, cost),
            Model: requestedModel);
    }
}
