using System.Globalization;
using System.Text;

namespace SUIsei.Composition;

/// <summary>Paints one colour, as WinUI's <c>CompositionColorBrush</c>.</summary>
public sealed class CompositionColorBrush(HdrColor color) : CompositionBrush
{
	/// <summary>A transparent brush, for an element to recolour in place.</summary>
	public CompositionColorBrush() : this(HdrColor.Transparent)
	{
	}

	/// <summary>The colour, authored as every <see cref="HdrColor"/> is: sRGB-encoded, with components above 1 above
	/// paper white.</summary>
	public HdrColor Color { get; set => Change(ref field, value); } = color;

	internal override void Describe(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(c, $"({Color.R:0.###},{Color.G:0.###},{Color.B:0.###},{Color.A:0.###})");
		if (Opacity != 1f)
			sb.Append(c, $"*{Opacity:0.###}");
	}
}
