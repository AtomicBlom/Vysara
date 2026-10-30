using System.Globalization;

namespace SUIsei.Markup;

/// <summary>
/// The text forms XAML values take: colours (<c>#RRGGBB</c>, <c>#AARRGGBB</c>, or one of WinUI's named colours), lists of
/// numbers separated by commas or whitespace (points, vectors, thicknesses), grid lengths, durations and repeat
/// behaviours.
/// <para>
/// This file is compiled into both the runtime and the netstandard2.0 XAML generator, which links it as source, so a
/// value the generator accepts at compile time parses the same way at runtime. It uses only APIs both targets have.
/// </para>
/// </summary>
internal static class XamlValueSyntax
{
	/// <summary>Parse a colour: <c>#RRGGBB</c>, <c>#AARRGGBB</c>, or a WinUI named colour such as <c>CornflowerBlue</c>
	/// (any case). Components are 0–255.</summary>
	public static bool TryParseColor(string text, out byte a, out byte r, out byte g, out byte b)
	{
		a = r = g = b = 0;
		var s = text.Trim();
		uint argb;
		if (s.StartsWith("#", StringComparison.Ordinal))
		{
			var hex = s.Substring(1);
			if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out argb))
				return false;
			if (hex.Length == 6)
				argb |= 0xFF000000u;
		}
		else if (!NamedColors.TryGetValue(s, out argb))
			return false;

		a = (byte)(argb >> 24);
		r = (byte)(argb >> 16);
		g = (byte)(argb >> 8);
		b = (byte)argb;
		return true;
	}

	/// <summary>Parse numbers separated by commas and/or whitespace, as WinUI's point and thickness syntax allows.
	/// Null when any token is not a finite number (NaN, infinities and values out of float range are rejected) or the
	/// text holds none.</summary>
	public static float[]? ParseNumbers(string text)
	{
		var tokens = text.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
			return null;
		var values = new float[tokens.Length];
		for (var i = 0; i < tokens.Length; i++)
		{
			if (!float.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i])
				|| float.IsNaN(values[i]) || float.IsInfinity(values[i]))
				return null;
		}
		return values;
	}

	/// <summary>The three forms a grid length takes.</summary>
	public enum GridLengthForm { Auto, Pixels, Stars }

	/// <summary>Parse a WinUI grid length: <c>Auto</c> (any case), <c>*</c> or <c>N*</c> (a star weight), or a pixel
	/// number. <paramref name="value"/> is the weight or the pixels, and 1 for a bare <c>*</c>.</summary>
	public static bool TryParseGridLength(string text, out GridLengthForm form, out float value)
	{
		var s = text.Trim();
		form = GridLengthForm.Pixels;
		value = 0f;
		if (string.Equals(s, "Auto", StringComparison.OrdinalIgnoreCase))
		{
			form = GridLengthForm.Auto;
			return true;
		}
		if (s == "*")
		{
			form = GridLengthForm.Stars;
			value = 1f;
			return true;
		}
		if (s.EndsWith("*", StringComparison.Ordinal))
		{
			if (ParseNumbers(s.Substring(0, s.Length - 1)) is not { Length: 1 } weight)
				return false;
			form = GridLengthForm.Stars;
			value = weight[0];
			return true;
		}
		if (ParseNumbers(s) is not { Length: 1 } pixels)
			return false;
		value = pixels[0];
		return true;
	}

	/// <summary>Parse a duration as WinUI's XAML reads a <c>TimeSpan</c>, to total seconds: <c>[d.]h:m[:s[.f]]</c>, or a
	/// whole number of days. So <c>0:0:0.25</c> is a quarter of a second, <c>0:30</c> thirty minutes and <c>2</c> two days.
	/// A negative duration is refused.</summary>
	public static bool TryParseDuration(string text, out double seconds)
	{
		seconds = 0;
		if (!TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out var span) || span < TimeSpan.Zero)
			return false;
		seconds = span.TotalSeconds;
		return true;
	}

	/// <summary>Whether <paramref name="text"/> is a duration SUIsei once read as seconds and WinUI reads otherwise: a bare
	/// number other than 0 (seconds once, days in WinUI) or <c>m:s</c> (minutes and seconds once, hours and minutes in
	/// WinUI). The generator refuses these, so markup written for the old reading is found rather than run thousands of
	/// times slower; <c>h:m:s</c> means the same in both.</summary>
	public static bool IsAmbiguousDuration(string text)
	{
		var s = text.Trim();
		var colons = 0;
		foreach (var c in s)
		{
			if (c == ':')
				colons++;
		}
		return colons == 1
			|| (colons == 0 && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number != 0);
	}

	/// <summary>Whether <paramref name="text"/> is <c>Auto</c> (any case) for a property that takes it: WinUI's
	/// <c>FrameworkElement.Width</c> and <c>Height</c>, which its length converter reads as NaN. No other property takes
	/// it.</summary>
	public static bool IsAutoLength(string declaringType, string property, string text) =>
		declaringType == "SUIsei.FrameworkElement" && property is "Width" or "Height"
		&& string.Equals(text.Trim(), "Auto", StringComparison.OrdinalIgnoreCase);

	/// <summary>Parse a repeat behaviour: <c>Forever</c> (any case), or a positive iteration count with an optional
	/// <c>x</c>, as <c>3x</c> or <c>0.5x</c>.</summary>
	public static bool TryParseRepeatBehavior(string text, out bool forever, out double count)
	{
		var s = text.Trim();
		forever = string.Equals(s, "Forever", StringComparison.OrdinalIgnoreCase);
		count = 0;
		if (forever)
			return true;
		var number = s.EndsWith("x", StringComparison.OrdinalIgnoreCase) ? s.Substring(0, s.Length - 1) : s;
		return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out count)
			&& !double.IsNaN(count) && !double.IsInfinity(count) && count > 0;
	}

	private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

	// WinUI's Microsoft.UI.Colors, as ARGB.
	private static readonly Dictionary<string, uint> NamedColors = new(StringComparer.OrdinalIgnoreCase)
	{
		["AliceBlue"] = 0xFFF0F8FF,
		["AntiqueWhite"] = 0xFFFAEBD7,
		["Aqua"] = 0xFF00FFFF,
		["Aquamarine"] = 0xFF7FFFD4,
		["Azure"] = 0xFFF0FFFF,
		["Beige"] = 0xFFF5F5DC,
		["Bisque"] = 0xFFFFE4C4,
		["Black"] = 0xFF000000,
		["BlanchedAlmond"] = 0xFFFFEBCD,
		["Blue"] = 0xFF0000FF,
		["BlueViolet"] = 0xFF8A2BE2,
		["Brown"] = 0xFFA52A2A,
		["BurlyWood"] = 0xFFDEB887,
		["CadetBlue"] = 0xFF5F9EA0,
		["Chartreuse"] = 0xFF7FFF00,
		["Chocolate"] = 0xFFD2691E,
		["Coral"] = 0xFFFF7F50,
		["CornflowerBlue"] = 0xFF6495ED,
		["Cornsilk"] = 0xFFFFF8DC,
		["Crimson"] = 0xFFDC143C,
		["Cyan"] = 0xFF00FFFF,
		["DarkBlue"] = 0xFF00008B,
		["DarkCyan"] = 0xFF008B8B,
		["DarkGoldenrod"] = 0xFFB8860B,
		["DarkGray"] = 0xFFA9A9A9,
		["DarkGreen"] = 0xFF006400,
		["DarkKhaki"] = 0xFFBDB76B,
		["DarkMagenta"] = 0xFF8B008B,
		["DarkOliveGreen"] = 0xFF556B2F,
		["DarkOrange"] = 0xFFFF8C00,
		["DarkOrchid"] = 0xFF9932CC,
		["DarkRed"] = 0xFF8B0000,
		["DarkSalmon"] = 0xFFE9967A,
		["DarkSeaGreen"] = 0xFF8FBC8F,
		["DarkSlateBlue"] = 0xFF483D8B,
		["DarkSlateGray"] = 0xFF2F4F4F,
		["DarkTurquoise"] = 0xFF00CED1,
		["DarkViolet"] = 0xFF9400D3,
		["DeepPink"] = 0xFFFF1493,
		["DeepSkyBlue"] = 0xFF00BFFF,
		["DimGray"] = 0xFF696969,
		["DodgerBlue"] = 0xFF1E90FF,
		["Firebrick"] = 0xFFB22222,
		["FloralWhite"] = 0xFFFFFAF0,
		["ForestGreen"] = 0xFF228B22,
		["Fuchsia"] = 0xFFFF00FF,
		["Gainsboro"] = 0xFFDCDCDC,
		["GhostWhite"] = 0xFFF8F8FF,
		["Gold"] = 0xFFFFD700,
		["Goldenrod"] = 0xFFDAA520,
		["Gray"] = 0xFF808080,
		["Green"] = 0xFF008000,
		["GreenYellow"] = 0xFFADFF2F,
		["Honeydew"] = 0xFFF0FFF0,
		["HotPink"] = 0xFFFF69B4,
		["IndianRed"] = 0xFFCD5C5C,
		["Indigo"] = 0xFF4B0082,
		["Ivory"] = 0xFFFFFFF0,
		["Khaki"] = 0xFFF0E68C,
		["Lavender"] = 0xFFE6E6FA,
		["LavenderBlush"] = 0xFFFFF0F5,
		["LawnGreen"] = 0xFF7CFC00,
		["LemonChiffon"] = 0xFFFFFACD,
		["LightBlue"] = 0xFFADD8E6,
		["LightCoral"] = 0xFFF08080,
		["LightCyan"] = 0xFFE0FFFF,
		["LightGoldenrodYellow"] = 0xFFFAFAD2,
		["LightGray"] = 0xFFD3D3D3,
		["LightGreen"] = 0xFF90EE90,
		["LightPink"] = 0xFFFFB6C1,
		["LightSalmon"] = 0xFFFFA07A,
		["LightSeaGreen"] = 0xFF20B2AA,
		["LightSkyBlue"] = 0xFF87CEFA,
		["LightSlateGray"] = 0xFF778899,
		["LightSteelBlue"] = 0xFFB0C4DE,
		["LightYellow"] = 0xFFFFFFE0,
		["Lime"] = 0xFF00FF00,
		["LimeGreen"] = 0xFF32CD32,
		["Linen"] = 0xFFFAF0E6,
		["Magenta"] = 0xFFFF00FF,
		["Maroon"] = 0xFF800000,
		["MediumAquamarine"] = 0xFF66CDAA,
		["MediumBlue"] = 0xFF0000CD,
		["MediumOrchid"] = 0xFFBA55D3,
		["MediumPurple"] = 0xFF9370DB,
		["MediumSeaGreen"] = 0xFF3CB371,
		["MediumSlateBlue"] = 0xFF7B68EE,
		["MediumSpringGreen"] = 0xFF00FA9A,
		["MediumTurquoise"] = 0xFF48D1CC,
		["MediumVioletRed"] = 0xFFC71585,
		["MidnightBlue"] = 0xFF191970,
		["MintCream"] = 0xFFF5FFFA,
		["MistyRose"] = 0xFFFFE4E1,
		["Moccasin"] = 0xFFFFE4B5,
		["NavajoWhite"] = 0xFFFFDEAD,
		["Navy"] = 0xFF000080,
		["OldLace"] = 0xFFFDF5E6,
		["Olive"] = 0xFF808000,
		["OliveDrab"] = 0xFF6B8E23,
		["Orange"] = 0xFFFFA500,
		["OrangeRed"] = 0xFFFF4500,
		["Orchid"] = 0xFFDA70D6,
		["PaleGoldenrod"] = 0xFFEEE8AA,
		["PaleGreen"] = 0xFF98FB98,
		["PaleTurquoise"] = 0xFFAFEEEE,
		["PaleVioletRed"] = 0xFFDB7093,
		["PapayaWhip"] = 0xFFFFEFD5,
		["PeachPuff"] = 0xFFFFDAB9,
		["Peru"] = 0xFFCD853F,
		["Pink"] = 0xFFFFC0CB,
		["Plum"] = 0xFFDDA0DD,
		["PowderBlue"] = 0xFFB0E0E6,
		["Purple"] = 0xFF800080,
		["Red"] = 0xFFFF0000,
		["RosyBrown"] = 0xFFBC8F8F,
		["RoyalBlue"] = 0xFF4169E1,
		["SaddleBrown"] = 0xFF8B4513,
		["Salmon"] = 0xFFFA8072,
		["SandyBrown"] = 0xFFF4A460,
		["SeaGreen"] = 0xFF2E8B57,
		["SeaShell"] = 0xFFFFF5EE,
		["Sienna"] = 0xFFA0522D,
		["Silver"] = 0xFFC0C0C0,
		["SkyBlue"] = 0xFF87CEEB,
		["SlateBlue"] = 0xFF6A5ACD,
		["SlateGray"] = 0xFF708090,
		["Snow"] = 0xFFFFFAFA,
		["SpringGreen"] = 0xFF00FF7F,
		["SteelBlue"] = 0xFF4682B4,
		["Tan"] = 0xFFD2B48C,
		["Teal"] = 0xFF008080,
		["Thistle"] = 0xFFD8BFD8,
		["Tomato"] = 0xFFFF6347,
		["Transparent"] = 0x00FFFFFF,
		["Turquoise"] = 0xFF40E0D0,
		["Violet"] = 0xFFEE82EE,
		["Wheat"] = 0xFFF5DEB3,
		["White"] = 0xFFFFFFFF,
		["WhiteSmoke"] = 0xFFF5F5F5,
		["Yellow"] = 0xFFFFFF00,
		["YellowGreen"] = 0xFF9ACD32,
	};
}
