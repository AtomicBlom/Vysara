using System.Collections;
using System.Reflection;

using SUIsei.Controls;

namespace SUIsei.RoseTap;

/// <summary>
/// Where an element keeps the elements markup nests inside it, and putting one in or taking one out: the property its
/// type names with <see cref="ContentPropertyAttribute"/>, as the XAML generator reads it. A collection
/// (<see cref="Panel.Children"/>, a list of items) takes a child at an index; a single slot (<see cref="Border.Child"/>,
/// <see cref="ContentControl.Content"/>) takes one where it is empty. The tap runs on the JIT, inside a game that opted
/// into diagnostics, so the property is found by reflection, which also reaches a game's own containers.
/// </summary>
internal static class TapContent
{
	/// <summary>Put <paramref name="child"/> into <paramref name="parent"/>'s content at <paramref name="index"/>, or say
	/// why it cannot go there.</summary>
	public static string? Add(UIElement parent, UIElement child, int index)
	{
		if (ContentPropertyOf(parent.GetType()) is not { } property)
			return "cannot add: its parent exposes no children collection";

		switch (property.GetValue(parent))
		{
			case UIElementCollection children:
				if (index < 0 || index > children.Count)
					return $"cannot add: index {index} is outside its parent's {property.Name}, which holds {children.Count}";
				children.Insert(index, child);
				return null;

			case IList list when !list.IsReadOnly && !list.IsFixedSize:
				if (index < 0 || index > list.Count)
					return $"cannot add: index {index} is outside its parent's {property.Name}, which holds {list.Count}";
				try
				{
					list.Insert(index, child);
				}
				catch (ArgumentException)
				{
					return $"cannot add: its parent's {property.Name} does not take a {child.GetType().Name}";
				}
				return null;

			case { } held when property.SetMethod is { IsPublic: true }:
				return $"cannot add: its parent's {property.Name} already holds {Describe(held)}";

			case null when property.SetMethod is { IsPublic: true }:
				if (index != 0)
					return $"cannot add: its parent's {property.Name} holds one element, so index {index} is not a place in it";
				if (!property.PropertyType.IsInstanceOfType(child))
					return $"cannot add: its parent's {property.Name} does not take a {child.GetType().Name}";
				property.SetValue(parent, child);
				return null;

			default:
				return $"cannot add: its parent's {property.Name} cannot be changed";
		}
	}

	/// <summary>
	/// Take <paramref name="child"/> out of whatever holds it, or say why it cannot be. A templated control's content
	/// sits in the control's own <see cref="ContentControl.Content"/> and is shown by a presenter its template binds, so
	/// it is taken from the control rather than the presenter, which the template would only fill again.
	/// </summary>
	public static string? Remove(UIElement child)
	{
		if (Diagnostics.ParentOf(child) is not { } parent)
			return "cannot remove: it has no parent in the tree";

		for (var holder = parent; holder is not null; holder = Diagnostics.ParentOf(holder))
		{
			if (holder is ContentPresenter)
				continue;
			if (TryTakeFrom(holder, child))
				return null;
		}
		return TryTakeFrom(parent, child) ? null : "cannot remove: it is not in any collection its parent exposes";
	}

	// The content property of a type, by the ContentProperty attribute on it or a base.
	private static PropertyInfo? ContentPropertyOf(Type type) =>
		type.GetCustomAttribute<ContentPropertyAttribute>(inherit: true) is { Name: { Length: > 0 } name }
			? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
			: null;

	private static bool TryTakeFrom(UIElement holder, UIElement child)
	{
		if (ContentPropertyOf(holder.GetType()) is not { } property)
			return false;

		switch (property.GetValue(holder))
		{
			case UIElementCollection children:
				for (var i = 0; i < children.Count; i++)
				{
					if (ReferenceEquals(children[i], child))
					{
						children.RemoveAt(i);
						return true;
					}
				}
				return false;

			case IList list when !list.IsReadOnly && !list.IsFixedSize && list.Contains(child):
				list.Remove(child);
				return true;

			case UIElement held when ReferenceEquals(held, child) && property.SetMethod is { IsPublic: true }:
				property.SetValue(holder, null);
				return true;

			default:
				return false;
		}
	}

	private static string Describe(object held) => held is UIElement element ? $"a {element.GetType().Name}" : $"'{held}'";
}
