using SUIsei.Animation;

namespace SUIsei.Controls;

/// <summary>The base of templatable controls. Sets <see cref="UIElement.IsTabStop"/> to
/// <c>true</c> by default so all controls participate in Tab navigation. A <see cref="Template"/> defines the visual
/// tree: it is applied lazily on first measure, becomes the control's single visual child, and carries the
/// <see cref="VisualStateManager"/> groups.</summary>
public abstract class Control : FrameworkElement
{
	/// <summary>Whether the user can interact with the control. Inherited: while any ancestor control is disabled this
	/// reads false whatever was set, and it reverts when that ancestor is enabled again.</summary>
	public static readonly DependencyProperty<Control, bool> IsEnabledProperty =
		DependencyProperty.Register(nameof(IsEnabled),
			new PropertyMetadata<Control, bool>
			{
				DefaultValue = true,
				Coerce = static (c, value) => value && !c._ancestorDisabled,
				PropertyChanged = static (c, _, _) => c.RefreshIsEnabled(),
			});

	private static readonly object BoxedTrue = true;
	private static readonly object BoxedFalse = false;

	// Whether the nearest ancestor control is disabled (it accounts for those above it).
	private bool _ancestorDisabled;

	// The effective IsEnabled last reported by IsEnabledChanged; read on the input path instead of the property store.
	private bool _isEnabled = true;

	/// <summary>
	/// Whether the user can interact with the control, as in WinUI. A disabled control takes no keyboard focus (it is
	/// skipped by Tab and refused by <see cref="FocusManager.TrySetFocus"/>), handles no pointer or keyboard input on
	/// itself, and goes to its <c>CommonStates</c> <c>Disabled</c> visual state where its template has one. It is still
	/// hit-testable, so it blocks input to whatever is behind it, and input it sources still bubbles to its ancestors.
	/// Disabling a control disables every control beneath it.
	/// </summary>
	public bool IsEnabled
	{
		get => GetValue(IsEnabledProperty);
		set => SetValue(IsEnabledProperty, value);
	}

	/// <summary>Raised when the effective <see cref="IsEnabled"/> changes, including when it changes because an
	/// ancestor control was disabled or enabled, or because the control was attached under a disabled one.</summary>
	public event DependencyPropertyChangedEventHandler? IsEnabledChanged;

	/// <summary>Whether input handlers on this control run: false while it is disabled.</summary>
	internal bool CanHandleInput => _isEnabled;

	/// <summary>The <c>CommonStates</c> state this control shows now: <c>Disabled</c> while disabled, else
	/// <c>Normal</c>. A control with pointer states overrides it.</summary>
	private protected virtual string CommonVisualState => _isEnabled ? "Normal" : "Disabled";

	/// <summary>Called when the effective <see cref="IsEnabled"/> changes, before the visual state is updated and
	/// <see cref="IsEnabledChanged"/> is raised: a control drops any press, hover or drag it was tracking.</summary>
	private protected virtual void OnIsEnabledChanged(bool isEnabled) { }

	/// <summary>Set whether the nearest ancestor control is disabled; called as the control is attached, detached or an
	/// ancestor's effective value changes.</summary>
	internal void SetAncestorDisabled(bool disabled)
	{
		if (_ancestorDisabled == disabled)
			return;
		_ancestorDisabled = disabled;
		RefreshIsEnabled();
	}

	// After the local value or the inherited context changed: if the effective value moved, record it, update the
	// visual state, raise IsEnabledChanged, and pass the change down to the controls beneath.
	private void RefreshIsEnabled()
	{
		var enabled = IsEnabled;
		if (enabled == _isEnabled)
			return;
		_isEnabled = enabled;
		OnIsEnabledChanged(enabled);
		// Only once the template is realized: ApplyTemplate shows the state itself, so a control created disabled does not
		// realize its template early.
		if (_templateRoot is not null)
			GoToCommonState(useTransitions: true);
		IsEnabledChanged?.Invoke(this, new DependencyPropertyChangedEventArgs(IsEnabledProperty,
			enabled ? BoxedFalse : BoxedTrue, enabled ? BoxedTrue : BoxedFalse));
		foreach (var child in VisualChildren)
			child.InheritDisabled(!enabled);
	}

	// Show CommonVisualState. A template with no Disabled state shows Normal while disabled, so a hover or press state is
	// never left showing on a control that can no longer leave it.
	private protected void GoToCommonState(bool useTransitions)
	{
		var state = CommonVisualState;
		if (!VisualStateManager.GoToState(this, state, useTransitions) && state == "Disabled")
			VisualStateManager.GoToState(this, "Normal", useTransitions);
	}

	public static readonly DependencyProperty<Control, TabFocusNavigation> TabFocusNavigationProperty =
		DependencyProperty.Register(nameof(TabFocusNavigation),
			new PropertyMetadata<Control, TabFocusNavigation> { DefaultValue = TabFocusNavigation.Local });

	public static readonly DependencyProperty<Control, ControlTemplate?> TemplateProperty =
		DependencyProperty.Register(nameof(Template),
			new PropertyMetadata<Control, ControlTemplate?>
			{
				Invalidates = InvalidationFlags.Measure,
				PropertyChanged = static (c, _, _) => c.OnTemplateChanged(),
			});

	private FrameworkElement? _templateRoot;

	// VisualChildren while a template is applied, rebuilt only when _templateRoot changes, so reading it allocates nothing.
	private UIElement[] _templateRootList = [];

	/// <summary>Controls how Tab navigation behaves within this control's subtree.</summary>
	public TabFocusNavigation TabFocusNavigation
	{
		get => GetValue(TabFocusNavigationProperty);
		set => SetValue(TabFocusNavigationProperty, value);
	}

	/// <summary>The control template that builds this control's visual tree. Null leaves the control to draw itself.</summary>
	public ControlTemplate? Template
	{
		get => GetValue(TemplateProperty);
		set => SetValue(TemplateProperty, value);
	}

	protected Control()
	{
		// Controls are tab-focusable by default; UIElement's registered default is false (non-interactive elements). Set
		// on the BuiltInStyle rung, so a style or the app's own value overrides it and it never reads as the app's.
		SetBuiltInValue(IsTabStopProperty, true);
	}

	/// <summary>The host's animation clock (via the attached root), used by <see cref="VisualStateManager"/> to run
	/// state transitions. Null when the control is not attached to a live host.</summary>
	internal AnimationClock? Clock => AttachedRoot?.Clock;

	/// <summary>The realized root of this control's template (the element carrying its
	/// <see cref="VisualStateManager"/> groups), or null when the control has no template.</summary>
	internal FrameworkElement? GetTemplateRoot()
	{
		ApplyTemplate();
		return _templateRoot;
	}

	/// <summary>Realize the template (idempotent): build its root, adopt it as the control's single child, and run
	/// <see cref="OnApplyTemplate"/>. A no-op when already applied or when there is no template.</summary>
	internal void ApplyTemplate()
	{
		if (_templateRoot is not null || Template is not { } template)
			return;
		_templateRoot = template.Factory(this);
		_templateRootList = [_templateRoot];
		AdoptChild(_templateRoot);
		OnApplyTemplate();
		if (!_isEnabled)
			GoToCommonState(useTransitions: false);
	}

	/// <summary>Hook for a control to grab named parts after its template is realized.</summary>
	protected virtual void OnApplyTemplate() { }

	private void OnTemplateChanged()
	{
		if (_templateRoot is not null)
		{
			DropChild(_templateRoot);
			_templateRoot = null;
			_templateRootList = [];
		}
		InvalidateMeasure();
	}

	internal override IReadOnlyList<UIElement> VisualChildren => _templateRoot is not null ? _templateRootList : base.VisualChildren;

	protected override Size MeasureOverride(Size available)
	{
		ApplyTemplate();
		if (_templateRoot is { } root)
		{
			root.Measure(available);
			return root.DesiredSize;
		}
		return base.MeasureOverride(available);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		if (_templateRoot is { } root)
			root.Arrange(new Rect(0f, 0f, finalSize.Width, finalSize.Height));
		return finalSize;
	}
}
