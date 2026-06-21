namespace SUIsei.Controls;

/// <summary>The base of templatable controls (PlanningUI.md §D2.1/§D1.6). Sets <see cref="UIElement.IsTabStop"/> to
/// <c>true</c> by default so all controls participate in Tab navigation. A <see cref="Template"/> defines the visual
/// tree: it is applied lazily on first measure, becomes the control's single visual child, and carries the
/// <see cref="VisualStateManager"/> groups.</summary>
public abstract class Control : FrameworkElement
{
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
		// Controls are tab-focusable by default; UIElement's registered default is false (non-interactive elements).
		IsTabStop = true;
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
		AdoptChild(_templateRoot);
		OnApplyTemplate();
	}

	/// <summary>Hook for a control to grab named parts after its template is realized.</summary>
	protected virtual void OnApplyTemplate() { }

	private void OnTemplateChanged()
	{
		if (_templateRoot is not null)
		{
			DropChild(_templateRoot);
			_templateRoot = null;
		}
		InvalidateMeasure();
	}

	internal override IReadOnlyList<UIElement> VisualChildren =>
		_templateRoot is { } root ? [root] : base.VisualChildren;

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
