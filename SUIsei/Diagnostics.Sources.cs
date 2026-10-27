using System.Runtime.CompilerServices;

namespace SUIsei;

public static partial class Diagnostics
{
	/// <summary>Where in a XAML file an element or a style setter was written.</summary>
	/// <param name="File">The XAML file's path, as the build saw it.</param>
	/// <param name="Line">The one-based line of the element's (or setter's) tag name.</param>
	/// <param name="Column">The one-based column of that tag name.</param>
	/// <param name="InTemplate">True when a <c>ControlTemplate</c> built the element, so it is part of a control's
	/// template rather than the app's own markup for its content.</param>
	public sealed record SourcePosition(string File, int Line, int Column, bool InTemplate);

	// Positions keyed weakly by the element or setter they describe, so recording one keeps nothing alive.
	private static ConditionalWeakTable<object, SourcePosition>? _sources;

	/// <summary>Where <paramref name="element"/> was written in markup, or null for one built in code or by a view
	/// compiled without its source table.</summary>
	/// <exception cref="InvalidOperationException">Diagnostics are off.</exception>
	public static SourcePosition? SourceOf(UIElement element)
	{
		ArgumentNullException.ThrowIfNull(element);
		RequireEnabled();
		return RecordedSource((object)element);
	}

	/// <summary>Record where <paramref name="element"/> was written. Generated <c>InitializeComponent</c> and template
	/// factories call this for each element they build; it does nothing while diagnostics are off.</summary>
	/// <param name="element">The element built.</param>
	/// <param name="file">The XAML file's path.</param>
	/// <param name="line">The one-based line of the element's tag name.</param>
	/// <param name="column">The one-based column of the element's tag name.</param>
	/// <param name="inTemplate">True when a <c>ControlTemplate</c> built it.</param>
	public static void RecordSource(UIElement element, string file, int line, int column, bool inTemplate)
	{
		if (!Enabled)
			return;
		ArgumentNullException.ThrowIfNull(element);
		Record(element, new SourcePosition(file, line, column, inTemplate));
	}

	/// <summary>Record where a style's <paramref name="setter"/> was written, which <see cref="Properties"/> reports as
	/// the source of the value it sets. Generated code calls this for each setter; it does nothing while diagnostics are
	/// off.</summary>
	/// <param name="setter">The setter built.</param>
	/// <param name="file">The XAML file's path.</param>
	/// <param name="line">The one-based line of the <c>Setter</c> tag name.</param>
	/// <param name="column">The one-based column of the <c>Setter</c> tag name.</param>
	public static void RecordSource(Setter setter, string file, int line, int column)
	{
		if (!Enabled)
			return;
		ArgumentNullException.ThrowIfNull(setter);
		Record(setter, new SourcePosition(file, line, column, InTemplate: false));
	}

	private static void Record(object target, SourcePosition position)
	{
		var sources = Volatile.Read(ref _sources);
		if (sources is null)
		{
			Interlocked.CompareExchange(ref _sources, new ConditionalWeakTable<object, SourcePosition>(), null);
			sources = _sources!;
		}
		sources.AddOrUpdate(target, position);
	}

	private static SourcePosition? RecordedSource(object target) =>
		Volatile.Read(ref _sources) is { } sources && sources.TryGetValue(target, out var position) ? position : null;
}
