namespace SUIsei;

/// <summary>
/// The base of everything that carries dependency properties (PlanningUI.md §D1.2). Holds a sparse, int-keyed
/// effective-value store and resolves the WinUI value-precedence ladder:
/// <para><c>Animation &gt; Local &gt; Style &gt; Inherited &gt; Default</c></para>
/// A set recomputes the top of the ladder, coerces it, fires the typed change callback only when the
/// <em>coerced resolved</em> value changes, and ORs the property's <see cref="InvalidationFlags"/> into the element (via
/// <see cref="OnPropertyInvalidated"/>). Reflection-free: dispatch goes through the typed delegates on
/// <see cref="DependencyProperty{TOwner, TValue}"/>. The animation rung exists from day one (a hard prerequisite
/// for storyboards, MU4) even though nothing drives it until then.
/// </summary>
public abstract class DependencyObject
{
	// One precedence rung's slot. We box value types here (the general store); the typed get/set API keeps the
	// public surface boxing-free, and a boxing-free value-type fast path is a later optimization (PlanningUI.md §D1.1).
	private enum Rung { Animation = 0, Local = 1, Style = 2, Inherited = 3 }

	private struct ValueSlot
	{
		public object? Animation, Local, Style, Inherited;
		public byte Set; // bitmask over Rung

		public readonly bool Has(Rung r) => (Set & (1 << (int)r)) != 0;

		public void Put(Rung r, object? value, bool has)
		{
			switch (r)
			{
				case Rung.Animation: Animation = value; break;
				case Rung.Local: Local = value; break;
				case Rung.Style: Style = value; break;
				default: Inherited = value; break;
			}
			if (has) Set |= (byte)(1 << (int)r);
			else Set &= (byte)~(1 << (int)r);
		}

		public readonly bool TryResolve(out object? value)
		{
			if (Has(Rung.Animation)) { value = Animation; return true; }
			if (Has(Rung.Local)) { value = Local; return true; }
			if (Has(Rung.Style)) { value = Style; return true; }
			if (Has(Rung.Inherited)) { value = Inherited; return true; }
			value = null;
			return false;
		}
	}

	private Dictionary<int, ValueSlot>? _slots;

	/// <summary>Read the property's resolved value (top of the precedence ladder, else its typed default), coerced.
	/// No boxing on the default path; a stored value is unboxed once on return.</summary>
	public TValue GetValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp) where TOwner : DependencyObject
	{
		var baseValue = _slots is not null && _slots.TryGetValue(dp.GlobalIndex, out var slot) && slot.TryResolve(out var v)
			? (TValue)v!
			: dp.Metadata.DefaultValue;
		// Coercion applies to the resolved (effective) value, not per rung at write time — so a coercion that
		// depends on another property reflects that property's current value on every read. No-op (and boxing-free)
		// when no coerce callback is registered.
		return dp.Metadata.Coerce is { } coerce ? coerce((TOwner)this, baseValue) : baseValue;
	}

	/// <summary>Write the <b>Local</b> rung (the public set). Never clobbers an active Animation value — animation
	/// still wins until cleared (MU4 correctness, PlanningUI.md §D1.2).</summary>
	public void SetValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, Rung.Local, value, hasValue: true);

	/// <summary>Push the <b>Animation</b> rung (the top of the ladder) — the storyboard/independent-animation path (MU4).</summary>
	internal void SetAnimatedValue(DependencyProperty dp, object? value) => SetRung(dp, Rung.Animation, value, hasValue: true);

	/// <summary>Clear the animation rung (FillBehavior.Stop / storyboard end) — reverts to Local/Style/Inherited/Default.</summary>
	internal void ClearAnimatedValue(DependencyProperty dp) => SetRung(dp, Rung.Animation, null, hasValue: false);

	/// <summary>Push the <b>Style</b> rung (applied by a Style; below Local) — MU3.</summary>
	internal void SetStyleValue(DependencyProperty dp, object? value) => SetRung(dp, Rung.Style, value, hasValue: true);

	/// <summary>Clear the <b>Style</b> rung (called when a new style replaces an old one so stale setters are removed).</summary>
	internal void ClearStyleValue(DependencyProperty dp) => SetRung(dp, Rung.Style, null, hasValue: false);

	/// <summary>Push the <b>Inherited</b> rung (property-value inheritance; below Style) — MU2+.</summary>
	internal void SetInheritedValue(DependencyProperty dp, object? value) => SetRung(dp, Rung.Inherited, value, hasValue: true);

	/// <summary>True if a Local value is currently set (independent of the resolved value).</summary>
	internal bool HasLocalValue(DependencyProperty dp) =>
		_slots is not null && _slots.TryGetValue(dp.GlobalIndex, out var s) && s.Has(Rung.Local);

	private void SetRung(DependencyProperty dp, Rung rung, object? value, bool hasValue)
	{
		_slots ??= [];
		_slots.TryGetValue(dp.GlobalIndex, out var slot);

		var oldEffective = Effective(dp, slot);
		slot.Put(rung, value, hasValue); // store the raw base value; coercion is applied to the resolved value
		_slots[dp.GlobalIndex] = slot;
		var newEffective = Effective(dp, slot);

		if (Equals(oldEffective, newEffective))
			return;

		dp.RaiseChanged(this, oldEffective, newEffective);
		if (dp.Invalidates != InvalidationFlags.None)
			OnPropertyInvalidated(dp, dp.Invalidates);
	}

	// The coerced effective value of a slot: resolve the precedence ladder (else the typed default), then coerce.
	private object? Effective(DependencyProperty dp, ValueSlot slot)
	{
		var baseValue = slot.TryResolve(out var v) ? v : dp.DefaultValueBoxed;
		return dp.CoerceBoxed(this, baseValue);
	}

	/// <summary>Hook for a resolved-value change that carries <see cref="InvalidationFlags"/> — <see cref="UIElement"/>
	/// overrides it to schedule measure/arrange/render. Default no-op (non-visual dependency objects).</summary>
	protected virtual void OnPropertyInvalidated(DependencyProperty dp, InvalidationFlags flags) { }
}
