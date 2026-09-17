using System;
using TabsSkinColorMod.Core;
using Xunit;

namespace TabsSkinColorMod.Core.Tests
{
    public class RgbaColorTests
    {
        [Theory]
        [InlineData("#C7A17A", 0xC7, 0xA1, 0x7A, 255)]
        [InlineData("C7A17A", 0xC7, 0xA1, 0x7A, 255)]
        [InlineData("#c7a17a", 0xC7, 0xA1, 0x7A, 255)]
        [InlineData("#8C5A3CFF", 0x8C, 0x5A, 0x3C, 0xFF)]
        [InlineData("8C5A3C80", 0x8C, 0x5A, 0x3C, 0x80)]
        public void TryParse_AcceptsValidHex(string text, byte r, byte g, byte b, byte a)
        {
            var ok = RgbaColor.TryParse(text, out var c);
            Assert.True(ok);
            Assert.Equal(new RgbaColor(r, g, b, a), c);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("#123")]
        [InlineData("#12345")]
        [InlineData("#1234567")]
        [InlineData("#GGHHII")]
        [InlineData("red")]
        public void TryParse_RejectsInvalidInput(string text)
        {
            Assert.False(RgbaColor.TryParse(text, out _));
        }

        [Fact]
        public void ToHex_RoundTrips()
        {
            var c = new RgbaColor(0x12, 0x34, 0xAB, 0xCD);
            Assert.Equal("#1234AB", c.ToHex());
            Assert.Equal("#1234ABCD", c.ToHex(includeAlpha: true));

            RgbaColor.TryParse(c.ToHex(true), out var parsed);
            Assert.Equal(c, parsed);
        }

        [Fact]
        public void ParseOrDefault_FallsBack()
        {
            var fallback = new RgbaColor(1, 2, 3);
            Assert.Equal(fallback, RgbaColor.ParseOrDefault("nope", fallback));
            Assert.Equal(new RgbaColor(0xFF, 0x00, 0x00, 255), RgbaColor.ParseOrDefault("#FF0000", fallback));
        }

        [Fact]
        public void Equality_Works()
        {
            Assert.Equal(new RgbaColor(1, 2, 3), new RgbaColor(1, 2, 3));
            Assert.NotEqual(new RgbaColor(1, 2, 3), new RgbaColor(1, 2, 3, 4));
            Assert.True(new RgbaColor(9, 9, 9) == new RgbaColor(9, 9, 9));
            Assert.True(new RgbaColor(9, 9, 9) != new RgbaColor(9, 9, 8));
        }
    }

    public class ColorMathTests
    {
        [Fact]
        public void HsvToRgb_PrimaryColors()
        {
            Assert.Equal(new RgbaColor(255, 0, 0), ColorMath.HsvToRgb(0, 1, 1));
            Assert.Equal(new RgbaColor(0, 255, 0), ColorMath.HsvToRgb(1.0 / 3.0, 1, 1));
            Assert.Equal(new RgbaColor(0, 0, 255), ColorMath.HsvToRgb(2.0 / 3.0, 1, 1));
        }

        [Fact]
        public void HsvToRgb_BlackAndWhite()
        {
            Assert.Equal(new RgbaColor(0, 0, 0), ColorMath.HsvToRgb(0, 0, 0));
            // Saturation 0 always yields white regardless of hue.
            Assert.Equal(new RgbaColor(255, 255, 255), ColorMath.HsvToRgb(0.7, 0, 1));
            // A saturated case: h=2/3 (blue), s=0.5, v=1 -> (128, 128, 255).
            Assert.Equal(new RgbaColor(128, 128, 255), ColorMath.HsvToRgb(2.0 / 3.0, 0.5, 1));
        }

        [Fact]
        public void HsvToRgb_ClampsOutOfRangeInputs()
        {
            Assert.Equal(new RgbaColor(0, 0, 0), ColorMath.HsvToRgb(-5, 2, -1));
            Assert.Equal(new RgbaColor(255, 255, 255), ColorMath.HsvToRgb(9, 0, 5));
        }
    }
}
