using System.Globalization;
using System.Numerics;
using System.Text;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara.Composition;

/// <summary>
/// Paints an image, as WinUI's <c>CompositionSurfaceBrush</c>: <see cref="Surface"/> is scaled into the box painted by
/// <see cref="Stretch"/> and placed by the alignment ratios. Only the box is painted: an image that overhangs it is cut
/// at its edges, and one that does not fill it leaves the rest transparent.
/// </summary>
public sealed class CompositionSurfaceBrush : CompositionBrush
{
	/// <summary>The image, sampled through the draw list's texture slots; null, or a disposed image, paints nothing.</summary>
	public TextureSource? Surface { get; set => Change(ref field, value); }

	/// <summary>The image the brush draws: <see cref="Surface"/> unless it has been disposed.</summary>
	internal TextureSource? Drawn => Surface is { IsDisposed: false } surface ? surface : null;

	/// <summary>How the image is scaled into the box. Defaults to filling it.</summary>
	public CompositionStretch Stretch { get; set => Change(ref field, value); } = CompositionStretch.Fill;

	/// <summary>Where the image sits across the box when it is narrower or wider: 0 at the left, 1 at the right.</summary>
	public float HorizontalAlignmentRatio { get; set => Change(ref field, value); } = 0.5f;

	/// <summary>Where the image sits down the box when it is shorter or taller: 0 at the top, 1 at the bottom.</summary>
	public float VerticalAlignmentRatio { get; set => Change(ref field, value); } = 0.5f;

	/// <summary>Where the whole image lands in a box of <paramref name="size"/> at the origin, before it is cut to the
	/// box: <see cref="Stretch"/> scales it, the alignment ratios place it.</summary>
	internal Rect ImageRect(Vector2 size)
	{
		if (Surface is not { } surface || surface.Width <= 0 || surface.Height <= 0 || Stretch == CompositionStretch.Fill)
			return new Rect(0f, 0f, size.X, size.Y);
		float iw = surface.Width, ih = surface.Height;
		var scale = Stretch switch
		{
			CompositionStretch.None => 1f,
			CompositionStretch.UniformToFill => MathF.Max(size.X / iw, size.Y / ih),
			_ => MathF.Min(size.X / iw, size.Y / ih),
		};
		var w = iw * scale;
		var h = ih * scale;
		return new Rect((size.X - w) * HorizontalAlignmentRatio, (size.Y - h) * VerticalAlignmentRatio, w, h);
	}

	/// <summary>What the brush paints in a box of <paramref name="size"/>: the image cut to the box, empty without
	/// one.</summary>
	internal Rect PaintedRect(Vector2 size) =>
		Drawn is null ? Rect.Zero : ImageRect(size).Intersect(new Rect(0f, 0f, size.X, size.Y));

	internal override void Describe(StringBuilder sb)
	{
		var c = CultureInfo.InvariantCulture;
		sb.Append("image(");
		if (Surface is { } s)
		{
			s.Describe(sb);
			sb.Append(c, $" {Stretch}");
		}
		else
			sb.Append("none");
		if (HorizontalAlignmentRatio != 0.5f || VerticalAlignmentRatio != 0.5f)
			sb.Append(c, $" align={HorizontalAlignmentRatio:0.###},{VerticalAlignmentRatio:0.###}");
		if (Opacity != 1f)
			sb.Append(c, $" *{Opacity:0.###}");
		sb.Append(')');
	}
}
