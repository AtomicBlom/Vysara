namespace SUIsei.Controls;

/// <summary>The base of templatable controls (PlanningUI.md §D2.1). Sets <see cref="UIElement.IsTabStop"/> to
/// <c>true</c> by default so all controls participate in Tab navigation. Control templates + visual states arrive
/// in MU4.</summary>
public abstract class Control : FrameworkElement
{
	public static readonly DependencyProperty<Control, TabFocusNavigation> TabFocusNavigationProperty =
		DependencyProperty.Register(nameof(TabFocusNavigation),
			new PropertyMetadata<Control, TabFocusNavigation> { DefaultValue = TabFocusNavigation.Local });

	/// <summary>Controls how Tab navigation behaves within this control's subtree.</summary>
	public TabFocusNavigation TabFocusNavigation
	{
		get => GetValue(TabFocusNavigationProperty);
		set => SetValue(TabFocusNavigationProperty, value);
	}

	protected Control()
	{
		// Controls are tab-focusable by default; UIElement's registered default is false (non-interactive elements).
		IsTabStop = true;
	}

	/// <summary>The host's animation clock (via the attached root), used by <see cref="VisualStateManager"/> to run
	/// state transitions. Null when the control is not attached to a live host.</summary>
	internal AnimationClock? Clock => AttachedRoot?.Clock;

	/// <summary>The realized root of this control's template, if any (the element carrying its
	/// <see cref="VisualStateManager"/> groups). Null until a template is applied.</summary>
	internal FrameworkElement? GetTemplateRoot() => null;
}
