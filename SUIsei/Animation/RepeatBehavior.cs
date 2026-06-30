namespace SUIsei;

/// <summary>
/// How many times a <see cref="Timeline"/> repeats: a finite iteration <see cref="Count"/>
/// or <see cref="Forever"/>. The default (<c>default(RepeatBehavior)</c>) is a single iteration.
/// </summary>
public readonly record struct RepeatBehavior
{
	/// <summary>True when the timeline repeats indefinitely.</summary>
	public bool IsForever { get; private init; }

	/// <summary>The number of iterations to run when not <see cref="IsForever"/> (a non-positive value means one).</summary>
	public double Count { get; private init; }

	/// <summary>Run a single iteration (the default).</summary>
	public static RepeatBehavior Once => new() { Count = 1 };

	/// <summary>Repeat for the given number of iterations.</summary>
	public static RepeatBehavior Times(double count) => new() { Count = count };

	/// <summary>Repeat indefinitely.</summary>
	public static RepeatBehavior Forever => new() { IsForever = true };
}
