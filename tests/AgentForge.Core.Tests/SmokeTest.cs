using Xunit;

namespace AgentForge.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_Compiles_And_Test_Runner_Works()
    {
        // Sentinela — se cai, o pipeline de teste está quebrado antes mesmo do primeiro caso real.
        Assert.True(true);
    }
}
