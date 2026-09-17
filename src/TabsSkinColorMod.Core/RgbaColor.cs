using System;
using System.Globalization;

namespace TabsSkinColorMod.Core
{
    /// <summary>
    /// A simple RGB(A) color value, independent of any Unity types, so the core
    /// logic stays unit-testable and free of engine dependencies.
    /// </summary>
    public struct RgbaColor : IEquatable<RgbaColor>
    {
        public byte R;
        public byte G;
        public byte B;
        public byte A;

        public RgbaColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public bool IsFullyTransparent => A == 0;

        /// <summary>
        /// Parses "#RRGGBB", "#RRGGBBAA", "RRGGBB" or "RRGGBBAA" (case-insensitive).
        /// Returns false for anything else.
        /// </summary>
        public static bool TryParse(string text, out RgbaColor color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var s = text.Trim().TrimStart('#');
            if (s.Length != 6 && s.Length != 8) return false;
            for (int i = 0; i < s.Length; i++)
                if (!Uri.IsHexDigit(s[i])) return false;
            var r = byte.Parse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var g = byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var b = byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var a = s.Length == 8
                ? byte.Parse(s.Substring(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
                : (byte)255;
            color = new RgbaColor(r, g, b, a);
            return true;
        }

        public static RgbaColor ParseOrDefault(string text, RgbaColor fallback)
            => TryParse(text, out var c) ? c : fallback;

        public string ToHex(bool includeAlpha = false)
        {
            var baseHex = string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", R, G, B);
            return includeAlpha ? baseHex + A.ToString("X2", CultureInfo.InvariantCulture) : baseHex;
        }

        public override string ToString() => ToHex(true);

        public bool Equals(RgbaColor other) => R == other.R && G == other.G && B == other.B && A == other.A;
        public override bool Equals(object obj) => obj is RgbaColor other && Equals(other);
        public override int GetHashCode() => (R << 24) | (G << 16) | (B << 8) | A;
        public static bool operator ==(RgbaColor a, RgbaColor b) => a.Equals(b);
        public static bool operator !=(RgbaColor a, RgbaColor b) => !a.Equals(b);
    }

    /// <summary>Color math helpers (HSV conversions) kept in Core for testability.</summary>
    public static class ColorMath
    {
        /// <summary>HSV (h,s,v all 0..1) to RGB. Alpha is always 255.</summary>
        public static RgbaColor HsvToRgb(double h, double s, double v)
        {
            h = Clamp(h, 0.0, 1.0) * 6.0;
            s = Clamp(s, 0.0, 1.0);
            v = Clamp(v, 0.0, 1.0);

            var i = (int)h % 6;
            var f = h - Math.Floor(h);
            var p = v * (1.0 - s);
            var q = v * (1.0 - f * s);
            var t = v * (1.0 - (1.0 - f) * s);

            byte R, G, B;
            switch (i)
            {
                case 0: R = B2(v); G = B2(t); B = B2(p); break;
                case 1: R = B2(q); G = B2(v); B = B2(p); break;
                case 2: R = B2(p); G = B2(v); B = B2(t); break;
                case 3: R = B2(p); G = B2(q); B = B2(v); break;
                case 4: R = B2(t); G = B2(p); B = B2(v); break;
                default: R = B2(v); G = B2(p); B = B2(q); break;
            }
            return new RgbaColor(R, G, B);
        }

        private static byte B2(double x) => (byte)Math.Round(Clamp(x, 0.0, 1.0) * 255.0);
        private static double Clamp(double x, double lo, double hi) => x < lo ? lo : (x > hi ? hi : x);
    }
}
