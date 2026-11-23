namespace Vysara.Gallery;

/// <summary>One page of the gallery: the <paramref name="Key"/> <c>--page</c> takes and its golden image is named by, the
/// <paramref name="Title"/> the shell lists it under, and how to make it.</summary>
internal sealed record GalleryPage(string Key, string Title, Func<IGalleryView> Create);
