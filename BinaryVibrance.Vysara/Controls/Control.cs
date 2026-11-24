using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Media;

namespace BinaryVibrance.Vysara.Controls;

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
				Coerce = static (c, value) => value && !c._ancestorDisabled && !c._commandDisabled,
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

	// Whether a command the control runs can't execute now: folded into the effective IsEnabled, as WinUI folds
	// CanExecute in, rather than written over the author's own value.
	private bool _commandDisabled;

	/// <summary>Set whether the control's command can't execute now; called as the command, its parameter or its
	/// CanExecute changes.</summary>
	private protected void SetCommandDisabled(bool disabled)
	{
		if (_commandDisabled == disabled)
			return;
		_commandDisabled = disabled;
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

	/// <summary>The brush behind the control's content. A built-in template paints it where WinUI's default template
	/// does; null shows the template's own look.</summary>
	public static readonly DependencyProperty<Control, Brush?> BackgroundProperty =
		DependencyProperty.Register(nameof(Background),
			new PropertyMetadata<Control, Brush?> { PropertyChanged = static (c, old, @new) => c.OnBackgroundChanged(old, @new) });

	/// <summary>The brush for the control's border, where its template draws one; null shows the template's own look.</summary>
	public static readonly DependencyProperty<Control, Brush?> BorderBrushProperty =
		DependencyProperty.Register(nameof(BorderBrush),
			new PropertyMetadata<Control, Brush?> { PropertyChanged = static (c, old, @new) => c.OnBrushChanged(old, @new) });

	/// <summary>The brush for the control's text, inherited as in WinUI: the text in its template, or made for its string
	/// content, is painted with it unless that text sets its own, and so are the controls inside it. Null shows the
	/// template's own look. Any brush is accepted, as in WinUI, since a template may paint more than text with it; an image
	/// brush can't paint glyphs (they are drawn from the font atlas), so text it reaches draws nothing.</summary>
	public static readonly DependencyProperty<Control, Brush?> ForegroundProperty =
		DependencyProperty.Register(nameof(Foreground),
			new PropertyMetadata<Control, Brush?>
			{
				Inherits = InheritedText.Foreground,
				PropertyChanged = static (c, _, value) => c.OnForegroundChanged(value),
			});

	/// <summary>The thickness of the control's border, where its template draws one with <see cref="BorderBrush"/>.</summary>
	public static readonly DependencyProperty<Control, Thickness> BorderThicknessProperty =
		DependencyProperty.Register(nameof(BorderThickness),
			new PropertyMetadata<Control, Thickness> { Invalidates = InvalidationFlags.Measure });

	public Brush? Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }

	public Brush? BorderBrush { get => GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }

	public Thickness BorderThickness { get => GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }

	public Brush? Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

	/// <summary>The space between the control's chrome and its content, as in WinUI: a built-in template puts it where
	/// WinUI's default template does (a button's border, around its content). Where it isn't set, the template's own look
	/// shows.</summary>
	public static readonly DependencyProperty<Control, Thickness> PaddingProperty =
		DependencyProperty.Register(nameof(Padding),
			new PropertyMetadata<Control, Thickness> { Invalidates = InvalidationFlags.Measure });

	public Thickness Padding { get => GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

	/// <summary>The size of the control's text in DIPs, inherited as in WinUI: the text in its template, or made for its
	/// string content, takes it unless that text sets its own, and so do the controls inside it.</summary>
	public static readonly DependencyProperty<Control, float> FontSizeProperty =
		DependencyProperty.Register(nameof(FontSize),
			new PropertyMetadata<Control, float> { DefaultValue = 14f, Invalidates = InvalidationFlags.Measure, Inherits = InheritedText.FontSize });

	/// <summary>The size of the control's text in DIPs: 14 unless set here or inherited from above.</summary>
	public float FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }

	/// <summary>The family of the control's text, by name in the host's <see cref="UIHost.Fonts"/>: passed down as
	/// <see cref="FontSize"/> is, to the control's own text (a text box's) and to its template's and content's. A built-in
	/// look sets none, so the family from above reaches its text.</summary>
	public static readonly DependencyProperty<Control, FontFamily?> FontFamilyProperty =
		DependencyProperty.Register(nameof(FontFamily),
			new PropertyMetadata<Control, FontFamily?> { Invalidates = InvalidationFlags.Measure, Inherits = InheritedText.FontFamily });

	/// <summary>The family of the control's text (<see cref="FontFamilyProperty"/>).</summary>
	public FontFamily? FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }

	private protected override FontFamily? TextFamily => FontFamily;

	/// <summary>The background changed; a control that paints it itself repaints. The default repaints in place.</summary>
	private protected virtual void OnBackgroundChanged(Brush? old, Brush? @new) => OnBrushChanged(old, @new);

	/// <summary>The foreground changed; a control that colours text itself passes it on.</summary>
	private protected virtual void OnForegroundChanged(Brush? foreground) { }

	private protected override void OnLiveTreeChanged(bool attached)
	{
		Listen(Background, attached);
		Listen(BorderBrush, attached);
		base.OnLiveTreeChanged(attached);
		// A template realized before the control joined the tree saw only its own resources: read them again from here.
		if (attached)
			RefreshThemeResources();
	}

	// The theme resources the realized template read, each with the brush it found (null for none).
	private List<(string Key, Brush? Found)>? _themeResources;

	/// <summary>
	/// What a built-in template reads for <paramref name="key"/>, as WinUI's default templates read a
	/// <c>{ThemeResource}</c>: the brush under that key in the nearest <see cref="FrameworkElement.Resources"/> from this
	/// control up, or <paramref name="fallback"/>, the template's own look, when none has it. So a page recolours the built-in
	/// controls under it with keys such as <c>ToggleButtonBackgroundChecked</c>, as WinUI's lightweight styling does. A
	/// template realized before the control joined a tree is built again if a key it read finds another brush there.
	/// </summary>
	internal Brush ThemeResource(string key, Brush fallback) => ThemeResourceOrNull(key) ?? fallback;

	/// <summary>The brush under the theme resource <paramref name="key"/>, as <see cref="ThemeResource"/>, or null where none
	/// is found: for a part the built-in look leaves alone unless a page gives it something.</summary>
	internal Brush? ThemeResourceOrNull(string key)
	{
		var found = FindResource(key) as Brush;
		(_themeResources ??= []).Add((key, found));
		return found;
	}

	// Joined a tree: a template that read a theme resource which finds another brush from here is built again on the next
	// measure, reading them anew.
	private void RefreshThemeResources()
	{
		if (_templateRoot is null || _themeResources is not { } read)
			return;
		for (var i = 0; i < read.Count; i++)
		{
			if (!ReferenceEquals(FindResource(read[i].Key) as Brush, read[i].Found))
			{
				DiscardTemplate();
				return;
			}
		}
	}

	internal override void ClearTemplateBindings()
	{
		_themeResources = null;
		base.ClearTemplateBindings();
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

	/// <summary>
	/// Shows how the control holds focus in its template's <c>FocusStates</c>, as WinUI's do: <c>Focused</c> when it
	/// came by keyboard, a gamepad or code (a focus outline shows), <c>PointerFocused</c> by a click (none does),
	/// <c>Unfocused</c> when it went. A template without them shows nothing. Called by the <see cref="FocusManager"/>.
	/// </summary>
	internal void ShowFocus(FocusState state) => VisualStateManager.GoToState(this, state switch
	{
		FocusState.Unfocused => "Unfocused",
		FocusState.Pointer => "PointerFocused",
		_ => "Focused",
	}, useTransitions: true);

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

	// The old template goes whole: its states are left, releasing what they hold (which can include this control's own
	// properties), and the template bindings its factory registered are dropped, so later changes reach only the new
	// template's parts and the old parts can be collected. Template bindings are registered only by template factories. A
	// control with no template realized shows the states of its own groups, as a ListViewItem's built-in selection does; a
	// template takes over from them, so they are left too.
	private void OnTemplateChanged() => DiscardTemplate();

	// Drop the realized template, if any, for the next measure to build again.
	private void DiscardTemplate()
	{
		VisualStateManager.LeaveAll(_templateRoot ?? (UIElement)this);
		if (_templateRoot is not null)
		{
			ClearTemplateBindings();
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
