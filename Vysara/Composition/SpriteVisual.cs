using System.Text;

namespace Vysara.Composition;

/// <summary>A leaf node that draws <see cref="Content"/>: a rect with its border, an image, a glyph run, or a vector shape.</summary>
public sealed class SpriteVisual : Visual
{
	/// <summary>
	/// What this visual draws, or null for nothing. Assigning a different object marks the visual dirty; a change made
	/// inside the same content object needs <see cref="Visual.MarkDirty"/> from whoever made it.
	/// </summary>
	public VisualContent? Content
	{
		get;
		set
		{
			if (ReferenceEquals(field, value))
				return;
			field = value;
			MarkDirty();
		}
	}

	private protected override void DescribeSelf(StringBuilder sb)
	{
		base.DescribeSelf(sb);
		Content?.Describe(sb);
	}
}
