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
}
