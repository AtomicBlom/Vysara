using System.Globalization;
using System.Runtime.CompilerServices;

namespace BinaryVibrance.Vysara.Animation;

/// <summary>
/// Advances active storyboards by the per-frame delta. Held by the <see cref="UIHost"/> and
/// stepped once per <see cref="UIHost.Tick"/> from its <c>deltaSeconds</c>, so output is fully deterministic: tests
/// tick synthetic deltas and assert the resolved value over time — no frame loop, no GPU.
///
/// Each active storyboard tracks its own elapsed time; every tick it normalizes each child animation's progress
/// (honouring <see cref="Timeline.BeginTime"/>/<see cref="Timeline.Duration"/>/<see cref="Timeline.RepeatBehavior"/>/
/// <see cref="Timeline.AutoReverse"/>) and pushes the eased, interpolated value to the target. When an animation
/// finishes, <see cref="FillBehavior.Stop"/> releases the target while <see cref="FillBehavior.HoldEnd"/> leaves the
/// final value in place. As in WinUI, a held value stays until its storyboard is stopped: a storyboard that has finished
/// is no longer advanced, but is kept, weakly, for <see cref="Stop"/> to release.
/// </summary>
public sealed class AnimationClock
{
	// Where each of a storyboard's animations is: running (or yet to begin), finished and holding its last value, finished
	// and released (FillBehavior.Stop), or replaced by a later animation of its property, which it then neither writes
	// nor releases.
	private enum Run : byte { Running, Holding, Released, Replaced }

	private sealed class Entry
	{
		public Storyboard? Storyboard; // while it runs
		public WeakReference<Storyboard>? Held; // once it has finished and holds values (see _holding)
		public double Elapsed;
		public int Count; // the storyboard's children when it began
		public Run[] Runs = []; // per child; the first Count are in use
	}

	private readonly List<Entry> _active = [];

	// Storyboards that have finished holding values (FillBehavior.HoldEnd), until Stop releases them, as WinUI's do. Held
	// weakly: a control dropped while one of its visual states holds values is not kept alive for a Stop nothing can call.
	private readonly List<Entry> _holding = [];

	// Entries of finished or stopped storyboards, reused by the next Begin: a visual-state change begins a storyboard
	// on every hover, so starting one allocates nothing once warm.
	private readonly Stack<Entry> _freeEntries = [];

	// Running tweens. A finished tween goes back to its type's pool, so a steady stream of tweens allocates nothing.
	private readonly List<TweenEntry> _tweens = [];

	/// <summary>True while any storyboard or tween is running (lets the host know a repaint may be needed).</summary>
	public bool HasActiveClocks => _active.Count > 0 || _tweens.Count > 0;

	/// <summary>Start (or restart) <paramref name="storyboard"/> from time zero. Each animation with no <c>From</c>
	/// starts at the value its target shows now, so a storyboard that takes over a property mid-animation continues
	/// from where the other left it. Restarting one that is still running releases its targets first, so its animations start,
	/// and are checked, at the values those show once released; one that has finished starts from the end values it holds. As
	/// in WinUI, an animation takes its property over from any other storyboard's that is animating or holding it, which then
	/// leaves the property to it: it stops writing it and does not release it when stopped. The clock compares animations bound
	/// through <c>Target</c> and <c>TargetProperty</c>; one whose delegates were set in code takes nothing over and is never
	/// taken over. A storyboard that can't begin (an animation's target can't be bound, is sealed, or refuses a value it names)
	/// throws before anything changes.</summary>
	public void Begin(Storyboard storyboard) => _active.Add(Start(storyboard));

	/// <summary>Stop <paramref name="storyboard"/> and release every target it animates, whether it is still running or
	/// has finished and holds its values (<see cref="FillBehavior.HoldEnd"/>), as WinUI's <c>Storyboard.Stop</c> does. A
	/// property another storyboard has since taken over (<see cref="Begin"/>) is left to it.</summary>
	public void Stop(Storyboard storyboard) => StopExcept(storyboard, keep: null);

	/// <summary>As <see cref="Stop(Storyboard)"/>, leaving alone each property <paramref name="keep"/> animates: for a visual
	/// state left for one that began on another clock, or with none, which has taken nothing over here.</summary>
	internal void StopExcept(Storyboard storyboard, Storyboard? keep)
	{
		Remove(_active, storyboard, release: true, keep);
		Remove(_holding, storyboard, release: true, keep);
	}

	/// <summary>Begin <paramref name="storyboard"/> at its end, much as WinUI's <c>Storyboard.SkipToFill</c> does: each
	/// animation shows the value at the end of its duration at once, and holds it until <see cref="Stop"/>. A visual state
	/// entered without a transition runs this way, so leaving it releases what it set.</summary>
	internal void SkipToFill(Storyboard storyboard)
	{
		var entry = Start(storyboard);
		var children = storyboard.Children;
		for (var c = 0; c < entry.Count; c++)
		{
			if (children[c] is AnimationTimeline anim)
			{
				anim.ApplyAt(1.0);
				entry.Runs[c] = Run.Holding;
			}
		}
		Settle(entry);
	}

	// Begin's work up to listing the entry. Every animation is checked first (its target bound, the values it names
	// validated), so one that can't begin throws before anything changes: the storyboard is not stopped, nothing is taken
	// over, and what was running or holding still is. A running storyboard's animations are checked against the values its
	// targets show once that run is released. Then a running storyboard is stopped, releasing its targets, while a finished
	// one is let go with its values held, so a restart starts from them as from any value its targets show. Children are
	// walked by index: a foreach over the IList would box an enumerator.
	private Entry Start(Storyboard storyboard)
	{
		CheckStart(storyboard, Running(storyboard));

		Remove(_active, storyboard, release: true);
		Remove(_holding, storyboard, release: false);
		var children = storyboard.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline anim)
				anim.CaptureStart();
		}

		var entry = _freeEntries.Count > 0 ? _freeEntries.Pop() : new Entry();
		entry.Storyboard = storyboard;
		entry.Elapsed = 0d;
		entry.Count = children.Count;
		if (entry.Runs.Length < children.Count)
			entry.Runs = new Run[children.Count];
		else
			Array.Clear(entry.Runs, 0, children.Count);
		TakeOver(children);
		return entry;
	}

	// The entry `storyboard` runs in, when it is running.
	private Entry? Running(Storyboard storyboard)
	{
		for (var i = 0; i < _active.Count; i++)
		{
			if (ReferenceEquals(_active[i].Storyboard, storyboard))
				return _active[i];
		}
		return null;
	}

	/// <summary>Check every animation of <paramref name="storyboard"/> as one (<see cref="AnimationTimeline.CheckStart"/>):
	/// when one can't begin, each animation checked is put back as it was (<see cref="AnimationTimeline.AbandonStart"/>)
	/// before the exception goes on, so a storyboard that can't begin changes nothing.</summary>
	internal static void CheckStart(Storyboard storyboard) => CheckStart(storyboard, null);

	// As CheckStart(Storyboard), where `running` is the entry of the storyboard's run when it is running: each animation of
	// it still driving its property is released before the new run starts, so is checked against the value beneath.
	private static void CheckStart(Storyboard storyboard, Entry? running)
	{
		var children = storyboard.Children;
		var i = 0;
		try
		{
			for (; i < children.Count; i++)
			{
				if (children[i] is AnimationTimeline anim)
					anim.CheckStart(released: running is not null && i < running.Count && running.Runs[i] is Run.Running or Run.Holding);
			}
		}
		catch
		{
			for (var c = 0; c <= i && c < children.Count; c++)
			{
				if (children[c] is AnimationTimeline anim)
					anim.AbandonStart();
			}
			throw;
		}
	}

	// Drop the entries of `list` that run or hold `storyboard`, releasing its targets first when `release` is set, and
	// with them any holding entry whose storyboard has been collected. Kept values are released anyway where the
	// animation's binding has moved to another object, since nothing of this storyboard drives the old one any more. A
	// property `keep` animates is not released.
	private void Remove(List<Entry> list, Storyboard storyboard, bool release, Storyboard? keep = null)
	{
		for (var i = list.Count - 1; i >= 0; i--)
		{
			var entry = list[i];
			var listed = entry.Storyboard ?? Held(entry);
			if (listed is not null && !ReferenceEquals(listed, storyboard))
				continue;
			if (listed is not null)
				Release(entry, listed, movedOnly: !release, keep);
			list.RemoveAt(i);
			Recycle(entry);
		}
	}

	// A storyboard has finished, or skipped to its end: keep its entry while it holds a value, so Stop can release it,
	// else let it go.
	private void Settle(Entry entry)
	{
		if (!Holds(entry))
		{
			Recycle(entry);
			return;
		}
		var storyboard = entry.Storyboard!;
		entry.Storyboard = null;
		if (entry.Held is null)
			entry.Held = new WeakReference<Storyboard>(storyboard);
		else
			entry.Held.SetTarget(storyboard);
		_holding.Add(entry);
	}

	// Keep a dropped entry for reuse, without holding on to its storyboard.
	private void Recycle(Entry entry)
	{
		entry.Storyboard = null;
		entry.Held?.SetTarget(null!);
		_freeEntries.Push(entry);
	}

	// As WinUI hands a property from one animation to the next: each animation of a storyboard beginning now takes its
	// property from any listed animation driving it, running or holding. A holding storyboard left with nothing to hold
	// is let go.
	private void TakeOver(IList<Timeline> children)
	{
		for (var c = 0; c < children.Count; c++)
		{
			if (children[c] is not AnimationTimeline { BoundProperty: not null } anim)
				continue;
			for (var i = 0; i < _active.Count; i++)
				Replace(_active[i], _active[i].Storyboard!, anim);
			for (var i = _holding.Count - 1; i >= 0; i--)
			{
				var entry = _holding[i];
				if (Held(entry) is { } held && (!Replace(entry, held, anim) || Holds(entry)))
					continue;
				_holding.RemoveAt(i);
				Recycle(entry);
			}
		}
	}

	// Mark each animation of the entry that drives the property `by` drives as replaced; true when there was one.
	private static bool Replace(Entry entry, Storyboard storyboard, AnimationTimeline by)
	{
		var replaced = false;
		var children = storyboard.Children;
		for (var c = 0; c < entry.Count; c++)
		{
			if (entry.Runs[c] is Run.Running or Run.Holding && children[c] is AnimationTimeline anim && anim.DrivesSamePropertyAs(by))
			{
				entry.Runs[c] = Run.Replaced;
				replaced = true;
			}
		}
		return replaced;
	}

	// Release each target the entry's storyboard still drives: not one released as it finished (FillBehavior.Stop), nor
	// one a later animation has taken over, nor one `keep` animates. With `movedOnly`, only those whose binding has moved to
	// another object.
	private static void Release(Entry entry, Storyboard storyboard, bool movedOnly = false, Storyboard? keep = null)
	{
		var children = storyboard.Children;
		for (var c = 0; c < entry.Count; c++)
		{
			if (entry.Runs[c] is Run.Running or Run.Holding && children[c] is AnimationTimeline anim && (!movedOnly || anim.BindingMoved)
				&& (keep is null || !Animates(keep, anim)))
				anim.ReleaseTarget();
		}
	}

	/// <summary>Whether one of <paramref name="storyboard"/>'s animations drives the property <paramref name="anim"/>
	/// drives.</summary>
	internal static bool Animates(Storyboard storyboard, AnimationTimeline anim)
	{
		var children = storyboard.Children;
		for (var i = 0; i < children.Count; i++)
		{
			if (children[i] is AnimationTimeline other && other.DrivesSamePropertyAs(anim))
				return true;
		}
		return false;
	}

	private static bool Holds(Entry entry) => Array.IndexOf(entry.Runs, Run.Holding, 0, entry.Count) >= 0;

	private static Storyboard? Held(Entry entry) => entry.Held is { } held && held.TryGetTarget(out var storyboard) ? storyboard : null;

	/// <summary>Advance every active storyboard by <paramref name="deltaSeconds"/> and push the current values.</summary>
	public void Advance(double deltaSeconds)
	{
		for (var i = _active.Count - 1; i >= 0; i--)
		{
			var entry = _active[i];
			entry.Elapsed += deltaSeconds;

			var children = entry.Storyboard!.Children;
			var running = false;
			for (var c = 0; c < entry.Count; c++)
			{
				if (entry.Runs[c] != Run.Running || children[c] is not AnimationTimeline anim)
					continue;

				var (progress, active, completed) = Normalize(anim, entry.Elapsed);
				if (!active)
				{
					running = true; // hasn't begun yet (BeginTime in the future)
				}
				else if (!completed)
				{
					anim.ApplyAt(progress);
					running = true;
				}
				else if (anim.FillBehavior == FillBehavior.Stop)
				{
					anim.ReleaseTarget();
					entry.Runs[c] = Run.Released;
				}
				else
				{
					anim.ApplyAt(progress); // hold the final value
					entry.Runs[c] = Run.Holding;
				}
			}

			if (running)
				continue;
			_active.RemoveAt(i);
			Settle(entry); // HoldEnd values stay applied until Stop releases them
		}

		for (var i = _tweens.Count - 1; i >= 0; i--)
		{
			var tween = _tweens[i];
			if (tween.Cut)
			{
				_tweens.RemoveAt(i);
				tween.Recycle();
				continue;
			}
			if (!tween.Advance(deltaSeconds))
				continue;
			_tweens.RemoveAt(i);
			tween.Complete();
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
		var from = get(target);
		ThrowIfNoStart(from, null); // before a running tween is taken over or a new one listed
		SetterTween<TTarget, T>? tween = null;
		for (var i = 0; tween is null && i < _tweens.Count; i++)
			if (_tweens[i] is SetterTween<TTarget, T> running && running.Matches(target, set))
				tween = running;
		if (tween is null)
			_tweens.Add(tween = SetterTween<TTarget, T>.Rent());
		tween.Start(target, set, lerp, from, to, duration, easing);
	}

	/// <summary>
	/// Animate a dependency property from the value on screen to <paramref name="to"/>, as a WinUI property transition
	/// (<c>OpacityTransition</c>, <c>BrushTransition</c>) animates a property that is set. <paramref name="to"/> is checked
	/// and becomes the property's local value at once, so <see cref="DependencyObject.GetValue{TOwner, TValue}"/> reads it
	/// from the start: like any local value it replaces the one set before and outranks a style's. Only what is shown
	/// moves toward it, on a transition value just above the local one and below a storyboard's, which is never checked
	/// frame by frame, so an easing that overshoots the property's valid range never throws mid-tween; a storyboard holding
	/// the property outranks the tween as it outranks a local value, and the tween ending never disturbs it. A local
	/// change to the property while it tweens ends the tween and shows at once; another tween of it takes over from the
	/// value on screen. Otherwise as the delegate-based
	/// <see cref="Tween{TTarget, T}(TTarget, Func{TTarget, T}, Action{TTarget, T}, T, TimeSpan, IEasingFunction, Lerp{T})"/>.
	/// The property must be animatable.
	/// </summary>
	public void Tween<TOwner, T>(DependencyObject target, DependencyProperty<TOwner, T> property, T to, TimeSpan duration,
		IEasingFunction? easing = null)
		where TOwner : DependencyObject
	{
		var lerp = property.Lerp ?? throw new ArgumentException($"{property.Name} is not animatable; its type has no interpolation.", nameof(property));
		var from = target.GetTransitionStart(property);
		ThrowIfNoStart(from, property); // before a running tween is taken over or a new one listed
		if (property.Metadata.Validate is { } validate && target is TOwner owner)
			validate(owner, to); // the start is what is on screen, and is not checked
		PropertyTween<TOwner, T>? running = null;
		for (var i = 0; running is null && i < _tweens.Count; i++)
			if (_tweens[i] is PropertyTween<TOwner, T> tween && tween.Matches(target, property))
				running = tween;
		var started = running ?? PropertyTween<TOwner, T>.Rent();
		started.Start(target, property, lerp, from, to, duration, easing);
		if (running is null)
			_tweens.Add(started);
	}

	/// <summary>Stop every tween running on <paramref name="target"/>. A delegate-based tween leaves its property where it
	/// is; a dependency property shows the local value its tween set, as a WinUI transition cut short lands on the value
	/// that was set.</summary>
	public void StopTweens(object target)
	{
		for (var i = _tweens.Count - 1; i >= 0; i--)
		{
			var tween = _tweens[i];
			if (!tween.Targets(target))
				continue;
			_tweens.RemoveAt(i);
			tween.Stop();
			tween.Recycle();
		}
	}

	// A float or double tween starts from the value now, which must be a number: a size left Auto is NaN, and a MaxWidth is
	// infinite by default, and interpolating from either never arrives. The type tests fold away, so nothing is boxed.
	private static void ThrowIfNoStart<T>(T from, DependencyProperty? property)
	{
		if (typeof(T) == typeof(float) ? !float.IsFinite(Unsafe.As<T, float>(ref from))
			: typeof(T) == typeof(double) && !double.IsFinite(Unsafe.As<T, double>(ref from)))
			throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
				$"A tween starts from {property?.Name ?? "the property"}'s value now, which is {from}" +
				$"{(from is float.NaN or double.NaN ? " (Auto, for a size)" : "")}: set it to a number first."));
	}

	// One running tween: its timing and easing here, its target and values in a typed subclass. Subclasses are pooled
	// per closed type, thread-static because a clock runs on its host's thread.
	private abstract class TweenEntry
	{
		private double _elapsed;
		private double _duration;
		private IEasingFunction? _easing;

		public abstract bool Targets(object target);

		// True once something other than the clock has ended the tween; it is then dropped without writing again.
		public virtual bool Cut => false;

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

		// Called once the end value has been written, before Recycle; and in its place when the tween is stopped part way.
		public virtual void Complete()
		{
		}

		public virtual void Stop()
		{
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

		// The end value becomes the local value at once, checked as any set is; the transition rung above it shows the value
		// on screen until the first frame moves it.
		public void Start(DependencyObject target, DependencyProperty<TOwner, T> property, Lerp<T> lerp, T from, T to, TimeSpan duration, IEasingFunction? easing)
		{
			target.BeginTransition(property, from, to);
			(_target, _property, _lerp, _from, _to) = (target, property, lerp, from, to);
			Restart(duration, easing);
		}

		public override bool Targets(object target) => ReferenceEquals(_target, target);

		// A local change to the property clears the transition rung, which ends the tween.
		public override bool Cut => !_target!.HasTransitionValue(_property!);

		protected override void Write(float progress) => _target!.SetTransitionValue(_property!, _lerp!(_from, _to, progress));

		// The end value is the local value already: the transition rung is cleared, and the property shows it.
		public override void Complete() => _target!.ClearTransitionValue(_property!);

		// Cut short, the property lands on the value it was set to, as a WinUI transition cut short does.
		public override void Stop() => _target!.ClearTransitionValue(_property!);

		protected override void ReturnToPool()
		{
			(_target, _property, _lerp, _from, _to) = (null, null, null, default!, default!);
			(t_pool ??= new()).Push(this);
		}
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
