namespace BinaryVibrance.Vysara.Media;

/// <summary>
/// Which font text measures and draws with, by name, as WinUI's <c>FontFamily</c>: set as <c>FontFamily</c> on a
/// <see cref="Controls.TextBlock"/>, <see cref="Controls.ContentPresenter"/> or <see cref="Controls.Control"/>, and passed
/// down the tree like <c>FontSize</c>. The name is looked up in the host's <see cref="UIHost.Fonts"/>, ignoring case;
/// a name it doesn't hold draws in the root's <see cref="UIRoot.Font"/>, as text with no family does. So a family may
/// name a role ("Label", "Readout") that an app maps to whichever font suits it, and maps again to change every text
/// in that role at once.
/// </summary>
public sealed class FontFamily : IEquatable<FontFamily>
{
	/// <summary>A family named <paramref name="source"/>.</summary>
	public FontFamily(string source)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(source);
		Source = source.Trim();
	}

	/// <summary>The family's name, as <see cref="UIHost.Fonts"/> knows it.</summary>
	public string Source { get; }

	public bool Equals(FontFamily? other) => other is not null && string.Equals(Source, other.Source, StringComparison.OrdinalIgnoreCase);

	public override bool Equals(object? obj) => obj is FontFamily other && Equals(other);

	public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Source);

	public override string ToString() => Source;

	public static bool operator ==(FontFamily? a, FontFamily? b) => a is null ? b is null : a.Equals(b);

	public static bool operator !=(FontFamily? a, FontFamily? b) => !(a == b);
}
