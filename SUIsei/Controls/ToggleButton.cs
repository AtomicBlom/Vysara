namespace SUIsei.Controls;

/// <summary>
/// A <see cref="ButtonBase"/> that holds a checked state (PlanningUI.md MU6) — the base for <see cref="CheckBox"/>.
/// A click cycles <see cref="IsChecked"/> (two-state, or three-state through <c>null</c> when
/// <see cref="IsThreeState"/> is set), raising <see cref="Checked"/>/<see cref="Unchecked"/>/<see cref="Indeterminate"/>
/// and driving the <c>CheckStates</c> visual-state group. Subclasses reflect the state in template parts via
/// <see cref="OnToggleStateChanged"/>.
/// </summary>
public class ToggleButton : ButtonBase
{
	public static readonly RoutedEvent CheckedEvent = RoutedEvent.Register(nameof(Checked), RoutingStrategy.Bubble);
	public static readonly RoutedEvent UncheckedEvent = RoutedEvent.Register(nameof(Unchecked), RoutingStrategy.Bubble);
	public static readonly RoutedEvent IndeterminateEvent = RoutedEvent.Register(nameof(Indeterminate), RoutingStrategy.Bubble);

	public static readonly DependencyProperty<ToggleButton, bool?> IsCheckedProperty =
		DependencyProperty.Register(nameof(IsChecked),
			new PropertyMetadata<ToggleButton, bool?> { DefaultValue = false, PropertyChanged = static (t, _, _) => t.OnIsCheckedChanged() });

	public static readonly DependencyProperty<ToggleButton, bool> IsThreeStateProperty =
		DependencyProperty.Register(nameof(IsThreeState),
			new PropertyMetadata<ToggleButton, bool> { DefaultValue = false });

	/// <summary>The checked state: <c>false</c>, <c>true</c>, or <c>null</c> (indeterminate, only reached when
	/// <see cref="IsThreeState"/> is set).</summary>
	public bool? IsChecked
	{
		get => GetValue(IsCheckedProperty);
		set => SetValue(IsCheckedProperty, value);
	}

	/// <summary>When set, a click cycles through the <c>null</c> (indeterminate) state as well.</summary>
	public bool IsThreeState
	{
		get => GetValue(IsThreeStateProperty);
		set => SetValue(IsThreeStateProperty, value);
	}

	public event RoutedEventHandler Checked
	{
		add => AddHandler(CheckedEvent, value);
		remove => RemoveHandler(CheckedEvent, value);
	}

	public event RoutedEventHandler Unchecked
	{
		add => AddHandler(UncheckedEvent, value);
		remove => RemoveHandler(UncheckedEvent, value);
	}

	public event RoutedEventHandler Indeterminate
	{
		add => AddHandler(IndeterminateEvent, value);
		remove => RemoveHandler(IndeterminateEvent, value);
	}

	protected override void OnClick()
	{
		OnToggle();
		base.OnClick();
	}

	/// <summary>Advance the checked state on click: false → true → (three-state) null → false.</summary>
	protected virtual void OnToggle() => IsChecked = IsChecked switch
	{
		false => true,
		true => IsThreeState ? null : false,
		_ => false,
	};

	/// <summary>Hook for subclasses to reflect the current state in template parts (e.g. a check glyph). Called on
	/// every state change and once the template is applied.</summary>
	protected virtual void OnToggleStateChanged() { }

	protected override void OnApplyTemplate()
	{
		base.OnApplyTemplate();
		UpdateVisualState();
	}

	private void OnIsCheckedChanged()
	{
		RaiseEvent(IsChecked switch { true => CheckedEvent, false => UncheckedEvent, _ => IndeterminateEvent }, new RoutedEventArgs());
		UpdateVisualState();
	}

	private void UpdateVisualState()
	{
		// Best-effort for a VSM-driven template; harmless (returns false) for a template that reflects the state
		// through parts instead. The part-based path is the OnToggleStateChanged hook.
		VisualStateManager.GoToState(this, IsChecked switch { true => "Checked", null => "Indeterminate", _ => "Unchecked" }, useTransitions: true);
		OnToggleStateChanged();
	}
}
