using System.Globalization;
using System.Text;

namespace SUIsei;

/// <summary>A leaf node that draws <see cref="Content"/> (rect/border now; glyph-run/image later).</summary>
public sealed class SpriteVisual : Visual
{
	public VisualContent? Content { get; set; }

	private protected override void DescribeSelf(StringBuilder sb)
	{
		base.DescribeSelf(sb);
		var c = CultureInfo.InvariantCulture;
		if (Content is RectContent r)
		{
			sb.Append(c, $" rect={r.Size.X:0.###}x{r.Size.Y:0.###}");
			if (r.Fill is { } f) sb.Append(c, $" fill=({f.R:0.###},{f.G:0.###},{f.B:0.###},{f.A:0.###})");
			if (r.CornerRadius > 0) sb.Append(c, $" radius={r.CornerRadius:0.###}");
			if (r.BorderBrush is { } b && r.BorderThickness > 0)
				sb.Append(c, $" border={r.BorderThickness:0.###}({b.R:0.###},{b.G:0.###},{b.B:0.###},{b.A:0.###})");
		}
		else if (Content is GlyphRunContent g)
			sb.Append(c, $" glyphs=\"{g.Text}\" size={g.FontSizePx:0.###}");
	}
}
