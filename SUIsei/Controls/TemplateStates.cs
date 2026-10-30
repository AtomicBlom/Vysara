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
/// resting state is WinUI's empty one: going to it leaves the state that held a brush, which releases it, and a template
/// being replaced leaves its states too (<see cref="VisualStateManager.LeaveAll"/>).
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
}
