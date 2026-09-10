using AgentForge.Core.Chat;
using Xunit;

namespace AgentForge.Core.Tests.Chat;

public class ChatMessageTests
{
    [Fact]
    public void User_Factory_Creates_User_Message_With_Single_TextBlock()
    {
        var msg = ChatMessage.User("olá");

        Assert.Equal(ChatRole.User, msg.Role);
        Assert.Single(msg.Content);
        var text = Assert.IsType<TextBlock>(msg.Content[0]);
        Assert.Equal("olá", text.Text);
    }

    [Fact]
    public void System_Factory_Creates_System_Role()
    {
        var msg = ChatMessage.System("você é um assistente");

        Assert.Equal(ChatRole.System, msg.Role);
        Assert.IsType<TextBlock>(msg.Content[0]);
    }

    [Fact]
    public void Assistant_With_Blocks_Preserves_Order()
    {
        var blocks = new ContentBlock[]
        {
            new TextBlock("vou usar uma ferramenta"),
            new ToolUseBlock("tu_1", "search", """{"q":"mcp"}"""),
        };

        var msg = ChatMessage.Assistant(blocks);

        Assert.Equal(ChatRole.Assistant, msg.Role);
        Assert.Equal(2, msg.Content.Count);
        Assert.IsType<TextBlock>(msg.Content[0]);
        var toolUse = Assert.IsType<ToolUseBlock>(msg.Content[1]);
        Assert.Equal("tu_1", toolUse.Id);
        Assert.Equal("search", toolUse.Name);
    }

    [Fact]
    public void Tool_Factory_Wraps_Results_With_Tool_Role()
    {
        var results = new[]
        {
            new ToolResultBlock("tu_1", """{"hits":3}"""),
        };

        var msg = ChatMessage.Tool(results);

        Assert.Equal(ChatRole.Tool, msg.Role);
        Assert.Single(msg.Content);
        Assert.IsType<ToolResultBlock>(msg.Content[0]);
    }

    [Fact]
    public void ToolResultBlock_Marks_Error_When_Requested()
    {
        var block = new ToolResultBlock("tu_1", "timeout", IsError: true);

        Assert.True(block.IsError);
    }
}
