namespace SUIsei;

/// <summary>
/// A group of timelines begun and stopped as a unit — the thing
/// <see cref="AnimationClock.Begin"/> runs and <see cref="VisualStateManager"/> swaps per state. Children are the
/// per-property <see cref="AnimationTimeline"/>s; the clock advances each by the storyboard's elapsed time.
/// </summary>
public sealed class Storyboard : Timeline
{
	/// <summary>The per-property animations this storyboard drives.</summary>
	public IList<Timeline> Children { get; } = [];
}
