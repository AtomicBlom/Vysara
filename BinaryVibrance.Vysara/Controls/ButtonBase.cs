using System.Windows.Input;
using BinaryVibrance.Vysara.Animation;
using BinaryVibrance.Vysara.Input;
using BinaryVibrance.Vysara.RoutedEvents;

namespace BinaryVibrance.Vysara.Controls;

/// <summary>
/// The clickable-control base: the bubbling <see cref="Click"/> routed event synthesized from
/// a pointer press+release over the control (or Enter/Space when focused), the common pointer visual states
/// (<c>Normal</c>/<c>PointerOver</c>/<c>Pressed</c>), and WinUI <see cref="Command"/>/<see cref="CommandParameter"/>
/// support. A concrete button supplies its look via a <see cref="Control.Template"/>; <see cref="OnClick"/> is the
/// single override point (e.g. a toggle).
/// </summary>
public abstract class ButtonBase : ContentControl
{
	/// <summary>The bubbling routed event raised when the control is clicked.</summary>
	public static readonly RoutedEvent ClickEvent = RoutedEvent.Register(nameof(Click), RoutingStrategy.Bubble);

	public static readonly DependencyProperty<ButtonBase, ICommand?> CommandProperty =
		DependencyProperty.Register(nameof(Command),
			new PropertyMetadata<ButtonBase, ICommand?> { PropertyChanged = static (b, old, @new) => b.OnCommandChanged(old, @new) });

	public static readonly DependencyProperty<ButtonBase, object?> CommandParameterProperty =
		DependencyProperty.Register(nameof(CommandParameter),
			new PropertyMetadata<ButtonBase, object?> { PropertyChanged = static (b, _, _) => b.UpdateCanExecute() });

	private bool _isPressed;
	private bool _isPointerOver;

	protected ButtonBase()
	{
		AddHandler(PointerEnteredEvent, OnPointerEntered);
		AddHandler(PointerExitedEvent, OnPointerExited);
		AddHandler(PointerPressedEvent, OnPointerPressed);
		AddHandler(PointerReleasedEvent, OnPointerReleased);
		AddHandler(KeyDownEvent, OnKeyDown);
	}

	/// <summary>A command invoked (with <see cref="CommandParameter"/>) on click. While a command is set, the
	/// control's <see cref="Control.IsEnabled"/> tracks <see cref="ICommand.CanExecute"/>.</summary>
	public ICommand? Command
	{
		get => GetValue(CommandProperty);
		set => SetValue(CommandProperty, value);
	}

	/// <summary>The parameter passed to <see cref="Command"/>'s <c>CanExecute</c>/<c>Execute</c>.</summary>
	public object? CommandParameter
	{
		get => GetValue(CommandParameterProperty);
		set => SetValue(CommandParameterProperty, value);
	}

	/// <summary>Raised when the control is clicked. Wired by the generator (<c>btn.Click += OnClick</c>) — bubbles.</summary>
	public event RoutedEventHandler Click
	{
		add => AddHandler(ClickEvent, value);
		remove => RemoveHandler(ClickEvent, value);
	}

	/// <summary>Click the control as if by input (public for headless tests so a click can be driven without the
	/// pointer stack). Like input, it does nothing while the control is disabled.</summary>
	public void PerformClick()
	{
		if (IsEnabled)
			OnClick();
	}

	/// <summary>The single click override point: raises <see cref="Click"/>, then executes <see cref="Command"/> if
	/// it can. Override to add behaviour (e.g. a toggle), calling <c>base.OnClick()</c>.</summary>
	protected virtual void OnClick()
	{
		RaiseEvent(ClickEvent, new RoutedEventArgs());
		if (Command is { } command && command.CanExecute(CommandParameter))
			command.Execute(CommandParameter);
	}

	private void OnCommandChanged(ICommand? oldCommand, ICommand? newCommand)
	{
		if (oldCommand is not null)
			oldCommand.CanExecuteChanged -= OnCanExecuteChanged;
		if (newCommand is not null)
			newCommand.CanExecuteChanged += OnCanExecuteChanged;
		UpdateCanExecute();
	}

	private void OnCanExecuteChanged(object? sender, EventArgs e) => UpdateCanExecute();

	// While a command is set, the button is enabled only when the command can execute (WinUI's command-driven IsEnabled),
	// folded into the effective value: an author's IsEnabled="False" stays false whatever the command says, and is left
	// as the author set it.
	private void UpdateCanExecute() => SetCommandDisabled(Command is { } command && !command.CanExecute(CommandParameter));

	private protected override string CommonVisualState =>
		!IsEnabled ? "Disabled" : _isPressed ? "Pressed" : _isPointerOver ? "PointerOver" : "Normal";

	// A disabled button sees no input, so a press or hover in progress could never end: drop both.
	private protected override void OnIsEnabledChanged(bool isEnabled)
	{
		_isPressed = false;
		_isPointerOver = false;
	}

	// The handlers below run only while the button is enabled: a disabled control's input handlers are skipped.
	private void OnPointerEntered(object sender, RoutedEventArgs e)
	{
		_isPointerOver = true;
		GoToCommonState(useTransitions: true);
	}

	private void OnPointerExited(object sender, RoutedEventArgs e)
	{
		_isPointerOver = false;
		_isPressed = false;
		GoToCommonState(useTransitions: true);
	}

	// A primary press presses the button, handled as WinUI's buttons handle it, so what the button sits in doesn't act on
	// it too; a right or middle press is left to what's around it (a context menu).
	private void OnPointerPressed(object sender, RoutedEventArgs e)
	{
		if (e is PointerRoutedEventArgs { IsSecondaryButton: true })
			return;
		_isPressed = true;
		e.Handled = true;
		GoToCommonState(useTransitions: true);
	}

	private void OnPointerReleased(object sender, RoutedEventArgs e)
	{
		if (e is PointerRoutedEventArgs { IsSecondaryButton: true })
			return;
		if (_isPressed)
		{
			_isPressed = false;
			e.Handled = true;
			OnClick();
		}
		GoToCommonState(useTransitions: true);
	}

	private void OnKeyDown(object sender, RoutedEventArgs e)
	{
		if (e is KeyRoutedEventArgs key && key.Key is VirtualKey.Enter or VirtualKey.Space or VirtualKey.GamepadA)
		{
			e.Handled = true;
			OnClick();
		}
	}
}
