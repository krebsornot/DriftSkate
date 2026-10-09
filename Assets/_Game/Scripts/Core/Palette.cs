using UnityEngine;

namespace DriftSkate
{
    /// <summary>Knallige Jet-Set-Radio-Farben, an einer Stelle gesammelt.</summary>
    public static class Palette
    {
        public static readonly Color Yellow = Hex("FFE14D");
        public static readonly Color Pink = Hex("FF3D8B");
        public static readonly Color Cyan = Hex("2EE6FF");
        public static readonly Color Lime = Hex("A8F03C");
        public static readonly Color Orange = Hex("FF8A2B");
        public static readonly Color Purple = Hex("8C5CFF");
        public static readonly Color Blue = Hex("3D7BFF");
        public static readonly Color Red = Hex("F2384A");
        public static readonly Color Teal = Hex("1FC7A6");
        public static readonly Color White = Hex("F7F4EE");
        public static readonly Color Cream = Hex("FFF1D0");
        public static readonly Color Ink = Hex("16121F");
        public static readonly Color Asphalt = Hex("4A4E63");
        public static readonly Color AsphaltDark = Hex("3A3D50");
        public static readonly Color Concrete = Hex("C9C3D6");
        public static readonly Color Sidewalk = Hex("B8B0C9");
        public static readonly Color Skin = Hex("E8B48F");
        public static readonly Color Glass = Hex("23263A");
        public static readonly Color Metal = Hex("9EA3B5");
        public static readonly Color Rubber = Hex("1C1B22");

        /// <summary>Farbauswahl fuer Lack, Crew-Farbe und Graffiti.</summary>
        public static readonly Color[] Swatches =
        {
            White, Hex("BFC3CF"), Hex("2B2B33"), Red, Orange, Yellow, Lime, Teal,
            Cyan, Blue, Purple, Pink, Hex("7A4B2A"), Hex("1E5C3A"), Hex("123A73"), Hex("5A1E4A")
        };

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var c);
            return c;
        }

        public static Color Shade(Color c, float factor)
        {
            return new Color(c.r * factor, c.g * factor, c.b * factor, c.a);
        }
    }
}
