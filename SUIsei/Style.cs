namespace SUIsei;

/// <summary>
/// An ordered collection of <see cref="Setter"/>s applied to a <see cref="FrameworkElement"/> via the Style
/// rung of the DP precedence ladder. <see cref="BasedOn"/> forms a chain: base setters
/// apply first so derived setters override them. When replaced, the previous style's setters are cleared before
/// the new style is applied.
/// </summary>
public sealed class Style
{
	/// <summary>The style this one inherits from. Base setters apply before <see cref="Setters"/>.</summary>
	public Style? BasedOn { get; init; }

	/// <summary>The setters applied by this style (not including <see cref="BasedOn"/> setters).</summary>
	public IList<Setter> Setters { get; } = [];

	/// <summary>Apply all setters in this chain to <paramref name="target"/> (base-first so derived wins).</summary>
	public void Apply(DependencyObject target)
	{
		BasedOn?.Apply(target);
		foreach (var s in Setters)
			s.Apply(target);
	}

	/// <summary>Clear all setters in this chain from <paramref name="target"/> (call before replacing with a new style).</summary>
	public void Clear(DependencyObject target)
	{
		BasedOn?.Clear(target);
		foreach (var s in Setters)
			s.Clear(target);
	}
}
