using TabsSkinColorMod.Core;
using Xunit;

namespace TabsSkinColorMod.Core.Tests
{
    public class ColorStoreTests
    {
        [Fact]
        public void SetColor_ThenGet_Works()
        {
            var store = new ColorStore();
            store.SetColor("Huskarl", "#8C5A3C");
            Assert.True(store.TryGetColor("Huskarl", out var hex));
            Assert.Equal("#8C5A3CFF", hex);
            Assert.Equal("Huskarl", store.LastSelectedUnit);
        }

        [Fact]
        public void SetColor_IgnoresInvalidInput()
        {
            var store = new ColorStore();
            store.SetColor("Huskarl", "not-a-color");
            Assert.False(store.TryGetColor("Huskarl", out _));
            store.SetColor(null, "#FFFFFF");
            store.SetColor("", "#FFFFFF");
            Assert.Equal(0, store.UnitCount);
        }

        [Fact]
        public void RemoveColor_RemovesOnlyMatching()
        {
            var store = new ColorStore();
            store.SetColor("A", "#111111");
            store.SetColor("B", "#222222");
            Assert.True(store.RemoveColor("A"));
            Assert.False(store.RemoveColor("A"));
            Assert.True(store.TryGetColor("B", out _));
            Assert.Equal(1, store.UnitCount);
        }

        [Fact]
        public void Serialize_Deserialize_RoundTrips()
        {
            var store = new ColorStore();
            store.DefaultSkinColor = "#AABBCC";
            store.SetColor("Huskarl", "#8C5A3CFF");
            store.SetColor("Halfling", "#FFE9C980");
            store.LastSelectedUnit = "Halfling";

            var json = store.Serialize();
            var restored = ColorStore.Deserialize(json);

            Assert.Equal(store.DefaultSkinColor, restored.DefaultSkinColor);
            Assert.Equal(store.LastSelectedUnit, restored.LastSelectedUnit);
            Assert.Equal(2, restored.UnitCount);
            Assert.True(restored.TryGetColor("Huskarl", out var huskarl));
            Assert.Equal("#8C5A3CFF", huskarl);
            Assert.True(restored.TryGetColor("Halfling", out var halfling));
            Assert.Equal("#FFE9C980", halfling);
        }

        [Fact]
        public void Deserialize_HandlesMalformedJson()
        {
            Assert.NotNull(ColorStore.Deserialize(""));
            Assert.NotNull(ColorStore.Deserialize(null));
            Assert.NotNull(ColorStore.Deserialize("{not json at all"));
            Assert.NotNull(ColorStore.Deserialize("{\"Version\": 1, \"Units\": {"));
        }

        [Fact]
        public void Deserialize_HandEditedJson()
        {
            const string json = @"
            {
                ""Version"": 1,
                ""DefaultSkinColor"": ""#C7A17A"",
                ""LastSelectedUnit"": ""Paul"",
                ""Units"": {
                    ""Paul"": ""#AB12CD"",
                    ""Oglaf"": ""#00112233"",
                    ""Extra"": 42
                }
            }";
            var store = ColorStore.Deserialize(json);
            Assert.True(store.TryGetColor("Paul", out var paul));
            Assert.Equal("#AB12CD", paul);
            Assert.True(store.TryGetColor("Oglaf", out var oglaf));
            Assert.Equal("#00112233", oglaf);
            Assert.False(store.TryGetColor("Extra", out _));
        }
    }
}
