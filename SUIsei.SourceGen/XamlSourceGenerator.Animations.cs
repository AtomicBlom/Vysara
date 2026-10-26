using System.Globalization;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;

namespace SUIsei.SourceGen;

partial class XamlSourceGenerator
{
	// --- Storyboards: animations, keyframes, easing functions and Storyboard.TargetProperty paths ---

	private enum AnimationKind { Double, Color, DoubleKeyFrames }

	// One step of a property path: `Property` alone, or `(Owner.Property)`.
	private readonly record struct PathStep(string? Owner, string Property)
	{
		public override string ToString() => Owner is null ? Property : $"({Owner}.{Property})";
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

		XAttribute? targetName = null, targetProp = null;
		var hasDuration = false;
		foreach (var attr in animEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration)
				continue;
			switch (attr.Name.LocalName)
			{
				case "Storyboard.TargetName": targetName = attr; break;
				case "Storyboard.TargetProperty": targetProp = attr; break;
				case "From" or "To" when kind != AnimationKind.DoubleKeyFrames:
				case "By" when kind == AnimationKind.Double:
					EmitAnimationValue(gen, scope, animVar, attr.Name.LocalName, kind, attr);
					break;
				case "Duration":
					hasDuration = true;
					if (TryParseDuration(attr.Value, out var d)) scope.Emit($"{animVar}.Duration = global::System.TimeSpan.FromSeconds({Fmt(d)});");
					else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "Duration");
					break;
				case "BeginTime":
					if (TryParseDuration(attr.Value, out var bt)) scope.Emit($"{animVar}.BeginTime = global::System.TimeSpan.FromSeconds({Fmt(bt)});");
					else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "BeginTime");
					break;
				case "AutoReverse":
					scope.Emit($"{animVar}.AutoReverse = {(string.Equals(attr.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase) ? "true" : "false")};");
					break;
				case "FillBehavior":
					scope.Emit($"{animVar}.FillBehavior = global::SUIsei.Animation.FillBehavior.{(attr.Value.Trim() == "Stop" ? "Stop" : "HoldEnd")};");
					break;
				case "RepeatBehavior":
					scope.Emit($"{animVar}.RepeatBehavior = {RepeatBehaviorExpr(attr.Value)};");
					break;
			}
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

	private static void EmitAnimationValue(Gen gen, Scope scope, string animVar, string prop, AnimationKind kind, XAttribute attr)
	{
		if (kind == AnimationKind.Color)
		{
			if (TryParseColor(attr.Value, out var code)) scope.Emit($"{animVar}.{prop} = {code};");
			else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "colour");
		}
		else if (double.TryParse(attr.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
			scope.Emit($"{animVar}.{prop} = {attr.Value.Trim()}d;");
		else
			gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "number");
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

		var keyTime = 0.0;
		foreach (var attr in keyEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration)
				continue;
			switch (attr.Name.LocalName)
			{
				case "KeyTime":
					if (TryParseDuration(attr.Value, out keyTime)) scope.Emit($"{keyVar}.KeyTime = global::System.TimeSpan.FromSeconds({Fmt(keyTime)});");
					else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "KeyTime");
					break;
				case "Value":
					if (double.TryParse(attr.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)) scope.Emit($"{keyVar}.Value = {attr.Value.Trim()}d;");
					else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "number");
					break;
				default:
					gen.Report(UnknownProperty, attr, name, attr.Name.LocalName);
					break;
			}
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

	// <Owner.EasingFunction> holding one easing element from SUIsei.Animation (CubicEase, QuadraticEase, BackEase, …),
	// its attributes set as literal properties (EasingMode, Amplitude).
	private static void EmitEasingFunction(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var easings = propEl.Elements().ToList();
		if (easings.Count != 1)
		{
			gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName);
			return;
		}

		var easeEl = easings[0];
		var type = gen.Compilation.GetTypeByMetadataName("SUIsei.Animation." + easeEl.Name.LocalName);
		var contract = gen.Compilation.GetTypeByMetadataName("SUIsei.Animation.IEasingFunction");
		if (type is null || type.IsAbstract || contract is null || !type.AllInterfaces.Contains(contract, SymbolEqualityComparer.Default))
		{
			gen.Report(UnknownElement, easeEl, easeEl.Name.LocalName);
			return;
		}

		var assignments = new List<string>();
		foreach (var attr in easeEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration)
				continue;
			if (FindProperty(type, attr.Name.LocalName) is not { } prop)
				gen.Report(UnknownProperty, attr, easeEl.Name.LocalName, attr.Name.LocalName);
			else if (TryLiteral(prop.Type, attr.Value, out var code) == LiteralStatus.Ok)
				assignments.Add($"{prop.Name} = {code}");
			else
				gen.Report(InvalidLiteral, attr, attr.Value.Trim(), prop.Name, prop.Type.ToDisplayString());
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
			var owner = step.Owner is null ? current : ResolvePathOwner(gen, step.Owner, current);
			if (owner is null)
			{
				gen.Report(BadPropertyPath, attr, raw, step.Owner is null
					? "there is no Storyboard.TargetName to look the property up on"
					: $"'{step.Owner}' is not a known type");
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

	// Parse a property path into steps: `Property` or `(Owner.Property)`, joined by '.'. Null when malformed. An xmlns
	// prefix on an owner is accepted; owners resolve by name, so it does not change which type is found.
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
				var colon = inner.IndexOf(':');
				if (colon >= 0)
					inner = inner.Substring(colon + 1);
				var dot = inner.LastIndexOf('.');
				if (dot <= 0)
					return null;
				var owner = inner.Substring(0, dot).Trim();
				var property = inner.Substring(dot + 1).Trim();
				if (!IsIdentifier(owner) || !IsIdentifier(property))
					return null;
				steps.Add(new PathStep(owner, property));
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
				steps.Add(new PathStep(null, property));
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

	// A path step's owner: the type itself or a base of the object the step starts from, else a SUIsei type (the owner
	// of an attached property, such as Canvas).
	private static INamedTypeSymbol? ResolvePathOwner(Gen gen, string owner, INamedTypeSymbol? current)
	{
		for (var t = current; t is not null; t = t.BaseType)
			if (t.Name == owner)
				return t;
		return ResolveType(gen.Compilation, owner);
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
