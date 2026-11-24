namespace BinaryVibrance.Vysara;

/// <summary>
/// How heavy text is, as WinUI's <c>FontWeight</c>: 100 (thin) to 950 (extra black), 400 normal and 700 bold
/// (<see cref="FontWeights"/>). Set as <c>FontWeight</c> on a <see cref="Controls.TextBlock"/>,
/// <see cref="Controls.ContentPresenter"/> or <see cref="Controls.Control"/> and passed down the tree like <c>FontSize</c>.
/// It chooses among the fonts its family has in the host's <see cref="UIHost.Fonts"/>: the one of that weight, or the
/// nearest it has, as CSS chooses. Text in no family, which draws in its root's <see cref="UIRoot.Font"/>, keeps it
/// whatever its weight.
/// </summary>
public readonly record struct FontWeight(ushort Weight)
{
	public override string ToString() => Weight.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The weights WinUI names (<c>FontWeights</c>), for code and for XAML's <c>FontWeight="Bold"</c>.</summary>
public static class FontWeights
{
	public static FontWeight Thin { get; } = new(100);
	public static FontWeight ExtraLight { get; } = new(200);
	public static FontWeight Light { get; } = new(300);
	public static FontWeight SemiLight { get; } = new(350);
	public static FontWeight Normal { get; } = new(400);
	public static FontWeight Medium { get; } = new(500);
	public static FontWeight SemiBold { get; } = new(600);
	public static FontWeight Bold { get; } = new(700);
	public static FontWeight ExtraBold { get; } = new(800);
	public static FontWeight Black { get; } = new(900);
	public static FontWeight ExtraBlack { get; } = new(950);

	/// <summary>The weight a name gives (any case), or a number from 1 to 999, as XAML writes one; false for anything else.</summary>
	public static bool TryParse(string text, out FontWeight weight)
	{
		var value = text.Trim();
		weight = value.ToLowerInvariant() switch
		{
			"thin" => Thin,
			"extralight" => ExtraLight,
			"light" => Light,
			"semilight" => SemiLight,
			"normal" => Normal,
			"medium" => Medium,
			"semibold" => SemiBold,
			"bold" => Bold,
			"extrabold" => ExtraBold,
			"black" => Black,
			"extrablack" => ExtraBlack,
			_ => default,
		};
		if (weight.Weight != 0)
			return true;
		if (ushort.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) && number is >= 1 and <= 999)
		{
			weight = new FontWeight(number);
			return true;
		}
		return false;
	}
}
