using System.Text;

using RoseMcp.Tap.Protocol;

namespace Vysara.RoseTap;

/// <summary>
/// The reply to <c>tree</c>: one row per element of every live host's views, walked fresh on each request so it cannot
/// drift from the tree the game has. Overlays a diagnostics tool added are left out, as
/// <see cref="Diagnostics.ChildrenOf(UIView)"/> leaves them out.
/// <para>
/// Each row carries the element's address, in RoseMCP's grammar: <c>#name</c> for a named element, which anchors
/// everything beneath it; otherwise its parent's address and <c>/Type[index]</c>, where the index counts the earlier
/// siblings with the same short type name, and an element with no named ancestor starts from its root's segment. The
/// elements at the top of every view of every host are siblings of one another, counted in that order. This is how
/// RoseMCP's markup diff addresses an element, so the two have to count alike or an edit lands on the element next door.
/// </para>
/// </summary>
internal static class TapTree
{
	/// <summary>Every element's row, parents before their children. Called on the UI thread.</summary>
	public static string Rows(TapHandles handles)
	{
		var roots = new List<UIElement>();
		foreach (var host in Diagnostics.Hosts)
		{
			foreach (var view in host.Views)
				roots.AddRange(Diagnostics.ChildrenOf(view));
		}

		var rows = new StringBuilder();
		Write(rows, handles, roots, parent: 0, parentAddress: "");
		handles.Prune();
		return rows.ToString();
	}

	/// <summary>The address of the element at <paramref name="index"/> among <paramref name="siblings"/>, below
	/// <paramref name="parentAddress"/> (empty for an element at the top of a view).</summary>
	public static string AddressOf(IReadOnlyList<UIElement> siblings, int index, string parentAddress)
	{
		if (NameOf(siblings[index]) is { Length: > 0 } name)
			return "#" + name;
		var segment = Segment(siblings, index);
		return parentAddress.Length == 0 ? segment : parentAddress + "/" + segment;
	}

	/// <summary>The name an element goes by in markup, or empty for one that has none.</summary>
	public static string NameOf(UIElement element) => (element as FrameworkElement)?.Name ?? "";

	/// <summary>The type name a row reports: the full name, as the XAML taps report the CLR type.</summary>
	public static string TypeName(Type type) => type.FullName ?? type.Name;

	private static void Write(StringBuilder rows, TapHandles handles, IReadOnlyList<UIElement> siblings, ulong parent, string parentAddress)
	{
		for (var i = 0; i < siblings.Count; i++)
		{
			var element = siblings[i];
			var handle = handles.HandleOf(element);
			var address = AddressOf(siblings, i, parentAddress);
			var source = Diagnostics.SourceOf(element);
			rows.Append(TapRows.Tree(handle, parent, i, TypeName(element.GetType()), NameOf(element),
				source?.File ?? "", source?.Line ?? 0, source?.Column ?? 0, address)).Append('\n');
			Write(rows, handles, Diagnostics.ChildrenOf(element), handle, address);
		}
	}

	// One Type[index] segment: the short type name, as markup names it, and how many earlier siblings share it.
	private static string Segment(IReadOnlyList<UIElement> siblings, int index)
	{
		var type = siblings[index].GetType().Name;
		var position = 0;
		for (var i = 0; i < index; i++)
		{
			if (siblings[i].GetType().Name == type)
				position++;
		}
		return $"{type}[{position}]";
	}
}
