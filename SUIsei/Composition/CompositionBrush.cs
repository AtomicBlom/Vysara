using System.Text;

namespace SUIsei.Composition;

/// <summary>
/// Paint for composition content: what fills a <see cref="RectContent"/> or its border, or colours a
/// <see cref="GlyphRunContent"/>. Named after WinUI's composition brushes: a solid colour
/// (<see cref="CompositionColorBrush"/>), a linear gradient (<see cref="CompositionLinearGradientBrush"/>) or an image
/// (<see cref="CompositionSurfaceBrush"/>). The compositor switches on the concrete type, with no reflection.
/// <para>
/// Paint is retained and mutable, as content is, and versioned on its own: <see cref="Version"/> moves on every change to
/// what the brush paints, while the content that holds it keeps its version. One brush may paint many contents: every
/// element showing the same XAML brush shares that brush's one composition brush, so a recolour is one in-place write
/// however many elements show it, and a compositor that keys work on what a content draws reads both versions.
/// </para>
/// </summary>
public abstract class CompositionBrush
{
	private protected CompositionBrush()
	{
	}

	/// <summary>
	/// Advances on every change to what this brush paints; a setter that stores an equal value leaves it alone. It is
	/// never 0, so a compositor can use 0 for "not seen yet".
	/// </summary>
	public uint Version { get; private set; } = 1;

	/// <summary>Multiplies the alpha of everything this brush paints: the XAML brush's <c>Opacity</c>.</summary>
	public float Opacity { get; set => Change(ref field, value); } = 1f;

	/// <summary>Append this brush's part of a <see cref="Visual.Dump()"/> line.</summary>
	internal abstract void Describe(StringBuilder sb);

	/// <summary>Store <paramref name="value"/> in <paramref name="slot"/>, advancing <see cref="Version"/> if it differs.</summary>
	private protected void Change<T>(ref T slot, T value)
	{
		if (EqualityComparer<T>.Default.Equals(slot, value))
			return;
		slot = value;
		Version++;
	}

	/// <summary>Record a change made in place, such as a new colour stop.</summary>
	private protected void Touch() => Version++;
}
