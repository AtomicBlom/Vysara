using System.Globalization;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

using SUIsei.Markup;

namespace SUIsei.SourceGen;

partial class XamlSourceGenerator
{
	// --- Storyboards: animations, keyframes, easing functions and Storyboard.TargetProperty paths ---

	private enum AnimationKind { Double, Color, DoubleKeyFrames }

	// One step of a property path: `Property` alone, or `(Owner.Property)` with an optional xmlns prefix on the owner.
	private readonly record struct PathStep(string? Prefix, string? Owner, string Property)
	{
		public override string ToString() => Owner is null ? Property : Prefix is null ? $"({Owner}.{Property})" : $"({Prefix}:{Owner}.{Property})";
	}

	// Build a Storyboard local and return its variable name.
	private static string EmitStoryboard(Gen gen, Scope scope, XElement sbEl)
	{
		var sbVar = NewName(gen, "__sb");
		scope.Emit($"var {sbVar} = new global::SUIsei.Animation.Storyboard();");
		foreach (var animEl in sbEl.Elements())
			EmitAnimation(gen, scope, sbVar, animEl);
		return sbVar;
	}

	private static void EmitAnimation(Gen gen, Scope scope, string sbVar, XElement animEl)
	{
		var name = animEl.Name.LocalName;
		AnimationKind kind;
		switch (name)
		{
			case "DoubleAnimation": kind = AnimationKind.Double; break;
			case "ColorAnimation": kind = AnimationKind.Color; break;
			case "DoubleAnimationUsingKeyFrames": kind = AnimationKind.DoubleKeyFrames; break;
			default: gen.Report(BadPropertyElement, animEl, name); return;
		}

		var animVar = NewName(gen, "__anim");
		scope.Emit($"var {animVar} = new global::SUIsei.Animation.{name}();");

		// Every other attribute is a property of the animation (From, To, Duration, RepeatBehavior, …), its value converted
		// through the shared table.
		var animType = gen.Compilation.GetTypeByMetadataName($"SUIsei.Animation.{name}")!;
		XAttribute? targetName = null, targetProp = null;
		var hasDuration = false;
		foreach (var attr in animEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || attr.Name.Namespace == X || IsIgnorable(animEl, attr.Name.Namespace))
				continue;
			switch (attr.Name.LocalName)
			{
				case "Storyboard.TargetName": targetName = attr; continue;
				case "Storyboard.TargetProperty": targetProp = attr; continue;
				case "Duration": hasDuration = true; break;
			}
			EmitObjectAttribute(gen, scope, animVar, animType, attr);
		}

		// Children: an easing function, or a keyframe animation's keys (directly, or inside <X.KeyFrames>).
		var keyFrames = 0;
		var lastKeyTime = 0.0;
		foreach (var child in animEl.Elements())
		{
			var local = child.Name.LocalName;
			if (kind != AnimationKind.DoubleKeyFrames && local == name + ".EasingFunction")
				EmitEasingFunction(gen, scope, animVar, child);
			else if (kind == AnimationKind.DoubleKeyFrames && (local == name + ".KeyFrames" || local.IndexOf('.') < 0))
			{
				foreach (var keyEl in local.IndexOf('.') < 0 ? [child] : child.Elements())
				{
					keyFrames++;
					lastKeyTime = Math.Max(lastKeyTime, EmitKeyFrame(gen, scope, animVar, keyEl));
				}
			}
			else
				gen.Report(BadPropertyElement, child, local);
		}
		// A keyframe animation with no Duration lasts until its last key, as in WinUI; the runtime measures key times
		// against the duration, so it is written out.
		if (kind == AnimationKind.DoubleKeyFrames && !hasDuration && keyFrames > 0)
			scope.Emit($"{animVar}.Duration = global::System.TimeSpan.FromSeconds({Fmt(lastKeyTime)});");

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
		if (targetProp is not null && targetResolved && TryResolveTargetPath(gen, targetProp, targetType, kind, name, out var path))
			scope.Emit($"{animVar}.TargetProperty = {path};");
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
		scope.Emit($"var {keyVar} = new global::SUIsei.Animation.DoubleKeyFrame();");
		if (name == "DiscreteDoubleKeyFrame")
			scope.Emit($"{keyVar}.IsDiscrete = true;");

		var keyType = gen.Compilation.GetTypeByMetadataName("SUIsei.Animation.DoubleKeyFrame")!;
		var keyTime = 0.0;
		foreach (var attr in keyEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || attr.Name.Namespace == X || IsIgnorable(keyEl, attr.Name.Namespace))
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
		var contract = gen.Compilation.GetTypeByMetadataName("SUIsei.Animation.IEasingFunction");
		if (type is null || type.IsAbstract || contract is null || !type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default))
		{
			gen.Report(ns == ExtensionNs ? UnknownExtensionMember : UnknownElement, easeEl, easeEl.Name.LocalName);
			return;
		}

		var assignments = new List<string>();
		foreach (var attr in easeEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration || attr.Name.Namespace == X || IsIgnorable(easeEl, attr.Name.Namespace))
				continue;
			if (FindProperty(type, attr.Name.LocalName) is not { } prop)
				gen.Report(UnknownProperty, attr, easeEl.Name.LocalName, attr.Name.LocalName);
			else if (ConvertOrReport(gen, attr, prop.Type, attr.Value, prop.Name) is { } code)
				assignments.Add($"{prop.Name} = {code}");
		}
		scope.Emit($"{ownerVar}.EasingFunction = new {Fq(type)} {{ {string.Join(", ", assignments)} }};");
	}

	// Resolve a Storyboard.TargetProperty path against the target's type into a PropertyPath expression, checking each
	// step and that this kind of animation can drive the final property. Reports and returns false otherwise.
	private static bool TryResolveTargetPath(Gen gen, XAttribute attr, INamedTypeSymbol? targetType, AnimationKind kind,
		string animationName, out string expr)
	{
		expr = "";
		var raw = attr.Value.Trim();
		var steps = ParsePropertyPath(raw);
		if (steps is null)
		{
			gen.Report(BadPropertyPath, attr, raw, "write a property name, or (Owner.Property) steps joined by '.'");
			return false;
		}

		var dependencyObject = gen.Compilation.GetTypeByMetadataName("SUIsei.DependencyObject");
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
				// Not a dependency property: the path may name one of the element channels instead.
				if (i == 0 && ElementChannel(raw, steps) is { } channel && FindAnyProperty(owner, step.Property) is not null)
					return TryElementChannel(gen, attr, raw, channel, kind, animationName, out expr);
				gen.Report(BadPropertyPath, attr, raw, $"'{owner.Name}' has no dependency property '{step.Property}'");
				return false;
			}

			var declaring = dp.ContainingType;
			if (DependencyValueType(dp) is not { } valueType)
			{
				gen.Report(BadPropertyPath, attr, raw, $"'{declaring.Name}.{step.Property}' is declared without its value type");
				return false;
			}
			if (current is not null && !IsAttachedProperty(declaring, step.Property) && !InheritsFrom(current, declaring))
			{
				gen.Report(BadPropertyPath, attr, raw, $"'{declaring.Name}.{step.Property}' does not apply to a {current.Name}");
				return false;
			}
			fields.Add($"{Fq(declaring)}.{dp.Name}");

			if (i == steps.Count - 1)
			{
				if (!CanAnimate(kind, valueType))
				{
					gen.Report(BadAnimationProperty, attr, raw, valueType.Name, animationName);
					return false;
				}
				break;
			}
			if (valueType is not INamedTypeSymbol next || dependencyObject is null || !InheritsFrom(next, dependencyObject))
			{
				gen.Report(BadPropertyPath, attr, raw, $"{step} holds a {valueType.Name}, not an object with properties to step into");
				return false;
			}
			current = next;
		}

		expr = $"new global::SUIsei.Animation.PropertyPath({string.Join(", ", fields)})";
		return true;
	}

	// Parse a property path into steps: `Property` or `(Owner.Property)`, joined by '.'; an owner may carry an xmlns
	// prefix, as in `(suisei:Owner.Property)`. Null when malformed.
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
	// extension namespace for `suisei:`). An unprefixed one is the type itself or a base of the object the step starts
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

	// The paths that name an element channel rather than a dependency property: a plain colour property (`Background`,
	// or WinUI's `(Border.Background).(SolidColorBrush.Color)`), and the offset an animation adds to the render
	// transform (`RenderTransform.Translate.X`, `(UIElement.RenderTransform).(CompositeTransform.TranslateX)`). Returns
	// the PropertyPath member and whether it takes a colour, or null.
	private static (string Member, bool IsColor)? ElementChannel(string raw, List<PathStep> steps)
	{
		if (raw == "RenderTransform.Translate.X")
			return ("TranslateX", false);
		if (raw == "RenderTransform.Translate.Y")
			return ("TranslateY", false);

		var first = steps[0].Property;
		var isColourProperty = first is "Background" or "Foreground" or "Fill";
		if (steps.Count == 1)
			return isColourProperty ? ("Fill", true) : null;
		if (steps.Count != 2)
			return null;

		var second = steps[1];
		if (isColourProperty && second is { Owner: "SolidColorBrush", Property: "Color" })
			return ("Fill", true);
		if (first != "RenderTransform")
			return null;
		return second switch
		{
			{ Owner: "CompositeTransform", Property: "TranslateX" } or { Owner: "TranslateTransform", Property: "X" } => ("TranslateX", false),
			{ Owner: "CompositeTransform", Property: "TranslateY" } or { Owner: "TranslateTransform", Property: "Y" } => ("TranslateY", false),
			_ => null,
		};
	}

	private static bool TryElementChannel(Gen gen, XAttribute attr, string raw, (string Member, bool IsColor) channel,
		AnimationKind kind, string animationName, out string expr)
	{
		expr = "";
		if (channel.IsColor != (kind == AnimationKind.Color))
		{
			gen.Report(BadAnimationProperty, attr, raw, channel.IsColor ? "HdrColor" : "Single", animationName);
			return false;
		}
		expr = $"global::SUIsei.Animation.PropertyPath.{channel.Member}";
		return true;
	}

	private static bool CanAnimate(AnimationKind kind, ITypeSymbol valueType) => kind == AnimationKind.Color
		? valueType.ToDisplayString() == "SUIsei.HdrColor"
		: valueType.SpecialType is SpecialType.System_Single or SpecialType.System_Double;

	// An attached property's owner declares a static Set<Name>(target, value), as WinUI's do.
	private static bool IsAttachedProperty(INamedTypeSymbol owner, string property) =>
		owner.GetMembers("Set" + property).OfType<IMethodSymbol>().Any(m => m is { IsStatic: true, Parameters.Length: 2 });

	private static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
	{
		for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
			if (SymbolEqualityComparer.Default.Equals(t, baseType))
				return true;
		return false;
	}
}
