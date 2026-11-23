using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Controls;

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
/// <para>
/// A <c>Disabled</c> state swaps its parts' brushes for the disabled ones here, as WinUI's default styles swap in
/// <c>ButtonBackgroundDisabled</c>, <c>ButtonForegroundDisabled</c> and the like, rather than fading the look: a faded
/// part with children composes through an offscreen layer, and a disabled control inside another would fade twice. A
/// brush is the same colour wherever it shows, so a check box in a disabled button looks as one disabled on its own does.
/// </para>
/// </summary>
internal static class TemplateStates
{
	// The disabled look, shared by every built-in template and never changed: muted against the enabled palette's greys,
	// with the accent's colour taken out.

	/// <summary>A disabled control's fill, where its look has one: darker and flatter than the resting grey.</summary>
	public static readonly SolidColorBrush DisabledBackground = Brush.Sealed(HdrColor.FromRgb(48, 48, 54));

	/// <summary>A disabled control's fill where its look rests on nothing, as a menu item does.</summary>
	public static readonly SolidColorBrush DisabledSubtleBackground = Brush.Sealed(HdrColor.Transparent);

	/// <summary>A disabled control's border.</summary>
	public static readonly SolidColorBrush DisabledBorder = Brush.Sealed(HdrColor.FromRgb(70, 70, 78));

	/// <summary>A disabled control's text.</summary>
	public static readonly SolidColorBrush DisabledForeground = Brush.Sealed(HdrColor.FromRgb(125, 125, 132));

	/// <summary>What a disabled control shows in place of the accent: a checked toggle, a check box's mark, a slider's
	/// fill or a selected item.</summary>
	public static readonly SolidColorBrush DisabledAccent = Brush.Sealed(HdrColor.FromRgb(84, 88, 102));

	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="border"/>'s background while its
	/// state is current.</summary>
	public static Storyboard Background(Border border, Brush brush) => Hold((border, Border.BackgroundProperty, brush));

	/// <summary>A storyboard that shows <paramref name="brush"/> as <paramref name="control"/>'s own background while its
	/// state is current, for a control that paints its background itself.</summary>
	public static Storyboard Background(Control control, Brush brush) => Hold((control, Control.BackgroundProperty, brush));

	/// <summary>The Disabled state of a look that is one border around a presenter: the border's background at
	/// <paramref name="background"/> and its border at <see cref="DisabledBorder"/>, and the presenter's text at
	/// <see cref="DisabledForeground"/>, which the content's text inherits.</summary>
	public static Storyboard Disabled(Border chrome, Brush background, ContentPresenter presenter) => Hold(
		(chrome, Border.BackgroundProperty, background),
		(chrome, Border.BorderBrushProperty, DisabledBorder),
		(presenter, ContentPresenter.ForegroundProperty, DisabledForeground));

	/// <summary>A fade of <paramref name="milliseconds"/> between background brushes.</summary>
	public static BrushTransition Fade(double milliseconds) => new() { Duration = TimeSpan.FromMilliseconds(milliseconds) };

	/// <summary>The built-in look's text colour, replaced by the brush under the theme resource <paramref name="key"/>
	/// wherever one is found: a Foreground the control or its style sets still wins.</summary>
	public static void ThemedForeground(Control control, string key)
	{
		if (control.ThemeResourceOrNull(key) is { } found)
			control.SetBuiltInValue(Control.ForegroundProperty, found);
	}

	/// <summary>A storyboard that holds each target's property at its brush while its state is current.</summary>
	public static Storyboard Hold(params ReadOnlySpan<(DependencyObject Target, DependencyProperty Property, Brush Brush)> holds)
	{
		var storyboard = new Storyboard();
		foreach (var (target, property, brush) in holds)
		{
			storyboard.Children.Add(new ObjectAnimationUsingKeyFrames
			{
				Target = target,
				TargetProperty = property,
				Duration = TimeSpan.Zero,
				KeyFrames = { new DiscreteObjectKeyFrame { Value = brush } },
			});
		}
		return storyboard;
	}
}
