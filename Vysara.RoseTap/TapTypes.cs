using System.Reflection;

namespace Vysara.RoseTap;

/// <summary>
/// The types markup names by their short name: an element type a <c>CreateInstance</c> builds, and the owner of an
/// attached property such as <c>Grid.Row</c>. Looked up across Vysara and every loaded assembly that references it,
/// which is where a game's own controls are, by public types only. A short name two types share is refused, naming
/// both, rather than built as whichever was met first.
/// </summary>
internal static class TapTypes
{
	/// <summary>The UIElement type named <paramref name="name"/> that can be built with no arguments, or the reason
	/// there is none.</summary>
	public static (Type? Type, string? Refusal) Element(string name)
	{
		var matches = Named(name).Where(static t => typeof(UIElement).IsAssignableFrom(t) && !t.IsAbstract).ToList();
		if (matches.Count == 0)
			return (null, $"cannot build: no element type is named '{name}'");
		if (matches.Count > 1)
			return (null, $"cannot build: '{name}' names {matches.Count} types: {string.Join(", ", matches.Select(static t => t.FullName))}");
		var type = matches[0];
		return type.GetConstructor(Type.EmptyTypes) is { IsPublic: true }
			? (type, null)
			: (null, $"cannot build: {type.FullName} has no public parameterless constructor");
	}

	/// <summary>The attached dependency property <c>Owner.Name</c>, by its <c>NameProperty</c> field on the owner type,
	/// or null where no single type of that short name declares one.</summary>
	public static DependencyProperty? Attached(string owner, string name)
	{
		DependencyProperty? found = null;
		foreach (var type in Named(owner))
		{
			if (type.GetField(name + "Property", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) is not DependencyProperty { Kind: DependencyPropertyKind.Attached } dp)
				continue;
			if (found is not null)
				return null;
			found = dp;
		}
		return found;
	}

	private static IEnumerable<Type> Named(string name)
	{
		var vysara = typeof(UIElement).Assembly;
		foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			var related = assembly == vysara || assembly.GetReferencedAssemblies().Any(r => r.Name == vysara.GetName().Name);
			if (!related || assembly.IsDynamic)
				continue;

			Type[] types;
			try
			{
				types = assembly.GetExportedTypes();
			}
			catch (Exception exception) when (exception is ReflectionTypeLoadException or NotSupportedException or FileNotFoundException)
			{
				continue;
			}
			foreach (var type in types)
			{
				if (type.Name == name)
					yield return type;
			}
		}
	}
}
