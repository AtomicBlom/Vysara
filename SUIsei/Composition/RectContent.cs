using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei.Composition;

/// <summary>
/// A rectangle, optionally rounded, with a fill and an optional border: the Border, Panel and Image primitive. Each
/// is painted by a <see cref="CompositionBrush"/>: a colour, a gradient, or an image. Corners of their own radii, and an
/// image on rounded corners or as the border, are drawn through the vector prim.
/// </summary>
public sealed class RectContent(Vector2 size, CompositionBrush? fill, CompositionCornerRadius cornerRadius = default, CompositionBrush? borderBrush = null,
	float borderThickness = 0f)
	: VisualContent
{
	/// <summary>An empty rectangle, for an element to fill in with <see cref="Update"/>.</summary>
	public RectContent() : this(Vector2.Zero, null)
	{
	}

	/// <summary>The rectangle's size in the visual's local space; it spans (0,0) to <c>Size</c>.</summary>
	public Vector2 Size { get; set => Change(ref field, value); } = size;

	/// <summary>The fill's paint, or null for no fill. Changes inside the brush are versioned by the brush, not by this
	/// content.</summary>
	public CompositionBrush? Fill { get; set => Change(ref field, value); } = fill;

	/// <summary>The corners' radii, shared by the fill and the border; 0 draws square corners. Radii too large for the
	/// rectangle are fitted to it (<see cref="CompositionCornerRadius.FitTo"/>).</summary>
	public CompositionCornerRadius CornerRadius { get; set => Change(ref field, value); } = cornerRadius;

	/// <summary>The border's paint, or null for no border. Changes inside the brush are versioned by the brush.</summary>
	public CompositionBrush? BorderBrush { get; set => Change(ref field, value); } = borderBrush;

	/// <summary>The uniform border thickness; 0 draws no border.</summary>
	public float BorderThickness { get; set => Change(ref field, value); } = borderThickness;

	/// <summary>Set everything this content draws at once, advancing <see cref="VisualContent.Version"/> only if something differs.</summary>
	public void Update(Vector2 size, CompositionBrush? fill, CompositionCornerRadius cornerRadius = default, CompositionBrush? borderBrush = null,
		float borderThickness = 0f)
	{
		Size = size;
		Fill = fill;
		CornerRadius = cornerRadius;
		BorderBrush = borderBrush;
		BorderThickness = borderThickness;
	}

	internal override void Describe(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append(c, $" rect={Size.X:0.###}x{Size.Y:0.###}");
		if (Fill is { } f)
		{
			sb.Append(" fill=");
			f.Describe(sb);
		}
		var r = CornerRadius;
		if (r.IsUniform && r.IsRounded)
			sb.Append(c, $" radius={r.TopLeft:0.###}");
		else if (r.IsRounded)
			sb.Append(c, $" radii=({r.TopLeft:0.###},{r.TopRight:0.###},{r.BottomRight:0.###},{r.BottomLeft:0.###})");
		if (BorderBrush is { } b && BorderThickness > 0)
		{
			sb.Append(c, $" border={BorderThickness:0.###}");
			b.Describe(sb);
		}
	}
}
