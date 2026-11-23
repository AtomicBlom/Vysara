namespace BinaryVibrance.Vysara;

/// <summary>
/// The <c>[MetadataUpdateHandler]</c> target (registered as an assembly attribute in <see cref="HotReload"/>).
/// The runtime invokes these after applying a metadata delta; they hand the changed types to <see cref="HotReload"/>,
/// which defers the actual view rebuild to the next <see cref="UIHost.Tick"/>.
/// </summary>
internal static class HotReloadHandler
{
	internal static void ClearCache(Type[]? updatedTypes) { }

	internal static void UpdateApplication(Type[]? updatedTypes) => HotReload.NotifyUpdate(updatedTypes);
}
