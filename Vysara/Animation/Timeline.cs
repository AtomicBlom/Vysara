namespace Vysara.Animation;

/// <summary>
/// The base of everything an <see cref="AnimationClock"/> advances: a span of animated time
/// with a <see cref="Duration"/>, a <see cref="BeginTime"/> offset, repeat/reverse behaviour, and a fill rule for
/// what happens when it ends. Plain authoring data — the clock owns the running state.
/// </summary>
public abstract class Timeline
{
	/// <summary>How long one iteration lasts. Defaults to one second.</summary>
	public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(1);

	/// <summary>Delay before the timeline starts, measured from when it is begun.</summary>
	public TimeSpan BeginTime { get; set; } = TimeSpan.Zero;

	/// <summary>How many times to repeat. Defaults to a single iteration.</summary>
	public RepeatBehavior RepeatBehavior { get; set; } = RepeatBehavior.Once;

	/// <summary>When true each iteration plays forward then backward (the period is twice <see cref="Duration"/>).</summary>
	public bool AutoReverse { get; set; }

	/// <summary>What the animation does to its target when it ends. Defaults to <see cref="FillBehavior.HoldEnd"/>.</summary>
	public FillBehavior FillBehavior { get; set; } = FillBehavior.HoldEnd;
}
