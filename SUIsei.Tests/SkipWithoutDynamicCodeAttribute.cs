using System.Runtime.CompilerServices;

namespace SUIsei.Tests;

/// <summary>
/// Skips a test that turns <see cref="HotReload"/> on where dynamic code is not supported, as in a NativeAOT-published
/// test run: <see cref="HotReload.Enabled"/> reads false there whatever it is set to, so the trimmer can remove what it
/// guards, and the rebuild path the test drives does not exist.
/// </summary>
internal sealed class SkipWithoutDynamicCodeAttribute()
	: SkipAttribute("Hot reload is off where dynamic code is not supported (NativeAOT): HotReload.Enabled reads false whatever it is set to.")
{
	public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
		Task.FromResult(!RuntimeFeature.IsDynamicCodeSupported);
}
