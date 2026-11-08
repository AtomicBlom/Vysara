using System.Reflection;
using System.Text;

using RoseMcp.Tap.Protocol;

namespace SUIsei.RoseTap;

/// <summary>
/// Applies a batch of <c>apply</c> commands to the game's tree and answers one row per command, in the order given:
/// <c>applied</c>, or a sentence saying why not, with the handle the command landed on and what the property said
/// before. A batch is never resent, because one that adds an element would add a second copy, so every command reports
/// its own outcome and none hides behind another's.
/// <para>
/// An addition arrives as <c>CreateInstance</c> commands that build elements into slots, <c>SetProperty</c> commands on
/// those slots, and <c>AddChild</c> commands that put each slot into its parent, the finished subtree last, so a
/// half-built element is never laid out. Slots last for one batch.
/// </para>
/// <para>
/// A property is set on its Local level, as the app's own markup would set it, after its text is converted with the
/// generator's value syntaxes. A dependency property goes through <see cref="Diagnostics.SetLocal"/> and is cleared with
/// <see cref="Diagnostics.ClearLocal"/>. A plain property, which SUIsei keeps for most layout values such as
/// <c>Width</c> or <c>Margin</c>, is set through its setter; it has no Local level, so it cannot be cleared.
/// </para>
/// </summary>
internal sealed class TapEdits(TapHandles handles)
{
	private readonly TapTargets _targets = new();

	/// <summary>The property changes the last batch made on elements already in the game, in the order made: what the
	/// overlay's change marks show. A property set on an element the batch built is not one: that element is the
	/// change.</summary>
	public List<TapChange> Changes { get; } = [];

	/// <summary>Apply <paramref name="commands"/> and return the result rows. Called on the UI thread.</summary>
	public string Apply(IReadOnlyList<TapApplyCommand> commands)
	{
		_targets.ClearSlots();
		Changes.Clear();

		var rows = new StringBuilder();
		foreach (var command in commands)
		{
			ulong handle = 0;
			var old = "";
			string status;
			try
			{
				status = command.Op switch
				{
					TapCommands.SetProperty => SetProperty(command, ref handle, ref old),
					TapCommands.ClearProperty => ClearProperty(command, ref handle, ref old),
					TapCommands.CreateInstance => Create(command, ref handle),
					TapCommands.AddChild => AddChild(command, ref handle),
					TapCommands.RemoveChild => RemoveChild(command, ref handle),
					_ => "unsupported op",
				};
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				status = $"failed: {exception.GetType().Name}: {exception.Message}";
			}

			var changedLive = status == "applied" && handle != 0 && !TapTargets.IsSlot(command.Target)
				&& command.Op is TapCommands.SetProperty or TapCommands.ClearProperty;
			if (changedLive)
				Changes.Add(new TapChange(handle, command.Property, old, command.Op == TapCommands.SetProperty ? command.Value : "", DateTime.UtcNow));

			rows.Append(TapRows.ApplyResult(command, status, handle, old)).Append('\n');
		}

		// An empty batch still answers: an empty reply means the command is not served.
		return rows.Length > 0 ? rows.ToString() : "\n";
	}

	private string SetProperty(TapApplyCommand command, ref ulong handle, ref string old)
	{
		var (target, unresolved) = _targets.Resolve(command.Target);
		if (target is null)
			return unresolved!;
		handle = handles.HandleOf(target);

		// A markup extension is compiled; its text is not a value, and set as one it would show its own source.
		var text = command.Value.Trim();
		if (text.StartsWith('{') && text != "{x:Null}")
			return $"cannot set {command.Property}: {text} is a markup extension, which is compiled rather than set live";

		if (DependencyPropertyOf(target, command.Property) is { } dp)
		{
			var valueType = ValueTypeOf(dp);
			old = Text(CurrentValue(target, dp));
			if (!Diagnostics.CanParseValue(valueType))
				return $"cannot set {command.Property}: SUIsei has no markup syntax for a {TapProperties.FriendlyName(valueType)}";
			if (!Diagnostics.TryParseValue(dp, command.Value, out var value))
				return $"cannot set {command.Property}: '{command.Value}' is not a {TapProperties.FriendlyName(valueType)}";
			Diagnostics.SetLocal(target, dp, value);
			return "applied";
		}

		if (PlainPropertyOf(target, command.Property) is { } property)
		{
			old = Text(property.GetValue(target));
			if (!Diagnostics.CanParseValue(property.PropertyType))
				return $"cannot set {command.Property}: SUIsei has no markup syntax for a {TapProperties.FriendlyName(property.PropertyType)}";
			if (!Diagnostics.TryParseValue(property.PropertyType, command.Value, out var value))
				return $"cannot set {command.Property}: '{command.Value}' is not a {TapProperties.FriendlyName(property.PropertyType)}";
			property.SetValue(target, value);
			return "applied";
		}

		return "property not found";
	}

	private string ClearProperty(TapApplyCommand command, ref ulong handle, ref string old)
	{
		var (target, unresolved) = _targets.Resolve(command.Target);
		if (target is null)
			return unresolved!;
		handle = handles.HandleOf(target);

		if (DependencyPropertyOf(target, command.Property) is { } dp)
		{
			old = Text(CurrentValue(target, dp));
			Diagnostics.ClearLocal(target, dp);
			return "applied";
		}

		if (PlainPropertyOf(target, command.Property) is { } property)
		{
			old = Text(property.GetValue(target));
			return $"cannot clear {command.Property}: it is a plain property with no local value to clear, so set the value wanted instead";
		}

		return "property not found";
	}

	private string Create(TapApplyCommand command, ref ulong handle)
	{
		if (command.Property.Length == 0)
			return "cannot build: no type was named";
		var (type, refusal) = TapTypes.Element(command.Property);
		if (type is null)
			return refusal!;
		var element = (UIElement)Activator.CreateInstance(type)!;
		_targets.Bind(command.Target, element);
		handle = handles.HandleOf(element);
		return "applied";
	}

	private string AddChild(TapApplyCommand command, ref ulong handle)
	{
		var (parent, unresolvedParent) = _targets.Resolve(command.Target);
		if (parent is null)
			return unresolvedParent!;
		handle = handles.HandleOf(parent);

		var (child, unresolvedChild) = _targets.Resolve(command.Arg);
		if (child is null)
			return unresolvedChild!;
		if (Diagnostics.ParentOf(child) is not null)
			return $"cannot add: {command.Arg} is already in the tree";

		return TapContent.Add(parent, child, command.Index) ?? "applied";
	}

	private string RemoveChild(TapApplyCommand command, ref ulong handle)
	{
		var (child, unresolved) = _targets.Resolve(command.Target);
		if (child is null)
			return unresolved!;
		handle = handles.HandleOf(child);
		return TapContent.Remove(child) ?? "applied";
	}

	/// <summary>
	/// The dependency property <paramref name="name"/> names on <paramref name="element"/>: one of its type's own, or an
	/// attached one written <c>Owner.Name</c>, as markup sets it. Null where it names none, which a plain property may
	/// still answer.
	/// </summary>
	public static DependencyProperty? DependencyPropertyOf(UIElement element, string name)
	{
		var dot = name.IndexOf('.', StringComparison.Ordinal);
		if (dot > 0)
			return TapTypes.Attached(name[..dot], name[(dot + 1)..]);
		return Diagnostics.Properties(element)
			.FirstOrDefault(p => p.Property.Kind == DependencyPropertyKind.Normal && p.Name == name)?.Property;
	}

	private static PropertyInfo? PlainPropertyOf(UIElement element, string name) =>
		name.Contains('.', StringComparison.Ordinal)
			? null
			: element.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance) is { SetMethod.IsPublic: true, GetMethod.IsPublic: true } property
				&& property.GetIndexParameters().Length == 0
				? property
				: null;

	private static Type ValueTypeOf(DependencyProperty dp) => Diagnostics.ValueTypeOf(dp);

	// The property's effective value: as the property list reads it where the element holds or inherits one, else the
	// property's default, which an attached property the element has never been given is at.
	private static object? CurrentValue(UIElement element, DependencyProperty dp) =>
		Diagnostics.Properties(element).FirstOrDefault(p => ReferenceEquals(p.Property, dp)) is { } listed
			? listed.Value
			: Diagnostics.DefaultValueOf(dp);

	private static string Text(object? value) => TapProperties.Format(value).Text ?? "";
}
