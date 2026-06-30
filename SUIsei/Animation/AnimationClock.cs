namespace SUIsei;

/// <summary>
/// Advances active storyboards by the per-frame delta. Held by the <see cref="UIHost"/> and
/// stepped once per <see cref="UIHost.Tick"/> from its <c>deltaSeconds</c>, so output is fully deterministic: tests
/// tick synthetic deltas and assert the resolved value over time — no frame loop, no GPU.
///
/// Each active storyboard tracks its own elapsed time; every tick it normalizes each child animation's progress
/// (honouring <see cref="Timeline.BeginTime"/>/<see cref="Timeline.Duration"/>/<see cref="Timeline.RepeatBehavior"/>/
/// <see cref="Timeline.AutoReverse"/>) and pushes the eased, interpolated value to the target. When an animation
/// finishes, <see cref="FillBehavior.Stop"/> releases the target while <see cref="FillBehavior.HoldEnd"/> leaves the
/// final value in place. A storyboard is dropped once all its children have finished.
/// </summary>
public sealed class AnimationClock
{
	private sealed class Entry
	{
		public required Storyboard Storyboard;
		public double Elapsed;
		public required bool[] Done; // per-child completion latch
	}

	private readonly List<Entry> _active = [];

	/// <summary>True while any storyboard is running (lets the host know a repaint may be needed).</summary>
	public bool HasActiveClocks => _active.Count > 0;

	/// <summary>Start (or restart) <paramref name="storyboard"/> from time zero.</summary>
	public void Begin(Storyboard storyboard)
	{
		Stop(storyboard);
		// Resolve any declarative Target/TargetProperty (from generated XAML) into the typed apply now.
		foreach (var child in storyboard.Children)
			if (child is AnimationTimeline anim)
				anim.EnsureResolved();
		_active.Add(new Entry { Storyboard = storyboard, Done = new bool[storyboard.Children.Count] });
	}

	/// <summary>Stop <paramref name="storyboard"/> and release every target it animates.</summary>
	public void Stop(Storyboard storyboard)
	{
		for (var i = _active.Count - 1; i >= 0; i--)
		{
			if (!ReferenceEquals(_active[i].Storyboard, storyboard))
				continue;
			ReleaseAll(storyboard);
			_active.RemoveAt(i);
		}
	}

	/// <summary>Advance every active storyboard by <paramref name="deltaSeconds"/> and push the current values.</summary>
	public void Advance(double deltaSeconds)
	{
		for (var i = _active.Count - 1; i >= 0; i--)
		{
			var entry = _active[i];
			entry.Elapsed += deltaSeconds;

			var children = entry.Storyboard.Children;
			var allComplete = true;
			for (var c = 0; c < children.Count; c++)
			{
				if (children[c] is not AnimationTimeline anim)
					continue;

				var (progress, active, completed) = Normalize(anim, entry.Elapsed);
				if (!active)
				{
					allComplete = false; // hasn't begun yet (BeginTime in the future)
					continue;
				}

				if (completed)
				{
					if (entry.Done[c])
						continue;
					entry.Done[c] = true;
					if (anim.FillBehavior == FillBehavior.Stop)
						anim.ReleaseTarget();
					else
						anim.ApplyAt(progress); // hold the final value
				}
				else
				{
					anim.ApplyAt(progress);
					allComplete = false;
				}
			}

			if (allComplete)
				_active.RemoveAt(i); // Stop children already released; HoldEnd values stay applied (sticky)
		}
	}

	private static void ReleaseAll(Storyboard storyboard)
	{
		foreach (var child in storyboard.Children)
			if (child is AnimationTimeline anim)
				anim.ReleaseTarget();
	}

	// Map a storyboard's elapsed seconds to (normalized progress in [0,1], has-begun, has-finished) for one timeline,
	// honouring BeginTime, Duration, RepeatBehavior and AutoReverse. Pure → the determinism the tests rely on.
	private static (double Progress, bool Active, bool Completed) Normalize(Timeline t, double elapsed)
	{
		var local = elapsed - t.BeginTime.TotalSeconds;
		if (local < 0)
			return (0.0, false, false);

		var d = t.Duration.TotalSeconds;
		if (d <= 0)
			return (1.0, true, true); // instantaneous

		var iterationLen = t.AutoReverse ? 2.0 * d : d;
		if (!t.RepeatBehavior.IsForever)
		{
			var count = t.RepeatBehavior.Count <= 0 ? 1.0 : t.RepeatBehavior.Count;
			if (local >= count * iterationLen)
				return (t.AutoReverse ? 0.0 : 1.0, true, true); // resting at the end (whole iterations)
		}

		var phase = local % iterationLen / d; // 0..1 (forward only) or 0..2 (auto-reverse)
		var progress = t.AutoReverse ? (phase <= 1.0 ? phase : 2.0 - phase) : phase;
		return (Math.Clamp(progress, 0.0, 1.0), true, false);
	}
}
