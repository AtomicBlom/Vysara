namespace Vysara.Animation;

/// <summary>
/// Holds a property at one value while its storyboard runs, as a WinUI <c>ObjectAnimationUsingKeyFrames</c> with a
/// single key at time 0 does: the built-in templates' visual states use it to show a state's brush. It writes the
/// animation rung, so leaving the state releases it and the property shows its own value again. Code-only.
/// </summary>
internal sealed class DiscreteValueAnimation<T> : AnimationTimeline<T>
{
	public DiscreteValueAnimation() => Duration = TimeSpan.Zero;

	/// <summary>The value held.</summary>
	public T Value { get; set; } = default!;

	public override T ValueAt(double progress) => Value;

	private protected override bool TryBind(DependencyObject owner, IAnimationTarget target) => BindDirect(owner, target);
}
