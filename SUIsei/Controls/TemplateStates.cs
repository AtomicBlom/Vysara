using SUIsei.Animation;
using SUIsei.Media;

namespace SUIsei.Controls;

/// <summary>
/// What the built-in templates' visual states are made of. As in WinUI's default templates, a state shows a brush of its
/// own (a pointer-over or pressed background) by holding the part's <c>Background</c> at it while the state is current,
/// and the resting state hands the background back to what the template bound it to, usually the control's own
/// <see cref="Control.Background"/>. The part's <see cref="BrushTransition"/> fades between them.
/// <para>
/// Each state's brush is an <see cref="ObjectAnimationUsingKeyFrames"/> on the property it holds, as WinUI's templates set
/// one, so going from one brush to another hands the property straight over, and leaving a state releases its brush. The
/// resting state still has a storyboard of its own rather than WinUI's empty state: it clears the background, which also
/// hands back a brush held by a state of a template since replaced, as a control's own background outlives its template.
/// </para>
/// </summary>
internal static class TemplateStates
{
	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="border"/>'s background while its
	/// state is current.</summary>
	public static Storyboard Background(Border border, Brush brush) => Hold(border, Border.BackgroundProperty, brush);

	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="control"/>'s own background while its
	/// state is current, for a control that paints its background itself.</summary>
	public static Storyboard Background(Control control, Brush brush) => Hold(control, Control.BackgroundProperty, brush);

	/// <summary>The resting state's storyboard: hands <paramref name="border"/>'s background back to its own.</summary>
	public static Storyboard RestoreBackground(Border border) => Clear(_ => border.ClearAnimatedValue(Border.BackgroundProperty));

	/// <summary>The resting state's storyboard for a control that paints its background itself.</summary>
	public static Storyboard RestoreBackground(Control control) => Clear(_ => control.ClearAnimatedValue(Control.BackgroundProperty));

	/// <summary>A fade of <paramref name="milliseconds"/> between background brushes.</summary>
	public static BrushTransition Fade(double milliseconds) => new() { Duration = TimeSpan.FromMilliseconds(milliseconds) };

	private static Storyboard Hold(DependencyObject target, DependencyProperty property, Brush brush) => new()
	{
		Children =
		{
			new ObjectAnimationUsingKeyFrames
			{
				Target = target, TargetProperty = property, Duration = TimeSpan.Zero,
				KeyFrames = { new DiscreteObjectKeyFrame { Value = brush } },
			},
		},
	};

	private static Storyboard Clear(Action<Brush?> clear) => new()
	{
		Children = { new DiscreteValueAnimation<Brush?> { Apply = clear } },
	};
}
