using System.Globalization;

namespace SUIsei.Animation;

/// <summary>
/// Animates a scalar from <see cref="From"/> to <see cref="To"/> (or by <see cref="By"/>) over the duration. A null
/// <see cref="From"/> starts at the target's value when the animation begins; a null <see cref="To"/> with a
/// <see cref="By"/> ends at <c>From + By</c>. Drives a float or double property.
/// </summary>
public sealed class DoubleAnimation : AnimationTimeline<double>
{
	/// <summary>
	/// The start value (null ⇒ the target's value when the animation begins). That value must be a number: from a size
	/// left Auto (NaN), or an infinite MaxWidth, the animation throws as it begins, so it needs a From.
	/// </summary>
	public double? From { get; set; }

	/// <summary>The end value (null with <see cref="By"/> ⇒ <c>From + By</c>).</summary>
	public double? To { get; set; }

	/// <summary>A relative end offset used when <see cref="To"/> is null.</summary>
	public double? By { get; set; }

	/// <summary>The optional easing applied to the linear progress.</summary>
	public IEasingFunction? EasingFunction { get; set; }

	/// <summary>
	/// WinUI's opt-in for an animation of a property that relayouts, such as <c>Width</c>, which WinUI otherwise skips so
	/// that it never slows the UI thread. SUIsei runs every animation on the UI thread with layout, so it runs them all and
	/// reads nothing here; the property is kept so WinUI markup that sets it compiles unchanged.
	/// </summary>
	public bool EnableDependentAnimation { get; set; }

	public override double ValueAt(double progress)
	{
		var from = From ?? StartValue;
		var to = To ?? (By is { } by ? from + by : from);
		var eased = EasingFunction?.Ease(progress) ?? progress;
		return from + (to - from) * eased;
	}

	private protected override bool StartsFromTarget => From is null;

	// With no From the run starts from the target's value, which must be a number. A size left Auto is NaN, and a
	// MaxWidth is infinite by default; WinUI interpolates from NaN, so the size stays Auto for the whole run and after, and
	// WPF throws. This throws, as WPF does, so the missing From is found at once.
	internal override void CheckStart()
	{
		base.CheckStart();
		if (From is null && !double.IsFinite(StartValue))
			throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
				$"A {nameof(DoubleAnimation)} with no From starts from its target's value, and {TargetProperty?.ToString() ?? "its target"} " +
				$"{(Target is null ? "" : $"on a {Target.GetType().Name} ")}is {StartValue}{(double.IsNaN(StartValue) ? " (Auto, for a size)" : "")}: give it a From."));
	}

	private protected override bool TryBind(DependencyObject owner, IAnimationTarget target) => DoubleBinding.TryBind(this, owner, target);
}
