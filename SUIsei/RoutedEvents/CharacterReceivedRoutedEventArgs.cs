namespace SUIsei.RoutedEvents;

/// <summary>Event args for <see cref="UIElement.CharacterReceivedEvent"/>.</summary>
public sealed class CharacterReceivedRoutedEventArgs : RoutedEventArgs
{
	/// <summary>The Unicode character produced by the key event.</summary>
	public char Character { get; internal set; }

	internal void Reset(RoutedEvent evt, object originalSource, char character)
	{
		base.Reset(evt, originalSource);
		Character = character;
	}
}
