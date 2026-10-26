namespace SUIsei.Animation;

/// <summary>
/// Animation channels for element properties that are not dependency properties: the paint colour, and the offset an
/// animation adds to the render transform. They write the element's animated overrides, on the visual path with no
/// relayout, and release clears the override.
/// </summary>
internal static class ElementChannels
{
	public static IAnimationTarget<HdrColor> Fill { get; } = new FillChannel();

	public static IAnimationTarget<float> TranslateX { get; } = new TranslateChannel(vertical: false);

	public static IAnimationTarget<float> TranslateY { get; } = new TranslateChannel(vertical: true);

	private static UIElement Element(DependencyObject target, string channel) => target as UIElement
		?? throw new InvalidOperationException($"The {channel} channel animates an element; its target is a {target.GetType().Name}.");

	private sealed class FillChannel : IAnimationTarget<HdrColor>
	{
		public string Name => "Fill";

		public string ValueTypeName => nameof(HdrColor);

		public HdrColor Read(DependencyObject target) => Element(target, Name).CurrentFill;

		public void Write(DependencyObject target, HdrColor value) => Element(target, Name).SetAnimatedFill(value);

		public void Release(DependencyObject target) => Element(target, Name).ClearAnimatedFill();
	}

	// Releasing either axis clears the whole animated offset, as the element keeps one override for both.
	private sealed class TranslateChannel(bool vertical) : IAnimationTarget<float>
	{
		public string Name => vertical ? "TranslateY" : "TranslateX";

		public string ValueTypeName => nameof(Single);

		public float Read(DependencyObject target)
		{
			var offset = Element(target, Name).AnimatedTranslate;
			return vertical ? offset.Y : offset.X;
		}

		public void Write(DependencyObject target, float value)
		{
			var element = Element(target, Name);
			if (vertical)
				element.SetAnimatedTranslateY(value);
			else
				element.SetAnimatedTranslateX(value);
		}

		public void Release(DependencyObject target) => Element(target, Name).ClearAnimatedTranslate();
	}
}
