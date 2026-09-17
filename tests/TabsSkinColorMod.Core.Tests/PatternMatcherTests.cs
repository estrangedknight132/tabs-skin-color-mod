using System.Linq;
using TabsSkinColorMod.Core;
using Xunit;

namespace TabsSkinColorMod.Core.Tests
{
    public class PatternMatcherTests
    {
        [Fact]
        public void Substring_Matches_CaseInsensitive()
        {
            var m = new PatternMatcher("skin");
            Assert.True(m.IsMatch("Skin_Material"));
            Assert.True(m.IsMatch("wobbler_skin"));
            Assert.False(m.IsMatch("Cloth_Tunic"));
        }

        [Fact]
        public void Wildcard_Star_Semantics()
        {
            var m = new PatternMatcher("wob*");
            Assert.True(m.IsMatch("Wobbler_Body"));
            Assert.False(m.IsMatch("Giant_Body"));

            var m2 = new PatternMatcher("*body*");
            Assert.True(m2.IsMatch("Unit_Body_MAT"));
            Assert.True(m2.IsMatch("body"));
            Assert.False(m2.IsMatch("Unit_Head_MAT"));
        }

        [Fact]
        public void Negative_Patterns_Reject()
        {
            var m = new PatternMatcher("skin, -team, -Team*");
            Assert.False(m.IsMatch("TeamColor_Skin"));   // rejected by -team
            Assert.False(m.IsMatch("Skin_Teamsheet"));   // rejected by -Team*
            Assert.True(m.IsMatch("Body_Skin"));
        }

        [Fact]
        public void Empty_List_Matches_Nothing()
        {
            var m = new PatternMatcher("");
            Assert.False(m.IsMatch("Skin"));
            Assert.False(m.HasAnyPositive);
        }

        [Fact]
        public void Reload_ReplacesPatterns()
        {
            var m = new PatternMatcher("skin");
            Assert.True(m.IsMatch("Skin"));
            m.Reload("cloth");
            Assert.False(m.IsMatch("Skin"));
            Assert.True(m.IsMatch("Cloth"));
        }

        [Theory]
        [InlineData("wob*", "wobbler", true)]
        [InlineData("wob*", "wob", true)]
        [InlineData("wob*", "wo", false)]
        [InlineData("*", "anything", true)]
        [InlineData("a*b*c", "aXbYc", true)]
        [InlineData("a*b*c", "aXbY", false)]
        [InlineData("skin", "my_skin_01", true)]
        [InlineData("skin", "ski", false)]
        public void WildcardMatches_Cases(string pattern, string text, bool expected)
        {
            Assert.Equal(expected, PatternMatcher.WildcardMatches(pattern, text));
        }
    }
}
