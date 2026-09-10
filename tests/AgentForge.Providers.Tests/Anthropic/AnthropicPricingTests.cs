using AgentForge.Providers.Anthropic;
using Xunit;

namespace AgentForge.Providers.Tests.Anthropic;

public class AnthropicPricingTests
{
    [Fact]
    public void Known_Sonnet_Model_Has_Nonzero_Cost()
    {
        var cost = AnthropicPricing.Estimate("claude-3-5-sonnet-20241022", 1_000_000, 1_000_000);

        // 3.00 (input) + 15.00 (output) = 18.00
        Assert.Equal(18.00m, cost);
    }

    [Fact]
    public void Unknown_Model_Has_Zero_Cost()
    {
        var cost = AnthropicPricing.Estimate("model-que-nao-existe-2099", 1_000, 500);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Small_Usage_Rounds_To_Six_Decimals()
    {
        var cost = AnthropicPricing.Estimate("claude-3-5-haiku-latest", 100, 50);

        // 0.80 * 100/1M + 4.00 * 50/1M = 0.00008 + 0.0002 = 0.00028
        Assert.Equal(0.00028m, cost);
    }

    [Fact]
    public void IsKnownModel_Recognizes_Aliases()
    {
        Assert.True(AnthropicPricing.IsKnownModel("claude-3-5-sonnet-latest"));
        Assert.True(AnthropicPricing.IsKnownModel("Claude-3-5-Sonnet-Latest")); // case-insensitive
        Assert.False(AnthropicPricing.IsKnownModel("gpt-4"));
    }
}
