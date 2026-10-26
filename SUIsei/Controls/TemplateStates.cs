using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// What the built-in templates' visual states are made of. As in WinUI's default templates, a state shows a brush of its
/// own (a pointer-over or pressed background) by holding the part's <c>Background</c> at it while the state is current;
/// leaving the state hands the background back to what the template bound it to, usually the control's own
/// <see cref="Control.Background"/>. The part's <see cref="BrushTransition"/> fades between them.
/// </summary>
internal static class TemplateStates
{
	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="border"/>'s background while its
	/// state is current.</summary>
	public static Storyboard Background(Border border, Brush brush) => new()
	{
		Children =
		{
			new DiscreteValueAnimation<Brush?>
			{
				Value = brush,
				Apply = value => border.SetAnimatedValue(Border.BackgroundProperty, value),
				Release = () => border.ClearAnimatedValue(Border.BackgroundProperty),
			},
		},
	};

	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="control"/>'s own background while its
	/// state is current, for a control that paints its background itself.</summary>
	public static Storyboard Background(Control control, Brush brush) => new()
	{
		Children =
		{
			new DiscreteValueAnimation<Brush?>
			{
				Value = brush,
				Apply = value => control.SetAnimatedValue(Control.BackgroundProperty, value),
				Release = () => control.ClearAnimatedValue(Control.BackgroundProperty),
			},
		},
	};

	/// <summary>A fade of <paramref name="milliseconds"/> between background brushes.</summary>
	public static BrushTransition Fade(double milliseconds) => new() { Duration = TimeSpan.FromMilliseconds(milliseconds) };
}
