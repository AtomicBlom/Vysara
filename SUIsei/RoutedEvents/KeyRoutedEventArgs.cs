using SUIsei.Input;

namespace SUIsei.RoutedEvents;

/// <summary>Event args for <see cref="UIElement.KeyDownEvent"/>, <see cref="UIElement.KeyUpEvent"/>, and their
/// Preview counterparts.</summary>
public sealed class KeyRoutedEventArgs : RoutedEventArgs
{
	/// <summary>The virtual key that was pressed or released.</summary>
	public VirtualKey Key { get; internal set; }

	/// <summary>True if this is an auto-repeat event (key held down).</summary>
	public bool KeyStatusRepeat { get; internal set; }

	/// <summary>Modifier keys held at the time of the event.</summary>
	public ModifierKeys Modifiers { get; internal set; }

	internal void Reset(RoutedEvent evt, object originalSource, in KeyInput input)
	{
		base.Reset(evt, originalSource);
		Key            = input.Key;
		KeyStatusRepeat = input.Repeat;
		Modifiers      = input.Modifiers;
	}
}
