using SUIsei.Drawing;

namespace SUIsei.Composition;

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
	/// before the first. A surface whose composite throws keeps showing it.
	/// </summary>
	DrawListData LastDraw(DrawListId surface);
}
