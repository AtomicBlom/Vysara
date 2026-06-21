namespace SUIsei.Controls;

/// <summary>Absolute positioning by the attached <see cref="LeftProperty"/>/<see cref="TopProperty"/>
/// (PlanningUI.md §D2.3): children measure unconstrained and arrange at their (Left, Top) with their desired size;
/// the Canvas itself desires nothing.</summary>
public sealed class Canvas : Panel
{
	public static readonly DependencyProperty<Canvas, float> LeftProperty =
		DependencyProperty.RegisterAttached<Canvas, float>("Left",
			new PropertyMetadata<Canvas, float> { DefaultValue = 0f, Invalidates = InvalidationFlags.Arrange });

	public static readonly DependencyProperty<Canvas, float> TopProperty =
		DependencyProperty.RegisterAttached<Canvas, float>("Top",
			new PropertyMetadata<Canvas, float> { DefaultValue = 0f, Invalidates = InvalidationFlags.Arrange });

	public static void SetLeft(UIElement element, float value) => element.SetValue(LeftProperty, value);
	public static float GetLeft(UIElement element) => element.GetValue(LeftProperty);
	public static void SetTop(UIElement element, float value) => element.SetValue(TopProperty, value);
	public static float GetTop(UIElement element) => element.GetValue(TopProperty);

	protected override Size MeasureOverride(Size available)
	{
		foreach (var child in Children.Items)
			child.Measure(Size.Infinite);
		return Size.Zero;
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		foreach (var child in Children.Items)
			child.Arrange(new Rect(GetLeft(child), GetTop(child), child.DesiredSize.Width, child.DesiredSize.Height));
		return finalSize;
	}
}
