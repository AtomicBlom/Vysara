using Vysara.Drawing;

namespace Vysara.Composition;

/// <summary>
/// An <see cref="IUICompositor"/> whose output is neutral draw lists. A <see cref="UIHost"/> hands out the list each
/// surface's compositor last produced in <see cref="UIHost.DrawLists"/>, and tracks the texture sources it names
/// (<see cref="UIHost.ReleasedTextures"/>), whichever compositor produced it: <see cref="DrawListCompositor"/>, or a
/// foreign one that implements this too. A compositor that renders some other way leaves it off, and its surfaces'
/// lists are <see cref="DrawListData.Empty"/>.
/// </summary>
public interface IDrawListSource
{
	/// <summary>
	/// The list the last successful composite of <paramref name="surface"/> produced, or <see cref="DrawListData.Empty"/>
	/// before the first. A surface whose composite throws keeps showing it. Its <see cref="DrawListData.Damage"/> is what
	/// changed since the list handed out before it, and empty when the same list is handed out again unchanged.
	/// <para>
	/// The list names no <see cref="TextureSource"/> whose <see cref="TextureSource.IsDisposed"/> is true once a composite
	/// after the disposal has run. A host finds a disposed source its surfaces still draw at the start of the next
	/// <see cref="UIHost.Render"/> and marks the root visual of each of those surfaces dirty, moving its
	/// <see cref="Visual.Version"/>; it reports the source in <see cref="UIHost.ReleasedTextures"/> only when the lists of
	/// that render leave it out. So a compositor that keeps output per visual composites the whole tree again when the root's
	/// version moves, and leaves disposed sources out as it does.
	/// </para>
	/// </summary>
	DrawListData LastDraw(DrawListId surface);
}
