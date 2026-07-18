using Xunit;

namespace Wallflow.Tests;

public class SpeedPaliersTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(1.75, 2.0)]
    [InlineData(1.74, 1.5)]
    [InlineData(0.1, 0.5)]   // sous la plage
    [InlineData(9.0, 2.0)]  // au-dessus de la plage
    [InlineData(0.5, 0.5)]  // palier exact
    [InlineData(1.5, 1.5)]  // palier exact
    [InlineData(2.0, 2.0)]  // palier exact
    public void Nearest_ReturnsClosestPalier(double input, double expected)
    {
        Assert.Equal(expected, SpeedPaliers.Nearest(input));
    }
}
