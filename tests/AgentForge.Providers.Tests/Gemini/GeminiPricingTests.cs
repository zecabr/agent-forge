using AgentForge.Providers.Gemini;
using Xunit;

namespace AgentForge.Providers.Tests.Gemini;

public class GeminiPricingTests
{
    [Fact]
    public void Flash_2_Point_0_Has_Nonzero_Cost()
    {
        var cost = GeminiPricing.Estimate("gemini-2.0-flash", 1_000_000, 1_000_000);

        Assert.Equal(0.50m, cost);
    }

    [Fact]
    public void Flash_Exp_Is_Free()
    {
        var cost = GeminiPricing.Estimate("gemini-2.0-flash-exp", 1_000_000, 1_000_000);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Pro_1_Point_5_Uses_Pro_Rates()
    {
        var cost = GeminiPricing.Estimate("gemini-1.5-pro", 1_000_000, 1_000_000);

        Assert.Equal(6.25m, cost);
    }

    [Fact]
    public void Unknown_Model_Returns_Zero()
    {
        var cost = GeminiPricing.Estimate("model-que-nao-existe", 1_000, 500);

        Assert.Equal(0m, cost);
    }

    [Fact]
    public void Prefix_Match_Handles_Versioned_Model_Names()
    {
        var cost = GeminiPricing.Estimate("gemini-2.0-flash-001", 1_000_000, 1_000_000);

        // deve match gemini-2.0-flash mesmo com sufixo -001
        Assert.Equal(0.50m, cost);
    }

    [Fact]
    public void More_Specific_Prefix_Wins()
    {
        // flash-lite deve match antes de flash (ordem no array)
        var liteCost = GeminiPricing.Estimate("gemini-2.0-flash-lite", 1_000_000, 1_000_000);
        var flashCost = GeminiPricing.Estimate("gemini-2.0-flash", 1_000_000, 1_000_000);

        Assert.NotEqual(liteCost, flashCost);
        Assert.Equal(0.375m, liteCost);   // 0.075 + 0.30
        Assert.Equal(0.50m, flashCost);   // 0.10 + 0.40
    }
}
