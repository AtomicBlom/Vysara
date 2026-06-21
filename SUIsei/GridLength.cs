namespace SUIsei;

/// <summary>A Grid row/column track length — fixed pixels, <c>Auto</c> (max child desired), or a <c>*</c> weight
/// of the leftover space (WinUI <c>GridLength</c>). Default is <c>1*</c>, matching WinUI.</summary>
public readonly record struct GridLength(float Value, GridUnitType Type)
{
	public static GridLength Auto => new(0f, GridUnitType.Auto);
	public static GridLength Star => new(1f, GridUnitType.Star);
	public static GridLength Pixels(float v) => new(v, GridUnitType.Pixel);
	public static GridLength Stars(float weight) => new(weight, GridUnitType.Star);

	public bool IsStar => Type == GridUnitType.Star;
	public bool IsAuto => Type == GridUnitType.Auto;
	public bool IsPixel => Type == GridUnitType.Pixel;

	public override string ToString() => Type switch
	{
		GridUnitType.Pixel => $"{Value:0.###}px",
		GridUnitType.Auto => "Auto",
		_ => $"{Value:0.###}*",
	};
}
