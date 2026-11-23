using System.Globalization;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

using BinaryVibrance.Vysara.Markup;

namespace BinaryVibrance.Vysara.SourceGen;

partial class XamlSourceGenerator
{
	// --- Storyboards: animations, keyframes, easing functions and Storyboard.TargetProperty paths ---

	private enum AnimationKind { Double, Color, DoubleKeyFrames, ObjectKeyFrames }

	// One step of a property path: `Property` alone, or `(Owner.Property)` with an optional xmlns prefix on the owner.
	private readonly record struct PathStep(string? Prefix, string? Owner, string Property)
	{
		public override string ToString() => Owner is null ? Property : Prefix is null ? $"({Owner}.{Property})" : $"({Prefix}:{Owner}.{Property})";
	}

	// Build a Storyboard local and return its variable name. `ownerVar` is the element whose visual states hold it, where
	// a {StaticResource} among its values is looked up from.
	private static string EmitStoryboard(Gen gen, Scope scope, string ownerVar, XElement sbEl)
	{
		var sbVar = NewName(gen, "__sb");
		scope.Emit($"var {sbVar} = new global::BinaryVibrance.Vysara.Animation.Storyboard();");
		// The storyboard itself takes nothing: timing and targets are set on each animation it holds.
		foreach (var attr in sbEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || IsIgnorable(sbEl, attr.Name.Namespace) || IsDirective(gen, sbEl, attr))
				continue;
			gen.Report(OutsideSubset, attr, $"'{attr.Name.LocalName}' on a Storyboard", " (set it on each animation the storyboard holds)");
		}
		foreach (var animEl in sbEl.Elements())
		{
			if (!IsIgnorable(animEl, animEl.Name.Namespace))
				EmitAnimation(gen, scope, ownerVar, sbVar, animEl);
		}
		return sbVar;
	}

	private static void EmitAnimation(Gen gen, Scope scope, string ownerVar, string sbVar, XElement animEl)
	{
		var name = animEl.Name.LocalName;
		AnimationKind kind;
		switch (name)
		{
			case "DoubleAnimation": kind = AnimationKind.Double; break;
			case "ColorAnimation": kind = AnimationKind.Color; break;
			case "DoubleAnimationUsingKeyFrames": kind = AnimationKind.DoubleKeyFrames; break;
			case "ObjectAnimationUsingKeyFrames": kind = AnimationKind.ObjectKeyFrames; break;
			default: gen.Report(BadPropertyElement, animEl, name); return;
		}
		var hasKeyFrames = kind is AnimationKind.DoubleKeyFrames or AnimationKind.ObjectKeyFrames;

		var animVar = NewName(gen, "__anim");
		scope.Emit($"var {animVar} = new global::BinaryVibrance.Vysara.Animation.{name}();");

		// Every other attribute is a property of the animation (From, To, Duration, RepeatBehavior, …), its value converted
		// through the shared table.
		var animType = gen.Compilation.GetTypeByMetadataName($"BinaryVibrance.Vysara.Animation.{name}")!;
		XAttribute? targetName = null, targetProp = null;
		var hasDuration = false;
		foreach (var attr in animEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || IsIgnorable(animEl, attr.Name.Namespace) || IsDirective(gen, animEl, attr))
				continue;
			switch (attr.Name.LocalName)
			{
				case "Storyboard.TargetName": targetName = attr; continue;
				case "Storyboard.TargetProperty": targetProp = attr; continue;
				case "Duration": hasDuration = true; break;
			}
			EmitObjectAttribute(gen, scope, animVar, animType, attr);
		}

		// The target first: an object animation's values convert for the type of the property it sets.
		INamedTypeSymbol? targetType = null;
		var targetResolved = targetName is null;
		if (targetName is not null)
		{
			var tn = targetName.Value.Trim();
			if (scope.NamedElements.TryGetValue(tn, out var named))
			{
				targetType = named.Type;
				targetResolved = true;
				scope.Emit($"{animVar}.Target = {named.Expr};");
			}
			else
				gen.Report(BadStoryboardTarget, targetName, tn);
		}
		ITypeSymbol? valueType = null;
		IFieldSymbol? propertyField = null;
		if (targetProp is not null && targetResolved && TryResolveTargetPath(gen, targetProp, targetType, kind, name, out var path, out valueType, out propertyField))
			scope.Emit($"{animVar}.TargetProperty = {path};");
		else if (targetProp is null && kind == AnimationKind.ObjectKeyFrames)
			gen.Report(BadPropertyElement, animEl, $"{name} (needs a Storyboard.TargetProperty, whose type its values convert for)");

		// Children: an easing function, or a keyframe animation's keys (directly, or inside <X.KeyFrames>).
		var keyFrames = 0;
		var lastKeyTime = 0.0;
		foreach (var child in animEl.Elements())
		{
			var local = child.Name.LocalName;
			if (!hasKeyFrames && local == name + ".EasingFunction")
				EmitEasingFunction(gen, scope, animVar, child);
			else if (hasKeyFrames && (local == name + ".KeyFrames" || local.IndexOf('.') < 0))
			{
				foreach (var keyEl in local.IndexOf('.') < 0 ? [child] : child.Elements())
				{
					keyFrames++;
					var keyTime = kind == AnimationKind.ObjectKeyFrames
						? EmitObjectKeyFrame(gen, scope, ownerVar, animVar, keyEl, targetProp?.Value.Trim() ?? "", valueType, propertyField)
						: EmitKeyFrame(gen, scope, animVar, keyEl);
					lastKeyTime = Math.Max(lastKeyTime, keyTime);
				}
			}
			else
				gen.Report(BadPropertyElement, child, local);
		}
		// A keyframe animation with no Duration lasts until its last key, as in WinUI; the runtime measures key times
		// against the duration, so it is written out.
		if (hasKeyFrames && !hasDuration && keyFrames > 0)
			scope.Emit($"{animVar}.Duration = global::System.TimeSpan.FromSeconds({Fmt(lastKeyTime)});");

		scope.Emit($"{sbVar}.Children.Add({animVar});");
	}

	// One <Linear|Discrete|EasingDoubleKeyFrame>, added to the animation's keys. Returns its key time in seconds.
	private static double EmitKeyFrame(Gen gen, Scope scope, string animVar, XElement keyEl)
	{
		var name = keyEl.Name.LocalName;
		if (name is not ("LinearDoubleKeyFrame" or "DiscreteDoubleKeyFrame" or "EasingDoubleKeyFrame"))
		{
			gen.Report(BadPropertyElement, keyEl, name);
			return 0;
		}

		var keyVar = NewName(gen, "__key");
		scope.Emit($"var {keyVar} = new global::BinaryVibrance.Vysara.Animation.DoubleKeyFrame();");
		if (name == "DiscreteDoubleKeyFrame")
			scope.Emit($"{keyVar}.IsDiscrete = true;");

		var keyType = gen.Compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.Animation.DoubleKeyFrame")!;
		var keyTime = 0.0;
		foreach (var attr in keyEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || IsIgnorable(keyEl, attr.Name.Namespace) || IsDirective(gen, keyEl, attr))
				continue;
			if (attr.Name.LocalName == "KeyTime" && !XamlValueSyntax.TryParseDuration(attr.Value, out keyTime))
				keyTime = 0;
			EmitObjectAttribute(gen, scope, keyVar, keyType, attr);
		}

		foreach (var child in keyEl.Elements())
		{
			if (name == "EasingDoubleKeyFrame" && child.Name.LocalName == "EasingDoubleKeyFrame.EasingFunction")
				EmitEasingFunction(gen, scope, keyVar, child);
			else
				gen.Report(BadPropertyElement, child, child.Name.LocalName);
		}

		scope.Emit($"{animVar}.KeyFrames.Add({keyVar});");
		return keyTime;
	}

	// One <DiscreteObjectKeyFrame>, added to the animation's keys, its Value converted for `valueType`, the type of the
	// property the animation sets (null when the path did not resolve, which is reported already). Returns its key time in
	// seconds.
	private static double EmitObjectKeyFrame(Gen gen, Scope scope, string ownerVar, string animVar, XElement keyEl, string propertyName,
		ITypeSymbol? valueType, IFieldSymbol? propertyField)
	{
		var name = keyEl.Name.LocalName;
		if (name != "DiscreteObjectKeyFrame")
		{
			gen.Report(BadPropertyElement, keyEl, name);
			return 0;
		}

		var keyVar = NewName(gen, "__key");
		scope.Emit($"var {keyVar} = new global::BinaryVibrance.Vysara.Animation.DiscreteObjectKeyFrame();");

		var keyType = gen.Compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.Animation.DiscreteObjectKeyFrame")!;
		var keyTime = 0.0;
		XAttribute? valueAttr = null;
		foreach (var attr in keyEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || IsIgnorable(keyEl, attr.Name.Namespace) || IsDirective(gen, keyEl, attr))
				continue;
			if (attr.Name.LocalName == "Value")
			{
				valueAttr = attr;
				continue;
			}
			if (attr.Name.LocalName == "KeyTime" && !XamlValueSyntax.TryParseDuration(attr.Value, out keyTime))
				keyTime = 0;
			EmitObjectAttribute(gen, scope, keyVar, keyType, attr);
		}

		XElement? valueEl = null;
		foreach (var child in keyEl.Elements())
		{
			if (child.Name.LocalName == name + ".Value" && valueEl is null)
				valueEl = child;
			else
				gen.Report(BadPropertyElement, child, child.Name.LocalName);
		}

		if ((valueAttr is null) == (valueEl is null))
			gen.Report(BadPropertyElement, keyEl, $"{name} (needs a Value, as an attribute or a <{name}.Value>)");
		else if (valueType is not null && ObjectKeyFrameValue(gen, scope, ownerVar, valueAttr, valueEl, valueType, propertyName, propertyField) is { } code)
			scope.Emit($"{keyVar}.Value = {code};");

		scope.Emit($"{animVar}.KeyFrames.Add({keyVar});");
		return keyTime;
	}

	// A key's Value, written as a setter's is: text converted for the property's type (an enum member, a colour for a
	// brush), {StaticResource}, {x:Null} or {x:Static}, or in <X.Value> text or one object of the type. Null when reported.
	private static string? ObjectKeyFrameValue(Gen gen, Scope scope, string ownerVar, XAttribute? valueAttr, XElement? valueEl,
		ITypeSymbol valueType, string propertyName, IFieldSymbol? propertyField)
	{
		if (valueAttr is not null)
		{
			if (!IsMarkup(valueAttr.Value, out var inner))
				return ConvertOrReport(gen, valueAttr, valueType, valueAttr.Value, propertyName, propertyField);
			if (TryMarkupValue(gen, scope, valueAttr, ownerVar, inner, valueType, propertyName, out var markupCode))
				return markupCode;
			gen.Report(UnsupportedMarkup, valueAttr, valueAttr.Value.Trim());
			return null;
		}

		var children = ObjectChildren(valueEl!);
		var text = TextContent(valueEl!);
		if (children.Count == 0 && text is not null)
			return ConvertOrReport(gen, valueEl!, valueType, text, propertyName, propertyField);
		if (children.Count == 1 && text is null)
		{
			var child = children[0];
			var ns = child.Name.Namespace == XNamespace.None ? Ui : child.Name.Namespace;
			// An element that names no type reports itself as it is emitted.
			if (ResolveType(gen.Compilation, ns, child.Name.LocalName) is null || ChildFits(gen, child, valueType))
				return EmitElement(gen, scope, child);
		}
		gen.Report(BadPropertyElement, valueEl!, $"{valueEl!.Name.LocalName} (takes one value of {propertyName}'s type, {valueType.ToDisplayString()})");
		return null;
	}

	// <Owner.EasingFunction> holding one easing element (CubicEase, QuadraticEase, BackEase, …), its attributes set as
	// properties through the shared converter table (EasingMode, Amplitude).
	private static void EmitEasingFunction(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var easings = ObjectChildren(propEl);
		if (easings.Count != 1)
		{
			gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName);
			return;
		}

		var easeEl = easings[0];
		var ns = easeEl.Name.Namespace == XNamespace.None ? Ui : easeEl.Name.Namespace;
		var type = ResolveType(gen.Compilation, ns, easeEl.Name.LocalName);
		var contract = gen.Compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.Animation.IEasingFunction");
		if (type is null || type.IsAbstract || contract is null || !type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default))
		{
			gen.Report(ns == ExtensionNs ? UnknownExtensionMember : UnknownElement, easeEl, easeEl.Name.LocalName);
			return;
		}

		var assignments = new List<string>();
		foreach (var attr in easeEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || IsIgnorable(easeEl, attr.Name.Namespace) || IsDirective(gen, easeEl, attr))
				continue;
			if (FindProperty(type, attr.Name.LocalName) is not { } prop)
				gen.Report(UnknownProperty, attr, easeEl.Name.LocalName, attr.Name.LocalName);
			else if (ConvertOrReport(gen, attr, prop.Type, attr.Value, prop.Name) is { } code)
				assignments.Add($"{prop.Name} = {code}");
		}
		scope.Emit($"{ownerVar}.EasingFunction = new {Fq(type)} {{ {string.Join(", ", assignments)} }};");
	}

	// Resolve a Storyboard.TargetProperty path against the target's type into a PropertyPath expression, checking each
	// step and that this kind of animation can drive the final property, whose value type is `propertyType`. Reports and
	// returns false otherwise.
	private static bool TryResolveTargetPath(Gen gen, XAttribute attr, INamedTypeSymbol? targetType, AnimationKind kind,
		string animationName, out string expr, out ITypeSymbol? propertyType, out IFieldSymbol? propertyField)
	{
		expr = "";
		propertyType = null;
		propertyField = null;
		var raw = attr.Value.Trim();
		if (raw is "RenderTransform.Translate.X" or "RenderTransform.Translate.Y")
		{
			gen.Report(BrushOrTransformShorthand, attr, raw, "names no property: RenderTransform holds a Transform, whose own property the animation drives",
				$"(UIElement.RenderTransform).(TranslateTransform.{raw[raw.Length - 1]})");
			return false;
		}
		var steps = ParsePropertyPath(raw);
		if (steps is null)
		{
			gen.Report(BadPropertyPath, attr, raw, "write a property name, or (Owner.Property) steps joined by '.'");
			return false;
		}

		var dependencyObject = gen.Compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.DependencyObject");
		var fields = new List<string>();
		var current = targetType;
		for (var i = 0; i < steps.Count; i++)
		{
			var step = steps[i];
			var owner = step.Owner is null ? current : ResolvePathOwner(gen, step, current, attr.Parent!);
			if (owner is null)
			{
				if (step.Prefix is not null && attr.Parent!.GetNamespaceOfPrefix(step.Prefix) == ExtensionNs)
					gen.Report(UnknownExtensionMember, attr, $"{step.Owner}.{step.Property}");
				else
					gen.Report(BadPropertyPath, attr, raw, step.Owner is null
						? "there is no Storyboard.TargetName to look the property up on"
						: $"'{step}' names no known type");
				return false;
			}

			if (FindDependencyProperty(owner, step.Property) is not { } dp)
			{
				gen.Report(BadPropertyPath, attr, raw, $"'{owner.Name}' has no dependency property '{step.Property}'");
				return false;
			}

			var declaring = dp.ContainingType;
			if (DependencyValueType(dp) is not { } valueType)
			{
				gen.Report(BadPropertyPath, attr, raw, $"'{declaring.Name}.{step.Property}' is declared without its value type");
				return false;
			}
			// A step may name a more derived type than the object before it is declared to hold, as WinUI's
			// (Border.Background).(SolidColorBrush.Color) does; the object found is checked when the animation begins.
			if (current is not null && !IsAttachedProperty(declaring, step.Property) && !InheritsFrom(current, declaring) && !InheritsFrom(declaring, current))
			{
				gen.Report(BadPropertyPath, attr, raw, $"'{declaring.Name}.{step.Property}' does not apply to a {current.Name}");
				return false;
			}
			// An attached property applies to what its setter takes, which may name a more derived type, as above.
			if (current is not null && AttachedSetter(declaring, step.Property) is { } setter
				&& setter.Parameters[0].Type is INamedTypeSymbol holder && !AttachesTo(setter, current) && !InheritsFrom(holder, current))
			{
				gen.Report(BadPropertyPath, attr, raw, $"'{declaring.Name}.{step.Property}' is set on a {holder.Name}, not a {current.Name}");
				return false;
			}
			fields.Add($"{Fq(declaring)}.{dp.Name}");

			if (i == steps.Count - 1)
			{
				// A colour animation aimed at a brush property: the colour it means is the brush's, through the WinUI path.
				if (kind == AnimationKind.Color && IsBrush(gen, valueType))
				{
					var before = string.Join(".", steps.Take(i).Select(static s => s.ToString()));
					var last = step.Owner is null ? $"({(current ?? declaring).Name}.{step.Property})" : step.ToString();
					gen.Report(BrushOrTransformShorthand, attr, raw, $"names {declaring.Name}.{step.Property}, which holds a {valueType.Name} rather than a colour",
						$"{(before.Length > 0 ? before + "." : "")}{last}.(SolidColorBrush.Color)");
					return false;
				}
				if (!CanAnimate(kind, valueType))
				{
					gen.Report(BadAnimationProperty, attr, raw, valueType.Name, animationName);
					return false;
				}
				propertyType = valueType;
				propertyField = dp;
				break;
			}
			if (valueType is not INamedTypeSymbol next || dependencyObject is null || !InheritsFrom(next, dependencyObject))
			{
				gen.Report(BadPropertyPath, attr, raw, $"{step} holds a {valueType.Name}, not an object with properties to step into");
				return false;
			}
			current = next;
		}

		expr = $"new global::BinaryVibrance.Vysara.Animation.PropertyPath({string.Join(", ", fields)})";
		return true;
	}

	// Parse a property path into steps: `Property` or `(Owner.Property)`, joined by '.'; an owner may carry an xmlns
	// prefix, as in `(vysara:Owner.Property)`. Null when malformed.
	private static List<PathStep>? ParsePropertyPath(string raw)
	{
		var steps = new List<PathStep>();
		var i = 0;
		while (i < raw.Length)
		{
			if (raw[i] == '(')
			{
				var close = raw.IndexOf(')', i);
				if (close < 0)
					return null;
				var inner = raw.Substring(i + 1, close - i - 1).Trim();
				string? prefix = null;
				var colon = inner.IndexOf(':');
				if (colon >= 0)
				{
					prefix = inner.Substring(0, colon).Trim();
					inner = inner.Substring(colon + 1);
					if (!IsIdentifier(prefix))
						return null;
				}
				var dot = inner.LastIndexOf('.');
				if (dot <= 0)
					return null;
				var owner = inner.Substring(0, dot).Trim();
				var property = inner.Substring(dot + 1).Trim();
				if (!IsIdentifier(owner) || !IsIdentifier(property))
					return null;
				steps.Add(new PathStep(prefix, owner, property));
				i = close + 1;
			}
			else
			{
				var end = raw.IndexOf('.', i);
				if (end < 0)
					end = raw.Length;
				var property = raw.Substring(i, end - i).Trim();
				if (!IsIdentifier(property))
					return null;
				steps.Add(new PathStep(null, null, property));
				i = end;
			}

			if (i < raw.Length)
			{
				if (raw[i] != '.' || i == raw.Length - 1)
					return null;
				i++;
			}
		}
		return steps.Count > 0 ? steps : null;
	}

	// A path step's owner. A prefixed owner resolves through the prefix's XML namespace in scope at `context` (the
	// extension namespace for `vysara:`). An unprefixed one is the type itself or a base of the object the step starts
	// from, else a type in the default namespace (the owner of an attached property, such as Canvas).
	private static INamedTypeSymbol? ResolvePathOwner(Gen gen, PathStep step, INamedTypeSymbol? current, XElement context)
	{
		if (step.Prefix is not null)
			return context.GetNamespaceOfPrefix(step.Prefix) is { } ns ? ResolveType(gen.Compilation, ns, step.Owner!) : null;
		for (var t = current; t is not null; t = t.BaseType)
		{
			if (t.Name == step.Owner)
				return t;
		}
		return ResolveType(gen.Compilation, step.Owner!);
	}

	// Whether a property's value type is a brush (BinaryVibrance.Vysara.Media.Brush or a subclass).
	private static bool IsBrush(Gen gen, ITypeSymbol valueType) =>
		valueType is INamedTypeSymbol named && gen.Compilation.GetTypeByMetadataName("BinaryVibrance.Vysara.Media.Brush") is { } brush
		&& InheritsFrom((INamedTypeSymbol)named.WithNullableAnnotation(NullableAnnotation.NotAnnotated), brush);

	// An object animation sets a value of any type, interpolating nothing.
	private static bool CanAnimate(AnimationKind kind, ITypeSymbol valueType) => kind switch
	{
		AnimationKind.ObjectKeyFrames => true,
		AnimationKind.Color => valueType.ToDisplayString() == "BinaryVibrance.Vysara.HdrColor",
		_ => valueType.SpecialType is SpecialType.System_Single or SpecialType.System_Double,
	};

	// An attached property's owner declares a static Set<Name>(target, value), as WinUI's do.
	private static bool IsAttachedProperty(INamedTypeSymbol owner, string property) => AttachedSetter(owner, property) is not null;

	private static IMethodSymbol? AttachedSetter(INamedTypeSymbol owner, string property) =>
		owner.GetMembers("Set" + property).OfType<IMethodSymbol>().FirstOrDefault(m => m is { IsStatic: true, Parameters.Length: 2 });

	private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
	{
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
			if (SymbolEqualityComparer.Default.Equals(t, baseType))
				return true;
		return false;
	}
}
