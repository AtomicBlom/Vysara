namespace SUIsei;

/// <summary>
/// A property whose value flows down the element tree, as WinUI's text properties do: each type that holds it registers
/// a dependency property of its own (<c>Control.FontSize</c>, <c>ContentPresenter.FontSize</c>, <c>TextBlock.FontSize</c>),
/// and an element's value of it becomes the <b>Inherited</b> value of the nearest elements below that hold one, through
/// any between that don't (a <c>Border</c>, a <c>Panel</c>). A value set on an element locally, by a style or by an
/// animation outranks the one it inherits, and is what its own descendants inherit in turn.
/// <para>
/// The value is pushed, not looked up: when an element's value changes, the elements below that take it are told at
/// once, and an element joining a parent takes the value the parent passes down (and gives it up on leaving), so reading
/// one never walks the tree and a steady frame does no inheritance work. The members share a default, so an element
/// below nothing that holds a value reads the same as one below an element that holds only the default.
/// </para>
/// </summary>
/// <param name="memberOf">The dependency property an element holds for this value, or null for one that only passes it
/// on.</param>
internal sealed class InheritedProperty<TValue>(Func<UIElement, IInheritedMember<TValue>?> memberOf)
{
	/// <summary>The value <paramref name="source"/> holds changed: pass it to the elements below that take it.
	/// <paramref name="hasValue"/> is false when it holds none on any rung, so they hold none either.</summary>
	internal void PassDown(DependencyObject source, TValue value, bool hasValue)
	{
		if (source is not UIElement element)
			return;
		var children = element.VisualChildren;
		for (var i = 0; i < children.Count; i++)
			Push(children[i], value, hasValue);
	}

	/// <summary><paramref name="child"/> joined <paramref name="parent"/>: it takes the value the parent passes down.</summary>
	internal void Adopt(UIElement parent, UIElement child)
	{
		var hasValue = ValueAt(parent, out var value);
		Push(child, value, hasValue);
	}

	/// <summary><paramref name="child"/> left its parent: it gives up the value it took from above.</summary>
	internal void Drop(UIElement child) => Push(child, default!, false);

	// Set the inherited value of the element, if it holds the property (which passes it on if its own value moves), or
	// else of the elements below it that do.
	private void Push(UIElement element, TValue value, bool hasValue)
	{
		if (memberOf(element) is { } member)
		{
			member.SetInherited(element, value, hasValue);
			return;
		}
		var children = element.VisualChildren;
		for (var i = 0; i < children.Count; i++)
			Push(children[i], value, hasValue);
	}

	// The value an element passes down: that of the nearest element at or above it that holds the property, if that one
	// holds a value on any rung.
	private bool ValueAt(UIElement element, out TValue value)
	{
		for (var e = element; e is not null; e = e.Parent)
		{
			if (memberOf(e) is { } member)
				return member.TryGetValue(e, out value);
		}
		value = default!;
		return false;
	}
}

/// <summary>One owner's dependency property for an <see cref="InheritedProperty{TValue}"/>.</summary>
internal interface IInheritedMember<TValue>
{
	/// <summary>Set (or, with <paramref name="hasValue"/> false, clear) <paramref name="target"/>'s inherited value.</summary>
	void SetInherited(DependencyObject target, TValue value, bool hasValue);

	/// <summary><paramref name="target"/>'s value, and whether it holds one on any rung rather than only the default.</summary>
	bool TryGetValue(DependencyObject target, out TValue value);
}
