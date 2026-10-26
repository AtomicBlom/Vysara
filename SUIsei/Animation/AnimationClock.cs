namespace SUIsei.Animation;

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

	// Running tweens. A finished tween goes back to its type's pool, so a steady stream of tweens allocates nothing.
	private readonly List<TweenEntry> _tweens = [];

	/// <summary>True while any storyboard or tween is running (lets the host know a repaint may be needed).</summary>
	public bool HasActiveClocks => _active.Count > 0 || _tweens.Count > 0;

	/// <summary>Start (or restart) <paramref name="storyboard"/> from time zero. Each animation with no <c>From</c>
	/// starts at the value its target shows now, so a storyboard that takes over a property mid-animation continues
	/// from where the other left it.</summary>
	public void Begin(Storyboard storyboard)
	{
		Stop(storyboard);
		// Resolve any declarative Target/TargetProperty (from generated XAML) into the typed apply now, then record
		// where each run starts.
		foreach (var child in storyboard.Children)
			if (child is AnimationTimeline anim)
			{
				anim.EnsureResolved();
				anim.CaptureStart();
			}
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

		for (var i = _tweens.Count - 1; i >= 0; i--)
		{
			var tween = _tweens[i];
			if (!tween.Advance(deltaSeconds))
				continue;
			_tweens.RemoveAt(i);
			tween.Recycle();
		}
	}

	/// <summary>
	/// Animate a property from its current value to <paramref name="to"/> over <paramref name="duration"/> through typed
	/// delegates: <paramref name="get"/> reads where it starts and <paramref name="set"/> writes every frame. No
	/// storyboard is involved, and once a tween of this shape has run, starting another allocates nothing; pass static
	/// lambdas so the delegates are cached. A tween on the same target with the same <paramref name="set"/> delegate (a
	/// static lambda is one delegate per call site) takes over from the running one where it is. The property is left at
	/// <paramref name="to"/>. <paramref name="lerp"/> says how <typeparamref name="T"/> interpolates; the standard one in
	/// <see cref="Lerps"/> when omitted.
	/// </summary>
	public void Tween<TTarget, T>(TTarget target, Func<TTarget, T> get, Action<TTarget, T> set, T to, TimeSpan duration,
		IEasingFunction? easing = null, Lerp<T>? lerp = null)
		where TTarget : class
	{
		lerp ??= Lerps.For<T>() ?? throw new ArgumentException($"{typeof(T).Name} has no standard interpolation; pass one.", nameof(lerp));
		SetterTween<TTarget, T>? tween = null;
		for (var i = 0; tween is null && i < _tweens.Count; i++)
			if (_tweens[i] is SetterTween<TTarget, T> running && running.Matches(target, set))
				tween = running;
		if (tween is null)
			_tweens.Add(tween = SetterTween<TTarget, T>.Rent());
		tween.Start(target, set, lerp, get(target), to, duration, easing);
	}

	/// <summary>
	/// Animate a dependency property from its value now to <paramref name="to"/>, writing its local value every frame as
	/// code setting it would; otherwise as the delegate-based
	/// <see cref="Tween{TTarget, T}(TTarget, Func{TTarget, T}, Action{TTarget, T}, T, TimeSpan, IEasingFunction, Lerp{T})"/>.
	/// The property must be animatable.
	/// </summary>
	public void Tween<TOwner, T>(DependencyObject target, DependencyProperty<TOwner, T> property, T to, TimeSpan duration,
		IEasingFunction? easing = null)
		where TOwner : DependencyObject
	{
		var lerp = property.Lerp ?? throw new ArgumentException($"{property.Name} is not animatable; its type has no interpolation.", nameof(property));
		PropertyTween<TOwner, T>? tween = null;
		for (var i = 0; tween is null && i < _tweens.Count; i++)
			if (_tweens[i] is PropertyTween<TOwner, T> running && running.Matches(target, property))
				tween = running;
		if (tween is null)
			_tweens.Add(tween = PropertyTween<TOwner, T>.Rent());
		tween.Start(target, property, lerp, target.GetValue(property), to, duration, easing);
	}

	/// <summary>Stop every tween running on <paramref name="target"/>, leaving its properties where they are.</summary>
	public void StopTweens(object target)
	{
		for (var i = _tweens.Count - 1; i >= 0; i--)
		{
			var tween = _tweens[i];
			if (!tween.Targets(target))
				continue;
			_tweens.RemoveAt(i);
			tween.Recycle();
		}
	}

	// One running tween: its timing and easing here, its target and values in a typed subclass. Subclasses are pooled
	// per closed type, thread-static because a clock runs on its host's thread.
	private abstract class TweenEntry
	{
		private double _elapsed;
		private double _duration;
		private IEasingFunction? _easing;

		public abstract bool Targets(object target);

		// Step by deltaSeconds and write the value; true once the end value has been written.
		public bool Advance(double deltaSeconds)
		{
			_elapsed += deltaSeconds;
			var t = _duration > 0 ? Math.Min(1.0, _elapsed / _duration) : 1.0;
			Write((float)(_easing?.Ease(t) ?? t));
			return t >= 1.0;
		}

		// Drop the references a finished tween holds and return it to its pool.
		public void Recycle()
		{
			_easing = null;
			ReturnToPool();
		}

		protected void Restart(TimeSpan duration, IEasingFunction? easing)
		{
			_elapsed = 0;
			_duration = duration.TotalSeconds;
			_easing = easing;
		}

		protected abstract void Write(float progress);

		protected abstract void ReturnToPool();
	}

	private sealed class SetterTween<TTarget, T> : TweenEntry where TTarget : class
	{
		[ThreadStatic] private static Stack<SetterTween<TTarget, T>>? t_pool;

		private TTarget? _target;
		private Action<TTarget, T>? _set;
		private Lerp<T>? _lerp;
		private T _from = default!;
		private T _to = default!;

		public static SetterTween<TTarget, T> Rent() => t_pool is { Count: > 0 } pool ? pool.Pop() : new();

		public bool Matches(TTarget target, Action<TTarget, T> set) => ReferenceEquals(_target, target) && _set == set;

		public void Start(TTarget target, Action<TTarget, T> set, Lerp<T> lerp, T from, T to, TimeSpan duration, IEasingFunction? easing)
		{
			(_target, _set, _lerp, _from, _to) = (target, set, lerp, from, to);
			Restart(duration, easing);
		}

		public override bool Targets(object target) => ReferenceEquals(_target, target);

		protected override void Write(float progress) => _set!(_target!, _lerp!(_from, _to, progress));

		protected override void ReturnToPool()
		{
			(_target, _set, _lerp, _from, _to) = (null, null, null, default!, default!);
			(t_pool ??= new()).Push(this);
		}
	}

	private sealed class PropertyTween<TOwner, T> : TweenEntry where TOwner : DependencyObject
	{
		[ThreadStatic] private static Stack<PropertyTween<TOwner, T>>? t_pool;

		private DependencyObject? _target;
		private DependencyProperty<TOwner, T>? _property;
		private Lerp<T>? _lerp;
		private T _from = default!;
		private T _to = default!;

		public static PropertyTween<TOwner, T> Rent() => t_pool is { Count: > 0 } pool ? pool.Pop() : new();

		public bool Matches(DependencyObject target, DependencyProperty<TOwner, T> property) =>
			ReferenceEquals(_target, target) && ReferenceEquals(_property, property);

		public void Start(DependencyObject target, DependencyProperty<TOwner, T> property, Lerp<T> lerp, T from, T to, TimeSpan duration, IEasingFunction? easing)
		{
			(_target, _property, _lerp, _from, _to) = (target, property, lerp, from, to);
			Restart(duration, easing);
		}

		public override bool Targets(object target) => ReferenceEquals(_target, target);

		protected override void Write(float progress) => _target!.SetValue(_property!, _lerp!(_from, _to, progress));

		protected override void ReturnToPool()
		{
			(_target, _property, _lerp, _from, _to) = (null, null, null, default!, default!);
			(t_pool ??= new()).Push(this);
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
