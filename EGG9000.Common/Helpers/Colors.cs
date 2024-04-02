using System;
using System.Drawing;

namespace EGG9000.Common.Helpers {
    public static class Colors {
        public static Color Lerp(this Color s, Color t, float k) {
            var bk = (1 - k);
            var a = s.A * bk + t.A * k;
            var r = s.R * bk + t.R * k;
            var g = s.G * bk + t.G * k;
            var b = s.B * bk + t.B * k;
            return Color.FromArgb((int)a, (int)r, (int)g, (int)b);
        }

        public static String HexConverter(System.Drawing.Color c) {
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }
    }
}
