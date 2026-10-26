namespace SUIsei;

/// <summary>
/// Animates an element's background from one brush to the next when its <c>Background</c> changes, as WinUI's
/// <c>BrushTransition</c>: set it as a <c>Border</c>'s, <c>Panel</c>'s or <c>ContentPresenter</c>'s
/// <c>BackgroundTransition</c>. A change from one <see cref="Media.SolidColorBrush"/> to another animates, its colour
/// (opacity included) interpolating over <see cref="Duration"/> on the host's animation clock; so, as a SUIsei extension,
/// does a change between a solid colour and no brush, which fades the colour in or out. Any other change, and any change
/// while the element is not in a live tree, shows at once. A change that arrives mid-transition starts the next one from
/// the colour on screen.
/// </summary>
public sealed class BrushTransition
{
	/// <summary>How long the transition takes. Defaults to 150 ms.</summary>
	public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(150);
}
