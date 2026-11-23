namespace Vysara.Gallery;

/// <summary>Something the gallery draws: its shell, or one of its pages on its own.</summary>
internal interface IGalleryView
{
	/// <summary>The view's element tree. Read it each time it is wanted: a hot-reload rebuild replaces it.</summary>
	UIElement Root { get; }

	/// <summary>Advance whatever the view animates by hand (rather than through storyboards) to
	/// <paramref name="seconds"/>.</summary>
	void Animate(double seconds) { }
}
