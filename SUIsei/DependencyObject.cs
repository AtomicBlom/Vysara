using System.Numerics;

namespace SUIsei;

/// <summary>
/// The base of everything that carries dependency properties. Holds a sparse, int-keyed
/// effective-value store and resolves the WinUI value-precedence ladder:
/// <para><c>Animation &gt; Transition &gt; Local &gt; Template &gt; Style &gt; BuiltInStyle &gt; Inherited &gt; Default</c></para>
/// The Transition rung holds what a tween shows on its way to the local value it set, as a WinUI property transition
/// shows it; a local change ends it. The Template rung holds what a <c>{TemplateBinding}</c> pushes onto a template part, so a Local value an app sets
/// on that part wins over the template, as in WinUI. The BuiltInStyle rung holds what a control gives itself, the way
/// WinUI's default style does: its built-in template, and that a control is a tab stop. An app's style or its own
/// value overrides either, and a diagnostics read can tell them from the app's markup.
/// A set recomputes the top of the ladder, coerces it, fires the typed change callback only when the
/// <em>coerced resolved</em> value changes, and ORs the property's <see cref="InvalidationFlags"/> into the element (via
/// <see cref="OnPropertyInvalidated"/>). A property WinUI inherits (a text property such as <c>FontSize</c>) then passes
/// the new value down to the elements below that hold it, as their Inherited value. Reflection-free: dispatch goes
/// through the typed delegates on <see cref="DependencyProperty{TOwner, TValue}"/>.
/// <para>
/// The store is typed: each property's rungs live in a slot of the property's own value type, made on the first write
/// of that property to this object. After that a write, its coercion, its change check and its change callback never
/// box, so animating a value-type property every frame allocates nothing.
/// </para>
/// <para>
/// A field-backed property (SUIsei's layout properties, such as <c>Width</c>) keeps its resolved value in a field of its
/// owner, which a read returns. Its local value lives there too until a style, inherited or animated value makes it a
/// slot, so an element whose layout only code or markup set holds no store at all for it.
/// </para>
/// </summary>
public abstract class DependencyObject
{
	/// <summary>A rung of the precedence ladder below the default, highest first. <see cref="Transition"/> holds what a
	/// tween shows on its way to the local value it set, as a WinUI property transition shows: below a storyboard's value,
	/// above every value set.</summary>
	internal enum ValueRung { Animation = 0, Transition = 1, Local = 2, Template = 3, Style = 4, BuiltInStyle = 5, Inherited = 6 }

	// One property's value on each rung, with a bitmask of the rungs that hold one.
	private abstract class ValueSlot
	{
		public byte Set;

		public bool Has(ValueRung r) => (Set & (1 << (int)r)) != 0;
	}

	private sealed class ValueSlot<T> : ValueSlot
	{
		private T _animation = default!, _transition = default!, _local = default!, _template = default!, _style = default!, _builtInStyle = default!,
			_inherited = default!;

		public void Put(ValueRung r, T value, bool has)
		{
			switch (r)
			{
				case ValueRung.Animation: _animation = value; break;
				case ValueRung.Transition: _transition = value; break;
				case ValueRung.Local: _local = value; break;
				case ValueRung.Template: _template = value; break;
				case ValueRung.Style: _style = value; break;
				case ValueRung.BuiltInStyle: _builtInStyle = value; break;
				default: _inherited = value; break;
			}
			if (has) Set |= (byte)(1 << (int)r);
			else Set &= (byte)~(1 << (int)r);
		}

		public bool TryResolve(out T value, bool belowAnimation = false)
		{
			if (!belowAnimation && Has(ValueRung.Animation)) { value = _animation; return true; }
			if (Has(ValueRung.Transition)) { value = _transition; return true; }
			if (Has(ValueRung.Local)) { value = _local; return true; }
			if (Has(ValueRung.Template)) { value = _template; return true; }
			if (Has(ValueRung.Style)) { value = _style; return true; }
			if (Has(ValueRung.BuiltInStyle)) { value = _builtInStyle; return true; }
			if (Has(ValueRung.Inherited)) { value = _inherited; return true; }
			value = default!;
			return false;
		}
	}

	private Dictionary<int, ValueSlot>? _slots;

	// The field-backed properties (PropertyMetadata.Field) whose local value is in the owner's field rather than a slot, by
	// DependencyProperty.FieldBit. A property that has a slot keeps its local value there instead.
	private ulong _fieldLocals;

	// {TemplateBinding} targets keyed by the source DP: a OneWay push from this object's property to a child setter,
	// fired on every resolved-value change (and once at registration). Each is an Action<TValue> of its property's
	// value type. Only allocated when a template wires one.
	private Dictionary<int, Delegate>? _templateTargets;

	/// <summary>Read the property's resolved value (top of the precedence ladder, else its typed default), coerced. A
	/// field-backed property's field holds it already.</summary>
	public TValue GetValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp) where TOwner : DependencyObject =>
		dp.FieldBit != 0 ? dp.Metadata.Field!((TOwner)this) : Effective(dp, Slot(dp));

	/// <summary>Write the <b>Local</b> rung (the public set). Never clobbers an active Animation value — animation
	/// still wins until cleared.</summary>
	public void SetValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, ValueRung.Local, value, hasValue: true);

	/// <summary>Clear the <b>Local</b> rung, as WinUI's <c>ClearValue</c> does: the property reads its style, inherited or
	/// default value again (an animation still wins while it runs).</summary>
	public void ClearValue(DependencyProperty dp) => dp.ClearRung(this, ValueRung.Local);

	/// <summary>Push the <b>Animation</b> rung (the top of the ladder) — the storyboard/independent-animation path.</summary>
	internal void SetAnimatedValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, ValueRung.Animation, value, hasValue: true);

	/// <summary>Clear the animation rung (FillBehavior.Stop / storyboard end) — reverts to Local/Style/Inherited/Default.</summary>
	internal void ClearAnimatedValue(DependencyProperty dp) => dp.ClearRung(this, ValueRung.Animation);

	/// <summary>The value a tween of <paramref name="dp"/> starts from: what is shown below any storyboard holding it (a
	/// running tween's value, else the property's own).</summary>
	internal TValue GetTransitionStart<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp) where TOwner : DependencyObject =>
		Slot(dp) is { } slot ? Effective(dp, slot, belowAnimation: true) : GetValue(dp);

	/// <summary>Start a tween's transition: <paramref name="target"/> becomes the <b>Local</b> value now, checked as any
	/// set is, and the <b>Transition</b> rung above it holds <paramref name="shown"/>, what is on screen, so nothing moves
	/// until the tween writes its first value.</summary>
	internal void BeginTransition<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue shown, TValue target) where TOwner : DependencyObject
	{
		if (dp.Metadata.Validate is { } validate)
			validate((TOwner)this, target); // before anything changes
		SetRung(dp, ValueRung.Transition, shown, hasValue: true);
		SetRung(dp, ValueRung.Local, target, hasValue: true, keepTransition: true);
	}

	/// <summary>Write the <b>Transition</b> rung: a tween's value on its way to the local value. Never checked, so an easing
	/// that overshoots the property's range never throws.</summary>
	internal void SetTransitionValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, ValueRung.Transition, value, hasValue: true);

	/// <summary>Clear the <b>Transition</b> rung: the tween has ended, and the property shows its local value (or a
	/// storyboard's above it).</summary>
	internal void ClearTransitionValue(DependencyProperty dp) => dp.ClearRung(this, ValueRung.Transition);

	/// <summary>Whether a tween's value is on the <b>Transition</b> rung: false once a local change has ended it.</summary>
	internal bool HasTransitionValue(DependencyProperty dp) =>
		_slots is not null && _slots.TryGetValue(dp.GlobalIndex, out var s) && s.Has(ValueRung.Transition);

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

	/// <summary>Write the <b>BuiltInStyle</b> rung: a value a control gives itself, as WinUI's default style does (below
	/// Style, above Inherited), so the app's style or its own value overrides it.</summary>
	internal void SetBuiltInValue<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, TValue value) where TOwner : DependencyObject =>
		SetRung(dp, ValueRung.BuiltInStyle, value, hasValue: true);

	/// <summary>Push the <b>Inherited</b> rung (property-value inheritance; below Style).</summary>
	internal void SetInheritedValue(DependencyProperty dp, object? value) => dp.SetRungBoxed(this, ValueRung.Inherited, value);

	/// <summary>True if a Local value is currently set (independent of the resolved value).</summary>
	internal bool HasLocalValue(DependencyProperty dp) =>
		(_fieldLocals & dp.FieldBit) != 0
		|| (_slots is not null && _slots.TryGetValue(dp.GlobalIndex, out var s) && s.Has(ValueRung.Local));

	/// <summary>The highest rung holding a value for <paramref name="dp"/>, or null when it resolves to its default.</summary>
	internal ValueRung? TopRung(DependencyProperty dp)
	{
		if ((_fieldLocals & dp.FieldBit) != 0)
			return ValueRung.Local; // a local value held in its field, which means the property has no slot
		if (_slots is null || !_slots.TryGetValue(dp.GlobalIndex, out var slot))
			return null;
		for (var rung = ValueRung.Animation; rung <= ValueRung.Inherited; rung++)
		{
			if (slot.Has(rung))
				return rung;
		}
		return null;
	}

	/// <summary>The <see cref="DependencyProperty.GlobalIndex"/> of each property this object holds a value for on some
	/// rung, a field-backed property's local value in its field included.</summary>
	internal int[] StoredPropertyIndices()
	{
		var indices = _slots is null ? [] : _slots.Where(static s => s.Value.Set != 0).Select(static s => s.Key).ToList();
		for (var locals = _fieldLocals; locals != 0; locals &= locals - 1)
			indices.Add(DependencyProperty.FieldGlobalIndex(BitOperations.TrailingZeroCount(locals)));
		return [.. indices];
	}

	/// <summary>True if any rung holds a value, a field-backed property's local value in its field included, so the
	/// property reads something other than its default.</summary>
	internal bool HasAnyValue(DependencyProperty dp) =>
		(_fieldLocals & dp.FieldBit) != 0
		|| (_slots is not null && _slots.TryGetValue(dp.GlobalIndex, out var s) && s.Set != 0);

	/// <summary>Write (or, with <paramref name="hasValue"/> false, clear) one rung, then raise the change if the
	/// coerced resolved value moved. A change to the local value ends a tween in progress on the property, so it shows at
	/// once, as a set without a transition does; <paramref name="keepTransition"/> is for the tween setting it.</summary>
	internal void SetRung<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, ValueRung rung, TValue value, bool hasValue,
		bool keepTransition = false)
		where TOwner : DependencyObject
	{
		if (IsSealed)
			throw new InvalidOperationException(
				$"This {GetType().Name} is part of SUIsei's built-in look, shared by every control that shows it, and cannot change; " +
				$"give the element one of its own to change its {dp.Name}.");
		// Only a value set on this object is checked. An animation's values are checked as it begins, never frame by frame;
		// an inherited or template-bound value comes from another property, checked where it was set there, and an object
		// that can't draw it draws what it can.
		if (hasValue && rung is (ValueRung.Local or ValueRung.Style or ValueRung.BuiltInStyle) && dp.Metadata.Validate is { } validate)
			validate((TOwner)this, value);
		var slot = Slot(dp);
		if (slot is null)
		{
			if (rung == ValueRung.Local && dp.Metadata.Field is { } fieldOf)
			{
				SetLocalField(dp, fieldOf, value, hasValue);
				return;
			}
			if (!hasValue)
				return; // clearing a rung of a property this object never held changes nothing
			_slots ??= [];
			_slots[dp.GlobalIndex] = slot = new ValueSlot<TValue>();
			if ((_fieldLocals & dp.FieldBit) != 0)
			{
				// The local value moves from the field into the slot, which ranks it against the rung being written.
				_fieldLocals &= ~dp.FieldBit;
				slot.Put(ValueRung.Local, dp.Metadata.Field!((TOwner)this), true);
			}
		}

		var oldValue = Effective(dp, slot);
		slot.Put(rung, value, hasValue); // store the raw base value; coercion is applied to the resolved value
		if (rung == ValueRung.Local && !keepTransition && slot.Has(ValueRung.Transition))
			slot.Put(ValueRung.Transition, default!, false);
		var newValue = Effective(dp, slot);

		if (EqualityComparer<TValue>.Default.Equals(oldValue, newValue))
			return;

		if (dp.Metadata.Field is { } resolvedField)
			resolvedField((TOwner)this) = newValue;
		dp.Metadata.PropertyChanged?.Invoke((TOwner)this, oldValue, newValue);
		if (dp.Invalidates != InvalidationFlags.None)
			OnPropertyInvalidated(dp, dp.Invalidates);
		if (_templateTargets is not null && _templateTargets.TryGetValue(dp.GlobalIndex, out var push))
			((Action<TValue>)push)(newValue);
		dp.Metadata.Inherits?.PassDown(this, newValue, slot.Set != 0);
	}

	// The local value of a field-backed property that holds no slot: it lives in the owner's field, and its bit in
	// _fieldLocals records that it was set, so a style or animated value written later still ranks below it. The change is
	// raised as SetRung raises it.
	private void SetLocalField<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, FieldAccessor<TOwner, TValue> fieldOf, TValue value, bool hasValue)
		where TOwner : DependencyObject
	{
		if (hasValue)
			_fieldLocals |= dp.FieldBit;
		else if ((_fieldLocals & dp.FieldBit) == 0)
			return;
		else
		{
			_fieldLocals &= ~dp.FieldBit;
			value = dp.Metadata.DefaultValue;
		}

		ref var stored = ref fieldOf((TOwner)this);
		var oldValue = stored;
		if (EqualityComparer<TValue>.Default.Equals(oldValue, value))
			return;
		stored = value;
		dp.Metadata.PropertyChanged?.Invoke((TOwner)this, oldValue, value);
		if (dp.Invalidates != InvalidationFlags.None)
			OnPropertyInvalidated(dp, dp.Invalidates);
		if (_templateTargets is not null && _templateTargets.TryGetValue(dp.GlobalIndex, out var push))
			((Action<TValue>)push)(value);
		dp.Metadata.Inherits?.PassDown(this, value, hasValue);
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

	// The coerced effective value: resolve the precedence ladder (else the default, this object's own where its type
	// sets one), then coerce. Coercion applies to the resolved value, not per rung at write time — so a coercion that
	// depends on another property reflects that property's current value on every read.
	private TValue Effective<TOwner, TValue>(DependencyProperty<TOwner, TValue> dp, ValueSlot<TValue>? slot, bool belowAnimation = false)
		where TOwner : DependencyObject
	{
		var metadata = dp.Metadata;
		var baseValue = slot is not null && slot.TryResolve(out var v, belowAnimation) ? v
			: metadata.DefaultValueFor is { } defaultFor ? defaultFor((TOwner)this) : metadata.DefaultValue;
		return metadata.Coerce is { } coerce ? coerce((TOwner)this, baseValue) : baseValue;
	}

	/// <summary>
	/// Whether this object can no longer change: every write to it throws. SUIsei seals the brushes of its built-in look,
	/// which every control showing that look shares (across UI threads, too), so they need no change listeners and nothing
	/// can recolour them for every control at once by accident.
	/// </summary>
	internal bool IsSealed { get; private set; }

	/// <summary>Make this object unchangeable from now on (<see cref="IsSealed"/>).</summary>
	internal void Seal() => IsSealed = true;

	/// <summary>Hook for a resolved-value change that carries <see cref="InvalidationFlags"/> — <see cref="UIElement"/>
	/// overrides it to schedule measure/arrange/render. Default no-op (non-visual dependency objects).</summary>
	protected virtual void OnPropertyInvalidated(DependencyProperty dp, InvalidationFlags flags) { }
}
