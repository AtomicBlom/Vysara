using System.Globalization;
using System.Numerics;
using System.Text;

namespace SUIsei.Composition;

/// <summary>
/// A rectangle, optionally rounded, with a fill and an optional border: the Border, Panel and Image primitive. Each
/// is painted by a <see cref="CompositionBrush"/>: a colour, a gradient, or an image (on rounded corners, or as the
/// border, drawn through the vector prim).
/// </summary>
public sealed class RectContent(Vector2 size, CompositionBrush? fill, float cornerRadius = 0f, CompositionBrush? borderBrush = null, float borderThickness = 0f)
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

	/// <summary>The corner radius shared by the fill and the border; 0 draws square corners.</summary>
	public float CornerRadius { get; set => Change(ref field, value); } = cornerRadius;

	/// <summary>The border's paint, or null for no border. Changes inside the brush are versioned by the brush.</summary>
	public CompositionBrush? BorderBrush { get; set => Change(ref field, value); } = borderBrush;

	/// <summary>The uniform border thickness; 0 draws no border.</summary>
	public float BorderThickness { get; set => Change(ref field, value); } = borderThickness;

	/// <summary>Set everything this content draws at once, advancing <see cref="VisualContent.Version"/> only if something differs.</summary>
	public void Update(Vector2 size, CompositionBrush? fill, float cornerRadius = 0f, CompositionBrush? borderBrush = null, float borderThickness = 0f)
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
		if (CornerRadius > 0)
			sb.Append(c, $" radius={CornerRadius:0.###}");
		if (BorderBrush is { } b && BorderThickness > 0)
		{
			sb.Append(c, $" border={BorderThickness:0.###}");
			b.Describe(sb);
		}
	}
}
