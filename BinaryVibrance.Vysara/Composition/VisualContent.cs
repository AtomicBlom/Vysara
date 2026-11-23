using System.Text;

namespace BinaryVibrance.Vysara.Composition;

/// <summary>
/// What a <see cref="SpriteVisual"/> draws: the rectangle family (fill + border, or an image), MSDF glyph-run text, and
/// vector shapes. The compositor switches on the concrete type, with no reflection.
/// <para>
/// Content is retained and mutable, like WinUI's composition objects: an element keeps one content object and
/// updates it in place, so a brush change, an animation frame or a re-arrange allocates nothing. Every change to what
/// it draws advances <see cref="Version"/>, which a compositor reads to see what changed and to key work it derives
/// from the content. Whoever changes content marks its visual dirty, as for any other visual change.
/// </para>
/// </summary>
public abstract class VisualContent
{
	/// <summary>
	/// Advances on every change to what this content draws; a setter that stores an equal value leaves it alone. It is
	/// never 0, so a compositor can use 0 for "not seen yet".
	/// </summary>
	public uint Version { get; private set; } = 1;

	/// <summary>
	/// Work a compositor derived from this content, such as a shaped glyph run or tessellated geometry, kept on the
	/// content so it needs no lookup table and is collected with it. It is current while <see cref="DerivedVersion"/>
	/// equals <see cref="Version"/>; a stale one is still here to be refilled in place.
	/// </summary>
	internal object? Derived { get; private set; }

	/// <summary>The <see cref="Version"/> <see cref="Derived"/> was computed from.</summary>
	internal uint DerivedVersion { get; private set; }

	/// <summary>Whether <see cref="Derived"/> was computed from the current <see cref="Version"/>.</summary>
	internal bool HasCurrentDerived => Derived is not null && DerivedVersion == Version;

	/// <summary>Keep <paramref name="derived"/> as the work derived from the current <see cref="Version"/>.</summary>
	internal void SetDerived(object derived)
	{
		Derived = derived;
		DerivedVersion = Version;
	}

	/// <summary>Append this content's part of a <see cref="Visual.Dump()"/> line.</summary>
	internal abstract void Describe(StringBuilder sb);

	/// <summary>Store <paramref name="value"/> in <paramref name="slot"/>, advancing <see cref="Version"/> if it differs.</summary>
	private protected void Change<T>(ref T slot, T value)
	{
		if (EqualityComparer<T>.Default.Equals(slot, value))
			return;
		slot = value;
		Version++;
	}
}
