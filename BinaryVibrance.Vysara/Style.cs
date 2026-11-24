namespace BinaryVibrance.Vysara;

/// <summary>
/// An ordered collection of <see cref="Setter"/>s applied to a <see cref="FrameworkElement"/> via the Style
/// rung of the DP precedence ladder. <see cref="BasedOn"/> forms a chain: base setters
/// apply first so derived setters override them. When replaced, the previous style's setters are cleared before
/// the new style is applied.
/// </summary>
public sealed class Style
{
	/// <summary>
	/// The type of element the style is for, as WinUI's <c>TargetType</c>. A style kept in a
	/// <see cref="ResourceDictionary"/> with no key (<see cref="ResourceDictionary.Add(Style)"/>) is that type's implicit
	/// style: every element of exactly that type under the dictionary's owner, with no <c>Style</c> of its own, takes it.
	/// </summary>
	public Type? TargetType { get; init; }

	/// <summary>The style this one inherits from. Base setters apply before <see cref="Setters"/>.</summary>
	public Style? BasedOn { get; init; }

	/// <summary>The setters applied by this style (not including <see cref="BasedOn"/> setters).</summary>
	public IList<Setter> Setters { get; } = [];

	/// <summary>Apply all setters in this chain to <paramref name="target"/> (base-first so derived wins). Every setter's
	/// value is checked against its property before any is applied, so a value the target refuses throws and leaves it as it
	/// was.</summary>
	public void Apply(DependencyObject target)
	{
		Validate(target);
		ApplySetters(target);
	}

	/// <summary>Check every setter in this chain against <paramref name="target"/>, applying none: throws
	/// <see cref="ArgumentException"/> on a value its property refuses.</summary>
	internal void Validate(DependencyObject target)
	{
		BasedOn?.Validate(target);
		foreach (var s in Setters)
			s.Validate(target);
	}

	private void ApplySetters(DependencyObject target)
	{
		BasedOn?.ApplySetters(target);
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
