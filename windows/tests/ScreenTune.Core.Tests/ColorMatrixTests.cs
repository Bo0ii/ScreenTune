using ScreenTune.Core;

namespace ScreenTune.Core.Tests;

// MAGCOLOREFFECT is row-vector: out = [r g b a 1] x M, so row i is the input channel
// and column j is the output channel.
public class ColorMatrixTests
{
    private static (double R, double G, double B) Apply(float[] m, double r, double g, double b)
    {
        double Out(int j) => r * m[0 * 5 + j] + g * m[1 * 5 + j] + b * m[2 * 5 + j] + m[4 * 5 + j];
        return (Out(0), Out(1), Out(2));
    }

    [Fact]
    public void HasTwentyFiveEntries()
    {
        Assert.Equal(25, ColorMatrix.Saturation(1).Length);
    }

    [Fact]
    public void OneIsIdentity()
    {
        var m = ColorMatrix.Saturation(1);
        for (var i = 0; i < 5; i++)
        for (var j = 0; j < 5; j++)
        {
            Assert.Equal(i == j ? 1f : 0f, m[i * 5 + j], 1e-6f);
        }
        Assert.Equal(ColorMatrix.Identity, m);
    }

    [Fact]
    public void ZeroIsGrayscale()
    {
        var (r, g, b) = Apply(ColorMatrix.Saturation(0), 1, 0, 0);
        Assert.Equal(r, g, 1e-6);
        Assert.Equal(g, b, 1e-6);
        Assert.Equal(ColorMatrix.LumaRed, r, 1e-6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(2.0)]
    public void GrayStaysGray(double amount)
    {
        var (r, g, b) = Apply(ColorMatrix.Saturation(amount), 0.4, 0.4, 0.4);
        Assert.Equal(0.4, r, 1e-5);
        Assert.Equal(0.4, g, 1e-5);
        Assert.Equal(0.4, b, 1e-5);
    }

    [Fact]
    public void BoostPushesPrimariesApart()
    {
        var (r, g, b) = Apply(ColorMatrix.Saturation(2), 1, 0, 0);
        Assert.True(r > 1);
        Assert.True(g < 0);
        Assert.True(b < 0);
    }

    [Fact]
    public void AlphaRowAndColumnStayIdentity()
    {
        var m = ColorMatrix.Saturation(0.3);
        Assert.Equal(1f, m[3 * 5 + 3]);
        Assert.Equal(1f, m[4 * 5 + 4]);
        for (var j = 0; j < 3; j++)
        {
            Assert.Equal(0f, m[3 * 5 + j]);
            Assert.Equal(0f, m[4 * 5 + j]);
            Assert.Equal(0f, m[j * 5 + 3]);
        }
    }
}
