namespace SUIsei;

/// <summary>
/// The base of everything that carries dependency properties. Holds a sparse, int-keyed
/// effective-value store and resolves the WinUI value-precedence ladder:
/// <para><c>Animation &gt; Local &gt; Template &gt; Style &gt; Inherited &gt; Default</c></para>
/// The Template rung holds what a <c>{TemplateBinding}</c> pushes onto a template part, so a Local value an app sets
/// on that part wins over the template, as in WinUI.
/// A set recomputes the top of the ladder, coerces it, fires the typed change callback only when the
/// <em>coerced resolved</em> value changes, and ORs the property's <see cref="InvalidationFlags"/> into the element (via
/// <see cref="OnPropertyInvalidated"/>). Reflection-free: dispatch goes through the typed delegates on
/// <see cref="DependencyProperty{TOwner, TValue}"/>.
/// <para>
/// The store is typed: each property's rungs live in a slot of the property's own value type, made on the first write
/// of that property to this object. After that a write, its coercion, its change check and its change callback never
/// box, so animating a value-type property every frame allocates nothing.
/// </para>
/// </summary>
public abstract class DependencyObject
{
	/// <summary>A rung of the precedence ladder below the default, highest first.</summary>
	internal enum ValueRung { Animation = 0, Local = 1, Template = 2, Style = 3, Inherited = 4 }

	// One property's value on each rung, with a bitmask of the rungs that hold one.
	private abstract class ValueSlot
	{
		public byte Set;

		public bool Has(ValueRung r) => (Set & (1 << (int)r)) != 0;
	}

	private sealed class ValueSlot<T> : ValueSlot
	{
		private T _animation = default!, _local = default!, _template = default!, _style = default!, _inherited = default!;

		public void Put(ValueRung r, T value, bool has)
		{
			switch (r)
			{
				case ValueRung.Animation: _animation = value; break;
				case ValueRung.Local: _local = value; break;
				case ValueRung.Template: _template = value; break;
				case ValueRung.Style: _style = value; break;
				default: _inherited = value; break;
			}
			if (has) Set |= (byte)(1 << (int)r);
			else Set &= (byte)~(1 << (int)r);
		}

		public bool TryResolve(out T value)
		{
			if (Has(ValueRung.Animation)) { value = _animation; return true; }
			if (Has(ValueRung.Local)) { value = _local; return true; }
			if (Has(ValueRung.Template)) { value = _template; return true; }
			if (Has(ValueRung.Style)) { value = _style; return true; }
			if (Has(ValueRung.Inherited)) { value = _inherited; return true; }
			value = default!;
			return false;
		}
	}

	private Dictionary<int, ValueSlot>? _slots;

	// {TemplateBinding} targets keyed by the source DP: a OneWay push from this object's property to a child setter,
	// fired on every resolved-value change (and once at registration). Each is an Action<TValue> of its property's
	// value type. Only allocated when a template wires one.
	private Dictionary<int, Delegate>? _templateTargets;

	/// <summary>Read the property's resolved value (top of the precedence ladder, else its typed default), coerced.</summary>
	public TValue GetValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp) where TOwner : DependencyObject =>
		Effective(dp, Slot(dp));

	/// <summary>Write the <b>Local</b> rung (the public set). Never clobbers an active Animation value — animation
	/// still wins until cleared.</summary>
	public void SetValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, ValueRung.Local, value, hasValue: true);

	/// <summary>Push the <b>Animation</b> rung (the top of the ladder) — the storyboard/independent-animation path.</summary>
	internal void SetAnimatedValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, ValueRung.Animation, value, hasValue: true);

	/// <summary>Clear the animation rung (FillBehavior.Stop / storyboard end) — reverts to Local/Style/Inherited/Default.</summary>
	internal void ClearAnimatedValue(DependencyProperty dp) => dp.ClearRung(this, ValueRung.Animation);

	/// <summary>Push the <b>Style</b> rung (applied by a Style; below Local). The setter's value arrives boxed and is
	/// unboxed once into the typed store.</summary>
	internal void SetStyleValue(DependencyProperty dp, object? value) => dp.SetRungBoxed(this, ValueRung.Style, value);

	/// <summary>Clear the <b>Style</b> rung (called when a new style replaces an old one so stale setters are removed).</summary>
	internal void ClearStyleValue(DependencyProperty dp) => dp.ClearRung(this, ValueRung.Style);

	/// <summary>Push the <b>Template</b> rung (a <c>{TemplateBinding}</c> onto a template part; below Local, above Style).</summary>
	internal void SetTemplateValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, ValueRung.Template, value, hasValue: true);

	/// <summary>Clear the <b>Template</b> rung.</summary>
	internal void ClearTemplateValue(DependencyProperty dp) => dp.ClearRung(this, ValueRung.Template);

	/// <summary>Push the <b>Inherited</b> rung (property-value inheritance; below Style).</summary>
	internal void SetInheritedValue(DependencyProperty dp, object? value) => dp.SetRungBoxed(this, ValueRung.Inherited, value);

	/// <summary>True if a Local value is currently set (independent of the resolved value).</summary>
	internal bool HasLocalValue(DependencyProperty dp) =>
		_slots is not null && _slots.TryGetValue(dp.GlobalIndex, out var s) && s.Has(ValueRung.Local);

	/// <summary>Write (or, with <paramref name="hasValue"/> false, clear) one rung, then raise the change if the
	/// coerced resolved value moved.</summary>
	internal void SetRung<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, ValueRung rung, TValue value, bool hasValue)
		where TOwner : DependencyObject
	{
		var slot = Slot(dp);
		if (slot is null)
		{
			if (!hasValue)
				return; // clearing a rung of a property this object never held changes nothing
			_slots ??= [];
			_slots[dp.GlobalIndex] = slot = new ValueSlot<TValue>();
		}

		var oldValue = Effective(dp, slot);
		slot.Put(rung, value, hasValue); // store the raw base value; coercion is applied to the resolved value
		var newValue = Effective(dp, slot);

		if (EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
			return;

		dp.Metadata.PropertyChanged?.Invoke((TOwner)this, oldValue, newValue);
		if (dp.Invalidates != InvalidationFlags.None)
			OnPropertyInvalidated(dp, dp.Invalidates);
		if (_templateTargets is not null && _templateTargets.TryGetValue(dp.GlobalIndex, out var push))
			((Action<TValue>)push)(newValue);
	}

	/// <summary>Wire a OneWay <c>{TemplateBinding}</c> from <paramref name="source"/> on this object to a child setter.
	/// The child is updated immediately with the current value and on every later change, with no boxing. This is the form
	/// for a target that is not a dependency property, which has no Template rung for the value to land on; a dependency
	/// property target takes the overload that writes its Template rung.
	/// Public so a generated (or hand-written) template factory can wire its template bindings without reflection.</summary>
	public void RegisterTemplateBinding<TOwner, TValue>(DependencyProperty<TOwner, TValue> source, Action<TValue> push)
		where TOwner : DependencyObject
	{
		_templateTargets ??= [];
		_templateTargets[source.GlobalIndex] = _templateTargets.TryGetValue(source.GlobalIndex, out var existing)
			? (Action<TValue>)existing + push
			: push;
		push(GetValue(source));
	}

	/// <summary>Wire a OneWay <c>{TemplateBinding}</c> from <paramref name="source"/> on this object to
	/// <paramref name="targetProperty"/> on a template part. The part's <b>Template</b> rung is written now and on every
	/// later change, so a Local value set on the part still wins, as in WinUI. Public so a generated (or hand-written)
	/// template factory can wire its template bindings without reflection.</summary>
	/// <param name="source">The templated control's property the value flows from.</param>
	/// <param name="target">The template part the value flows to.</param>
	/// <param name="targetProperty">The part's property, of the same value type as <paramref name="source"/>.</param>
	public void RegisterTemplateBinding<TOwner, TValue, TTargetOwner>(DependencyProperty<TOwner, TValue> source,
		DependencyObject target, DependencyProperty<TTargetOwner, TValue> targetProperty)
		where TOwner : DependencyObject
		where TTargetOwner : DependencyObject =>
		RegisterTemplateBinding(source, value => target.SetTemplateValue(targetProperty, value));

	/// <summary>Wire a OneWay <c>{TemplateBinding}</c> between properties of different value types: as
	/// <see cref="RegisterTemplateBinding{TOwner, TValue, TTargetOwner}(DependencyProperty{TOwner, TValue}, DependencyObject, DependencyProperty{TTargetOwner, TValue})"/>,
	/// with each value passed through <paramref name="convert"/> on its way to the part's <b>Template</b> rung.</summary>
	/// <param name="source">The templated control's property the value flows from.</param>
	/// <param name="target">The template part the value flows to.</param>
	/// <param name="targetProperty">The part's property.</param>
	/// <param name="convert">Converts a source value to the part's value type.</param>
	public void RegisterTemplateBinding<TOwner, TValue, TTargetOwner, TTargetValue>(DependencyProperty<TOwner, TValue> source,
		DependencyObject target, DependencyProperty<TTargetOwner, TTargetValue> targetProperty, Func<TValue, TTargetValue> convert)
		where TOwner : DependencyObject
		where TTargetOwner : DependencyObject =>
		RegisterTemplateBinding(source, value => target.SetTemplateValue(targetProperty, convert(value)));

	private ValueSlot<TValue>? Slot<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp) where TOwner : DependencyObject =>
		_slots is not null && _slots.TryGetValue(dp.GlobalIndex, out var s) ? (ValueSlot<TValue>)s : null;

	// The coerced effective value: resolve the precedence ladder (else the typed default), then coerce. Coercion
	// applies to the resolved value, not per rung at write time — so a coercion that depends on another property
	// reflects that property's current value on every read.
	private TValue Effective<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, ValueSlot<TValue>? slot)
		where TOwner : DependencyObject
	{
		var baseValue = slot is not null && slot.TryResolve(out var v) ? v : dp.Metadata.DefaultValue;
		return dp.Metadata.Coerce is { } coerce ? coerce((TOwner)this, baseValue) : baseValue;
	}

	/// <summary>Hook for a resolved-value change that carries <see cref="InvalidationFlags"/> — <see cref="UIElement"/>
	/// overrides it to schedule measure/arrange/render. Default no-op (non-visual dependency objects).</summary>
	protected virtual void OnPropertyInvalidated(DependencyProperty dp, InvalidationFlags flags) { }
}
