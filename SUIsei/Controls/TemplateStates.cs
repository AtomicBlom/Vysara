using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// What the built-in templates' visual states are made of. As in WinUI's default templates, a state shows a brush of its
/// own (a pointer-over or pressed background) by holding the part's <c>Background</c> at it while the state is current,
/// and the resting state hands the background back to what the template bound it to, usually the control's own
/// <see cref="Control.Background"/>. The part's <see cref="BrushTransition"/> fades between them.
/// <para>
/// The hand-back is a storyboard of its own rather than WinUI's empty state: the clock lets a storyboard go once it has
/// finished, holding its end value, so leaving the state that holds a brush cannot release it. The resting state's
/// storyboard clears the held value instead, on the clock's next step.
/// </para>
/// </summary>
internal static class TemplateStates
{
	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="border"/>'s background while its
	/// state is current.</summary>
	public static Storyboard Background(Border border, Brush brush) => Hold(
		value => border.SetAnimatedValue(Border.BackgroundProperty, value),
		() => border.ClearAnimatedValue(Border.BackgroundProperty), brush);

	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="control"/>'s own background while its
	/// state is current, for a control that paints its background itself.</summary>
	public static Storyboard Background(Control control, Brush brush) => Hold(
		value => control.SetAnimatedValue(Control.BackgroundProperty, value),
		() => control.ClearAnimatedValue(Control.BackgroundProperty), brush);

	/// <summary>The resting state's storyboard: hands <paramref name="border"/>'s background back to its own.</summary>
	public static Storyboard RestoreBackground(Border border) => Hold(_ => border.ClearAnimatedValue(Border.BackgroundProperty), null, null);

	/// <summary>The resting state's storyboard for a control that paints its background itself.</summary>
	public static Storyboard RestoreBackground(Control control) => Hold(_ => control.ClearAnimatedValue(Control.BackgroundProperty), null, null);

	/// <summary>A fade of <paramref name="milliseconds"/> between background brushes.</summary>
	public static BrushTransition Fade(double milliseconds) => new() { Duration = TimeSpan.FromMilliseconds(milliseconds) };

	private static Storyboard Hold(Action<Brush?> apply, Action? release, Brush? value) => new()
	{
		Children = { new DiscreteValueAnimation<Brush?> { Value = value, Apply = apply, Release = release } },
	};
}
