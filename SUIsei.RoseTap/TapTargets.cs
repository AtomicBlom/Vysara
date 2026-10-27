namespace SUIsei.RoseTap;

/// <summary>
/// Finds the element an apply command names: a slot this batch built, <c>#name</c>, or an address as <see cref="TapTree"/>
/// writes it. The tree is walked afresh for each target, so a command after a removal or an addition in the same batch
/// resolves against the tree as it now is, not as it was when the batch began.
/// <para>
/// A target that cannot be resolved says why, and that sentence becomes the command's status: a name two elements share
/// is refused rather than answered with one of them, since an edit landing on an arbitrary one reports success either
/// way, and the refusal says how to name the one meant.
/// </para>
/// </summary>
internal sealed class TapTargets
{
	private readonly Dictionary<string, UIElement> _slots = new(StringComparer.Ordinal);

	/// <summary>Whether <paramref name="target"/> names a slot rather than something in the tree.</summary>
	public static bool IsSlot(string target) => target.Length > 0 && target[0] == '$';

	/// <summary>Forget the slots, as each batch begins: a slot names something one batch built and is not reachable
	/// from the next.</summary>
	public void ClearSlots() => _slots.Clear();

	/// <summary>Name an element this batch built, for the commands after it.</summary>
	public void Bind(string slot, UIElement element) => _slots[slot] = element;

	/// <summary>The element <paramref name="target"/> names, or null with the reason it names none.</summary>
	public (UIElement? Element, string? Unresolved) Resolve(string target)
	{
		if (target.Length == 0)
			return (null, "target not found: no target was given");
		if (IsSlot(target))
		{
			return _slots.TryGetValue(target, out var built)
				? (built, null)
				: (null, $"target not found: nothing has been built into {target}");
		}

		var isPath = target.Contains('/', StringComparison.Ordinal) || target.Contains('[', StringComparison.Ordinal);
		return isPath ? ResolvePath(target) : ResolveName(target[0] == '#' ? target[1..] : target);
	}

	// Every element of every view, with the elements at the top of each view first in their order.
	private static List<UIElement> Roots()
	{
		var roots = new List<UIElement>();
		foreach (var host in Diagnostics.Hosts)
		{
			foreach (var view in host.Views)
				roots.AddRange(Diagnostics.ChildrenOf(view));
		}
		return roots;
	}

	private static (UIElement?, string?) ResolveName(string name)
	{
		var named = new List<UIElement>();
		var pending = new Stack<UIElement>(Roots());
		while (pending.Count > 0)
		{
			var element = pending.Pop();
			if (TapTree.NameOf(element) == name)
				named.Add(element);
			foreach (var child in Diagnostics.ChildrenOf(element))
				pending.Push(child);
		}

		return named.Count switch
		{
			0 => (null, $"target not found: no element is named '{name}'"),
			1 => (named[0], null),
			_ => (null, $"target ambiguous: {named.Count} elements are named '{name}'; address one of them by its path instead"),
		};
	}

	private static (UIElement?, string?) ResolvePath(string path)
	{
		var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (segments.Length == 0)
			return (null, $"target not found: '{path}' has no path segments");

		UIElement? current = null;
		for (var s = 0; s < segments.Length; s++)
		{
			// A name identifies an element wherever it appears in a path, so it is looked up rather than walked to.
			if (segments[s][0] == '#')
			{
				var (named, unresolved) = ResolveName(segments[s][1..]);
				if (named is null)
					return (null, unresolved);
				current = named;
				continue;
			}

			IReadOnlyList<UIElement> candidates = current is null ? Roots() : Diagnostics.ChildrenOf(current);
			var (stepped, unstepped) = Step(candidates, segments[s]);
			if (stepped is null)
				return (null, unstepped);
			current = stepped;
		}
		return (current, null);
	}

	// One Type[index] segment among a set of siblings, counted as the address was written: by short type name.
	private static (UIElement?, string?) Step(IReadOnlyList<UIElement> siblings, string segment)
	{
		var open = segment.IndexOf('[', StringComparison.Ordinal);
		var isSegment = open > 0 && segment[^1] == ']'
			&& int.TryParse(segment.AsSpan(open + 1, segment.Length - open - 2), System.Globalization.NumberStyles.None,
				System.Globalization.CultureInfo.InvariantCulture, out _);
		if (!isSegment)
			return (null, $"target not found: '{segment}' is neither Type[index] nor #name");

		var type = segment[..open];
		var wanted = int.Parse(segment.AsSpan(open + 1, segment.Length - open - 2), System.Globalization.CultureInfo.InvariantCulture);
		var position = 0;
		foreach (var sibling in siblings)
		{
			if (sibling.GetType().Name != type)
				continue;
			if (position == wanted)
				return (sibling, null);
			position++;
		}
		return (null, $"target not found: no {segment} here, among {position} element(s) of type {type}");
	}
}
