using SUIsei.Input;

namespace SUIsei.Controls;

/// <summary>
/// A single-line editable text field. Consumes the host's <see cref="UIElement.CharacterReceived"/>
/// and <see cref="UIElement.KeyDown"/> events (the input pipeline already routes them to the focused element) to
/// drive a caret + selection over <see cref="Text"/>: insert/overtype, Backspace/Delete, arrow/Home/End motion
/// (Shift extends the selection), Select-All, and cut/copy/paste through a process-wide clipboard (an OS clipboard is
/// a later host seam). <see cref="Text"/> is a dependency property with a <see cref="TextChanged"/> event, so it
/// participates in TwoWay <c>{x:Bind}</c>. IME is deferred — committed characters only.
/// </summary>
public sealed class TextBox : Control
{
	private static string _clipboard = string.Empty;
	private static readonly Thickness Pad = new(4f, 2f);

	// Chrome that reacts to interaction: the resting border/background are the public Background/BorderBrush; hover
	// brightens the border and focus swaps in an accent border (thicker) over a slightly lifted background.
	private static readonly HdrColor HoverBorder = HdrColor.FromRgb(130, 130, 150);
	private static readonly HdrColor FocusBorder = HdrColor.FromRgb(80, 150, 240);
	private static readonly HdrColor FocusBackground = HdrColor.FromRgb(36, 38, 48);

	// The Windows caret cadence: solid through a half-period, then hidden through the next, repeating. The short ramp
	// keeps it an effectively hard on/off (a fade would read as a pulse, not a caret).
	private const double CaretBlinkHalfSeconds = 0.53;
	private const double CaretBlinkRampSeconds = 0.04;

	public static readonly DependencyProperty<TextBox, string> TextProperty =
		DependencyProperty.Register<TextBox, string>(nameof(Text),
			new PropertyMetadata<TextBox, string> { DefaultValue = "", Invalidates = InvalidationFlags.Measure, PropertyChanged = static (t, _, _) => t.OnTextChanged() });

	private readonly TextBlock _textBlock = new();
	private readonly Border _caretVisual = new() { Background = HdrColor.FromRgb(230, 230, 235) };
	private readonly Border _selectionHighlight = new() { Background = HdrColor.FromRgb(60, 110, 200), Visibility = Visibility.Collapsed };

	private int _caret;
	private int _anchor;
	private bool _focused;
	private bool _pointerOver;
	private float _scrollOffset; // horizontal scroll (DIPs) keeping the caret inside the viewport
	private readonly Storyboard _caretBlink;

	public TextBox()
	{
		AdoptChild(_selectionHighlight);
		AdoptChild(_textBlock);
		AdoptChild(_caretVisual);
		_caretVisual.Visibility = Visibility.Collapsed;
		_caretBlink = BuildCaretBlink();
		ClipToBounds = true; // long text scrolls within the field instead of spilling past its border

		AddHandler(CharacterReceivedEvent, OnCharacterReceived);
		AddHandler(KeyDownEvent, OnKeyDown);
		AddHandler(GotFocusEvent, OnGotFocus);
		AddHandler(LostFocusEvent, OnLostFocus);
		AddHandler(PointerEnteredEvent, OnPointerEntered);
		AddHandler(PointerExitedEvent, OnPointerExited);
	}

	public string Text
	{
		get => GetValue(TextProperty);
		set => SetValue(TextProperty, value);
	}

	/// <summary>Font size in DIPs.</summary>
	public float FontSize { get; set { if (field != value) { field = value; InvalidateMeasure(); } } } = 14f;

	public HdrColor Foreground { get; set; } = HdrColor.FromRgb(235, 235, 240);

	public HdrColor Background { get; set { if (field != value) { field = value; InvalidateContent(); } } } = HdrColor.FromRgb(30, 30, 38);

	public HdrColor BorderBrush { get; set; } = HdrColor.FromRgb(90, 90, 105);

	/// <summary>The caret position (also the moving end of a selection).</summary>
	public int CaretIndex
	{
		get => _caret;
		set => MoveCaret(value, extend: false);
	}

	public int SelectionStart => Math.Min(_anchor, _caret);

	public int SelectionLength => Math.Abs(_caret - _anchor);

	public event EventHandler? TextChanged;

	public void SelectAll()
	{
		_anchor = 0;
		_caret = Text.Length;
		RestartCaretBlink();
		InvalidateArrange();
	}

	internal override IReadOnlyList<UIElement> VisualChildren => [_selectionHighlight, _textBlock, _caretVisual];

	protected override Size MeasureOverride(Size available)
	{
		_textBlock.Text = Text;
		_textBlock.FontSize = FontSize;
		_textBlock.Foreground = Foreground;

		// Measure the text unbounded in width so it reports its full single-line extent (the field scrolls to it
		// rather than wrapping or capping at the viewport).
		_textBlock.Measure(new Size(float.PositiveInfinity, available.Height));
		_caretVisual.Measure(available);
		_selectionHighlight.Measure(available);

		var lineHeight = FontAtlas.Default.LineHeight * FontSize;
		var width = float.IsPositiveInfinity(available.Width) ? _textBlock.DesiredSize.Width + Pad.Horizontal + 2f : available.Width;
		return new Size(width, lineHeight + Pad.Vertical);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		var lineHeight = FontAtlas.Default.LineHeight * FontSize;
		var viewport = MathF.Max(0f, finalSize.Width - Pad.Horizontal);

		// Keep the caret inside the viewport, then pull back so the text's end never floats past the right edge
		// leaving blank space. Everything below arranges in this scrolled content space (clipped to the field).
		var caretX = OffsetOf(_caret);
		if (caretX - _scrollOffset < 0f)
			_scrollOffset = caretX;
		else if (caretX - _scrollOffset > viewport)
			_scrollOffset = caretX - viewport;
		var contentWidth = OffsetOf(Text.Length);
		_scrollOffset = Math.Clamp(_scrollOffset, 0f, MathF.Max(0f, contentWidth - viewport));

		var originX = Pad.Left - _scrollOffset;
		_textBlock.Arrange(new Rect(originX, Pad.Top, MathF.Max(contentWidth, viewport), lineHeight));

		_caretVisual.Visibility = _focused ? Visibility.Visible : Visibility.Collapsed;
		_caretVisual.Arrange(new Rect(originX + caretX, Pad.Top, 1f, lineHeight));

		if (SelectionLength > 0)
		{
			var x0 = originX + OffsetOf(SelectionStart);
			var x1 = originX + OffsetOf(SelectionStart + SelectionLength);
			_selectionHighlight.Visibility = Visibility.Visible;
			_selectionHighlight.Arrange(new Rect(x0, Pad.Top, MathF.Max(0f, x1 - x0), lineHeight));
		}
		else
		{
			_selectionHighlight.Visibility = Visibility.Collapsed;
			_selectionHighlight.Arrange(new Rect(originX, Pad.Top, 0f, lineHeight));
		}
		return finalSize;
	}

	/// <summary>The caret's current blink opacity (test seam).</summary>
	internal float CaretOpacity => _caretVisual.Opacity;

	/// <summary>The horizontal scroll offset in DIPs (test seam).</summary>
	internal float HorizontalScrollOffset => _scrollOffset;

	/// <summary>The caret's arranged rect within the field (test seam).</summary>
	internal Rect CaretRect => _caretVisual.LayoutRect;

	private protected override VisualContent? BuildContent()
	{
		var brush = _focused ? FocusBorder : _pointerOver ? HoverBorder : BorderBrush;
		var fill = _focused ? FocusBackground : Background;
		var thickness = _focused ? 1.5f : 1f;
		return new RectContent(new System.Numerics.Vector2(LayoutRect.Width, LayoutRect.Height), fill, 3f, brush, thickness);
	}

	// A repeating square-wave on the caret's Opacity: hold solid through the first half-period, ramp off, hold hidden
	// through the second. RepeatBehavior.Forever means it never completes; restarting it (on each edit) makes the
	// caret solid again, so it only blinks once typing goes idle.
	private Storyboard BuildCaretBlink()
	{
		var blink = new DoubleAnimationUsingKeyFrames
		{
			Duration = TimeSpan.FromSeconds(2 * CaretBlinkHalfSeconds),
			RepeatBehavior = RepeatBehavior.Forever,
			Apply = v => _caretVisual.SetAnimatedValue(OpacityProperty, (float)v),
			Release = () => _caretVisual.ClearAnimatedValue(OpacityProperty),
		};
		blink.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.Zero, Value = 1.0 });
		blink.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(CaretBlinkHalfSeconds - CaretBlinkRampSeconds), Value = 1.0 });
		blink.KeyFrames.Add(new DoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(CaretBlinkHalfSeconds), Value = 0.0 });
		return new Storyboard { Children = { blink } };
	}

	// Begin (or restart) the blink — solid first, then blinking. A no-op until focused and attached to a host clock.
	private void RestartCaretBlink()
	{
		if (_focused && Clock is { } clock)
			clock.Begin(_caretBlink);
	}

	private void StopCaretBlink()
	{
		Clock?.Stop(_caretBlink);
		_caretVisual.ClearAnimatedValue(OpacityProperty);
	}

	// The x pixel of the caret/selection edge before character index `i`.
	private float OffsetOf(int index)
	{
		index = Math.Clamp(index, 0, Text.Length);
		return index == 0 ? 0f : FontAtlas.Default.MeasureWidth(Text.AsSpan(0, index)) * FontSize;
	}

	private void OnTextChanged()
	{
		_caret = Math.Clamp(_caret, 0, Text.Length);
		_anchor = Math.Clamp(_anchor, 0, Text.Length);
		TextChanged?.Invoke(this, EventArgs.Empty);
	}

	private void OnGotFocus(object sender, RoutedEventArgs e)
	{
		_focused = true;
		_caret = _anchor = Text.Length;
		RestartCaretBlink();
		InvalidateContent();
		InvalidateArrange();
	}

	private void OnLostFocus(object sender, RoutedEventArgs e)
	{
		_focused = false;
		StopCaretBlink();
		InvalidateContent();
		InvalidateArrange();
	}

	private void OnPointerEntered(object sender, RoutedEventArgs e)
	{
		_pointerOver = true;
		InvalidateContent();
	}

	private void OnPointerExited(object sender, RoutedEventArgs e)
	{
		_pointerOver = false;
		InvalidateContent();
	}

	private void OnCharacterReceived(object sender, RoutedEventArgs e)
	{
		if (e is not CharacterReceivedRoutedEventArgs c || c.Character < ' ')
			return;
		ReplaceSelection(c.Character.ToString());
		e.Handled = true;
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is not KeyRoutedEventArgs key)
			return;
		var control = (key.Modifiers & ModifierKeys.Control) != 0;
		var shift = (key.Modifiers & ModifierKeys.Shift) != 0;

		switch (key.Key)
		{
			case VirtualKey.Back: Backspace(); break;
			case VirtualKey.Delete: DeleteForward(); break;
			case VirtualKey.Left: MoveCaret(_caret - 1, shift); break;
			case VirtualKey.Right: MoveCaret(_caret + 1, shift); break;
			case VirtualKey.Home: MoveCaret(0, shift); break;
			case VirtualKey.End: MoveCaret(Text.Length, shift); break;
			case VirtualKey.A when control: SelectAll(); break;
			case VirtualKey.C when control: Copy(); break;
			case VirtualKey.X when control: Cut(); break;
			case VirtualKey.V when control: ReplaceSelection(_clipboard); break;
			default: return;
		}
		e.Handled = true;
	}

	private void MoveCaret(int position, bool extend)
	{
		_caret = Math.Clamp(position, 0, Text.Length);
		if (!extend)
			_anchor = _caret;
		RestartCaretBlink();
		InvalidateArrange();
	}

	private void Backspace()
	{
		if (SelectionLength > 0)
			DeleteSelection();
		else if (_caret > 0)
			SetTextAndCaret(Text[..(_caret - 1)] + Text[_caret..], _caret - 1);
	}

	private void DeleteForward()
	{
		if (SelectionLength > 0)
			DeleteSelection();
		else if (_caret < Text.Length)
			SetTextAndCaret(Text[.._caret] + Text[(_caret + 1)..], _caret);
	}

	private void Copy()
	{
		if (SelectionLength > 0)
			_clipboard = Text.Substring(SelectionStart, SelectionLength);
	}

	private void Cut()
	{
		if (SelectionLength == 0)
			return;
		_clipboard = Text.Substring(SelectionStart, SelectionLength);
		DeleteSelection();
	}

	private void DeleteSelection()
	{
		var start = SelectionStart;
		SetTextAndCaret(Text[..start] + Text[(start + SelectionLength)..], start);
	}

	private void ReplaceSelection(string insert)
	{
		var start = SelectionStart;
		var end = start + SelectionLength;
		SetTextAndCaret(Text[..start] + insert + Text[end..], start + insert.Length);
	}

	private void SetTextAndCaret(string text, int caret)
	{
		Text = text;                          // raises TextChanged + invalidates measure
		_caret = _anchor = Math.Clamp(caret, 0, Text.Length);
		RestartCaretBlink();
		InvalidateArrange();
	}
}
