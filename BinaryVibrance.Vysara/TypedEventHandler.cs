namespace BinaryVibrance.Vysara;

/// <summary>A handler of an event that passes its sender as its own type, as WinUI's <c>TypedEventHandler</c>
/// (<c>Expander.Expanding</c>, <c>NumberBox.ValueChanged</c>, <c>InfoBar.Closed</c>).</summary>
public delegate void TypedEventHandler<in TSender, in TResult>(TSender sender, TResult args);
