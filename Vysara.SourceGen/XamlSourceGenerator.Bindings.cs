using System.Text;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Vysara.SourceGen;

partial class XamlSourceGenerator
{
	private const string Visible = "global::Vysara.Visibility.Visible";
	private const string Collapsed = "global::Vysara.Visibility.Collapsed";

	// --- Resolving one {x:Bind} ---

	// One {x:Bind} on `prop` of the element `targetVar` (of `targetType`): resolved against its root (the page, a
	// DataTemplate's item, or a ControlTemplate's templated control), checked each way it flows, then handed on. On the page
	// it joins the page's bindings (OneTime ones assigned in InitializeComponent, the rest tracked by the _Bindings helper);
	// in a template a OneTime one is assigned in the factory, and a OneWay or TwoWay one in a DataTemplate is tracked by the
	// template's own helper, one per realized element.
	private static void EmitXBind(Gen gen, Scope scope, XAttribute attr, string targetVar, INamedTypeSymbol targetType, BindTarget prop, XBindSpec spec)
	{
		// FallbackValue / TargetNullValue are values of the property, converted like any other.
		var fallbackCode = spec.Fallback is null ? null : ConvertOrReport(gen, attr, prop.Type, spec.Fallback, $"{prop.Name} FallbackValue", prop.Member);
		var targetNullCode = spec.TargetNull is null ? null : ConvertOrReport(gen, attr, prop.Type, spec.TargetNull, $"{prop.Name} TargetNullValue", prop.Member);
		if ((spec.Fallback is not null && fallbackCode is null) || (spec.TargetNull is not null && targetNullCode is null))
			return;

		if (scope.IsControlTemplate && spec.OneWay)
		{
			gen.Report(OutsideSubset, attr, $"Mode={(spec.TwoWay ? "TwoWay" : "OneWay")} on an {{x:Bind}} in a ControlTemplate",
				" (a ControlTemplate's {x:Bind} is read once, against the templated control; follow one of its properties with {TemplateBinding})");
			return;
		}
		if (prop.AttachedSetter is not null && spec.TwoWay)
		{
			gen.Report(TwoWayNoWriteBack, attr, prop.Name, "an attached property has no change event to write back from; bind it OneWay");
			return;
		}

		// The root a path starts from, and the expression that reaches it where the binding is first assigned.
		var rootType = scope.IsTemplate ? scope.DataRootType : gen.PageType;
		var rootName = scope.IsTemplate ? "the template data type (set x:DataType)" : gen.PageType?.ToDisplayString() ?? "the page";
		var rootExpr = !scope.IsTemplate ? "this" : scope.IsControlTemplate || rootType is not null ? scope.DataRootExpr : "__item";

		var bind = spec.Function is not null
			? ResolveFunctionBind(gen, attr, targetVar, targetType, prop, spec, rootType, rootName)
			: ResolvePathBind(gen, scope, attr, targetVar, targetType, prop, spec, rootType, rootName);
		if (bind is null)
			return;
		bind.FallbackValue = fallbackCode;
		bind.TargetNullValue = targetNullCode;
		if (scope.IsControlTemplate)
			ResolveTemplateRung(gen, attr, targetType, prop, bind);

		if (!scope.IsTemplate)
			gen.Bindings.Add(bind);
		else if (!bind.OneWay)
			scope.Emit(AssignStatement(bind, rootExpr, targetVar));
		else
			scope.TemplateBindings.Add(bind);
	}

	// A path binding: the path read from the root, or the root itself where there is no path (a template's item).
	private static Bind? ResolvePathBind(Gen gen, Scope scope, XAttribute attr, string targetVar, INamedTypeSymbol targetType, BindTarget prop,
		XBindSpec spec, INamedTypeSymbol? rootType, string rootName)
	{
		BindPath path;
		ITypeSymbol leafType;
		if (spec.Path.Length == 0)
		{
			if (!scope.IsTemplate)
			{
				gen.Report(BadXBind, attr, attr.Value.Trim(), "an {x:Bind} with no path binds a template's item; on a page, name a path");
				return null;
			}
			path = new BindPath([], []);
			leafType = (ITypeSymbol?)rootType ?? gen.Compilation.ObjectType;
		}
		else
		{
			if (!TryResolvePath(rootType, spec.Path, out var resultTypes, out leafType))
			{
				gen.Report(BindPathUnresolved, attr, string.Join(".", spec.Path), rootName);
				return null;
			}
			path = new BindPath(spec.Path, resultTypes);
		}
		if (!ForwardConversion(gen, attr, prop, leafType, out var toVisibility))
			return null;

		var bind = new Bind
		{
			TargetVar = targetVar,
			TargetProp = prop.Name,
			AttachedSetter = prop.AttachedSetter,
			TargetElementType = targetType,
			Path = path,
			LeafType = leafType,
			// The item itself is fixed for its element's life, so a binding to it is read once whatever its Mode.
			OneWay = spec.OneWay && path.Length > 0,
			ToVisibility = toVisibility,
		};
		if (!spec.TwoWay)
			return bind;

		// TwoWay: the control's {Prop}Changed writes the value back to the path's leaf, converted for it. What can't be
		// written back is an error whether or not the control has the event; a control without one is read OneWay.
		var reason = path.Length == 0 ? "a template's item itself has no path to write to"
			: toVisibility ? "a Visibility does not convert back to a bool"
			: WriteBackFormat(gen, prop.Type, leafType) is not { } format ? $"a {prop.Type.ToDisplayString()} does not convert to the source's {leafType.ToDisplayString()}"
			: !IsWritable(path.Length == 1 ? rootType : path.ResultTypes[path.Length - 2], path.Segments[path.Length - 1]) ? $"'{path.Segments[path.Length - 1]}' has no setter"
			: null;
		if (reason is not null)
		{
			gen.Report(TwoWayNoWriteBack, attr, prop.Name, reason);
			return null;
		}
		if (FindEvent(targetType, prop.Name + "Changed") is null)
		{
			gen.Report(TwoWayNoEvent, attr, targetType.ToDisplayString(), prop.Name);
			return bind;
		}
		bind.TwoWay = true;
		bind.WriteBack = WriteBackFormat(gen, prop.Type, leafType)!;
		return bind;
	}

	// A function binding: a static method, named through its type's xmlns prefix, called with the values of its argument
	// paths, each read from the binding's root. OneTime or OneWay; it has nothing to write a TwoWay value back to.
	private static Bind? ResolveFunctionBind(Gen gen, XAttribute attr, string targetVar, INamedTypeSymbol targetType, BindTarget prop,
		XBindSpec spec, INamedTypeSymbol? rootType, string rootName)
	{
		var function = spec.Function!;
		if (spec.TwoWay)
		{
			gen.Report(TwoWayNoWriteBack, attr, prop.Name, $"'{function}' is a function, with no path to write back to");
			return null;
		}
		var dot = function.LastIndexOf('.');
		var ownerName = function.Substring(0, dot);
		var methodName = function.Substring(dot + 1);
		if (ResolveTypeName(gen, attr.Parent!, ownerName) is not { } owner)
		{
			gen.Report(BadXBindFunction, attr, function, $"'{ownerName}' names no type (a type is named through its xmlns prefix, as xmlns:local=\"using:App.Views\")");
			return null;
		}

		var arguments = new BindPath[spec.Arguments.Count];
		for (var i = 0; i < arguments.Length; i++)
		{
			if (!TryResolvePath(rootType, spec.Arguments[i], out var resultTypes, out _))
			{
				gen.Report(BindPathUnresolved, attr, string.Join(".", spec.Arguments[i]), rootName);
				return null;
			}
			arguments[i] = new BindPath(spec.Arguments[i], resultTypes);
		}

		var named = owner.GetMembers(methodName).OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary).ToList();
		var callable = named.Where(m => m is { IsStatic: true, IsGenericMethod: false, ReturnsVoid: false }
			&& m.Parameters.All(static p => p.RefKind == RefKind.None)
			&& gen.Compilation.IsSymbolAccessibleWithin(m, GeneratedClass(gen))).ToList();
		var applicable = callable.Where(m => m.Parameters.Length == arguments.Length
			&& m.Parameters.Select((p, i) => gen.Compilation.ClassifyConversion(arguments[i].Leaf, p.Type).IsImplicit).All(static ok => ok)).ToList();
		if (applicable.Count > 1)
		{
			// The overload taking every argument as it is, where there is exactly one.
			var exact = applicable.Where(m => m.Parameters.Select((p, i) => SymbolEqualityComparer.Default.Equals(arguments[i].Leaf, p.Type)).All(static same => same)).ToList();
			if (exact.Count == 1)
				applicable = exact;
		}
		var argumentTypes = string.Join(", ", arguments.Select(static a => a.Leaf.ToDisplayString()));
		if (applicable.Count != 1)
		{
			gen.Report(BadXBindFunction, attr, function,
				named.Count == 0 ? $"{owner.ToDisplayString()} has no method '{methodName}'"
				: callable.Count == 0 ? $"'{methodName}' is not a static method the view can call, taking its arguments by value and returning a value"
				: applicable.Count == 0 ? $"no overload of '{methodName}' takes ({argumentTypes})"
				: $"more than one overload of '{methodName}' takes ({argumentTypes})");
			return null;
		}
		var method = applicable[0];
		if (!ForwardConversion(gen, attr, prop, method.ReturnType, out var toVisibility))
			return null;

		return new Bind
		{
			TargetVar = targetVar,
			TargetProp = prop.Name,
			AttachedSetter = prop.AttachedSetter,
			TargetElementType = targetType,
			Function = method,
			Arguments = arguments,
			LeafType = method.ReturnType,
			OneWay = spec.OneWay,
			ToVisibility = toVisibility,
		};
	}

	// Whether a value of `source` reaches the property: as it is, or a bool shown as a Visibility, as WinUI's x:Bind
	// converts it (true Visible, false Collapsed).
	private static bool ForwardConversion(Gen gen, XAttribute attr, BindTarget prop, ITypeSymbol source, out bool toVisibility)
	{
		toVisibility = false;
		if (gen.Compilation.ClassifyConversion(source, prop.Type).IsImplicit)
			return true;
		if (prop.Type.ToDisplayString() == "Vysara.Visibility" && IsBool(source))
		{
			toVisibility = true;
			return true;
		}
		gen.Report(BindTypeMismatch, attr, source.ToDisplayString(), prop.Name, prop.Type.ToDisplayString());
		return false;
	}

	private static bool IsBool(ITypeSymbol type) =>
		type.SpecialType == SpecialType.System_Boolean || (NullableUnderlying(type) is { SpecialType: SpecialType.System_Boolean });

	private static ITypeSymbol? NullableUnderlying(ITypeSymbol type) =>
		type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : null;

	// How a TwoWay write-back turns the control's value (`{0}`) into the source's type, or null where it can't: as it is
	// where that converts implicitly, a nullable control value (a ToggleButton's bool?) taken as its value or the default,
	// and a number cast to the source's numeric type (a Slider's double into a float).
	private static string? WriteBackFormat(Gen gen, ITypeSymbol control, ITypeSymbol source)
	{
		var compilation = gen.Compilation;
		if (compilation.ClassifyConversion(control, source).IsImplicit)
			return "{0}";
		if (NullableUnderlying(control) is { } underlying)
		{
			var fromValue = compilation.ClassifyConversion(underlying, source);
			if (fromValue.IsImplicit)
				return "({0}).GetValueOrDefault()";
			if (fromValue is { IsExplicit: true, IsNumeric: true })
				return $"({Fq(source)})({{0}}).GetValueOrDefault()";
		}
		return compilation.ClassifyConversion(control, source) is { IsExplicit: true, IsNumeric: true } ? $"({Fq(source)})({{0}})" : null;
	}

	// Whether `owner` has a settable property or a writable field named `name`, which a TwoWay write-back assigns.
	private static bool IsWritable(ITypeSymbol? owner, string name)
	{
		for (var t = owner as INamedTypeSymbol; t is not null; t = t.BaseType)
		{
			foreach (var member in t.GetMembers(name))
			{
				switch (member)
				{
					case IPropertySymbol property:
						return property.SetMethod is { IsInitOnly: false };
					case IFieldSymbol field:
						return !field.IsReadOnly && !field.IsConst;
				}
			}
		}
		return false;
	}

	// --- The code that pushes a binding's value ---

	// The statement that pushes the binding's value, read from `root`, into `target`'s property: an expression statement,
	// or a block where it needs locals of its own, so statements side by side never share one.
	private static string AssignStatement(Bind b, string root, string target)
	{
		if (b.Function is { } function)
			return FunctionStatement(b, function, root, target);

		var read = PathRead(root, b.Path!, b.Path!.Length);
		if (!b.ToVisibility)
			return Set(b, target, CoalesceRead(read, b.LeafType, b.FallbackValue, b.TargetNullValue));
		if (!read.Contains("?.") && NullableUnderlying(b.LeafType) is null)
			return Set(b, target, $"({read}) ? {Visible} : {Collapsed}");
		var whenNull = b.TargetNullValue ?? b.FallbackValue;
		return whenNull is null
			? Set(b, target, $"({read}) == true ? {Visible} : {Collapsed}")
			: $"{{ var __v = {read}; {Set(b, target, $"__v is null ? ({whenNull}) : (__v == true ? {Visible} : {Collapsed})")} }}";
	}

	// The statement that gives `target` the binding's value: its property assigned, or an attached property's owner's
	// Set<Prop>(target, value) called.
	private static string Set(Bind b, string target, string value) =>
		b.TemplateDp is { } dp ? $"global::Vysara.Generated.TemplateValues.Set({target}, {dp}, {(b.TemplateCast is { } cast ? $"({cast})({value})" : value)});"
		: b.AttachedSetter is { } setter ? $"{setter}({target}, {value});"
		: $"{target}.{b.TargetProp} = {value};";

	// A ControlTemplate's {x:Bind} gives its part a value as the template's other values do: on the part's Template rung,
	// through the dependency property behind the property (an attached one's owner's), converted where its type differs
	// implicitly. A plain CLR property with no dependency property has no levels and is set through its setter; any other
	// value that can't reach the Template rung is set as the part's own and reported (VYSARA027), as a plain value is.
	private static void ResolveTemplateRung(Gen gen, XAttribute attr, INamedTypeSymbol targetType, BindTarget prop, Bind bind)
	{
		// An attached property's name is Owner.Prop in the markup; its dependency property is named after its setter's Set<Prop>
		var (owner, name) = prop.AttachedSetter is not null && prop.Member is IMethodSymbol setter
			? (setter.ContainingType, setter.Name.Substring("Set".Length))
			: (targetType, prop.Name);
		var found = FindDependencyProperty(owner, name);
		if (found is null && prop.AttachedSetter is null)
			return;
		if (found is { } dp && DependencyValueType(dp) is { } valueType)
		{
			if (SymbolEqualityComparer.Default.Equals(valueType, prop.Type))
			{
				bind.TemplateDp = $"{Fq(dp.ContainingType!)}.{dp.Name}";
				return;
			}
			if (prop.AttachedSetter is null && gen.Compilation.ClassifyConversion(prop.Type, valueType).IsImplicit)
			{
				bind.TemplateDp = $"{Fq(dp.ContainingType!)}.{dp.Name}";
				bind.TemplateCast = Fq(valueType);
				return;
			}
		}
		gen.Report(TemplateValueOnLocal, attr, $"{owner.Name}.{name}",
			found is null ? $"{owner.Name} declares no dependency property {name}Property" : $"its value's type, {prop.Type.ToDisplayString()}, does not convert implicitly to its dependency property's");
	}

	// A function binding's push: each argument read from `root`; where an argument's path passes through an object that
	// may be null, the call waits for it, and the target takes the FallbackValue meanwhile (or keeps what it shows), as
	// WinUI's x:Bind does.
	private static string FunctionStatement(Bind b, IMethodSymbol function, string root, string target)
	{
		var checks = new List<string>();
		var args = new List<string>();
		for (var k = 0; k < b.Arguments.Length; k++)
		{
			var arg = b.Arguments[k];
			if (arg.Length == 1)
			{
				args.Add($"{root}.{arg.Segments[0]}");
				continue;
			}
			var owner = PathRead(root, arg, arg.Length - 1);
			if (owner.Contains("?.") || arg.ResultTypes[arg.Length - 2].IsReferenceType)
			{
				checks.Add($"{owner} is {{ }} __a{k}");
				args.Add($"__a{k}.{arg.Segments[arg.Length - 1]}");
			}
			else
				args.Add($"{owner}.{arg.Segments[arg.Length - 1]}");
		}
		var call = $"{Fq(function.ContainingType)}.{function.Name}({string.Join(", ", args)})";
		var returnsNullable = function.ReturnType.IsReferenceType || NullableUnderlying(function.ReturnType) is not null;
		var value = b.ToVisibility
			? NullableUnderlying(function.ReturnType) is null ? $"({call}) ? {Visible} : {Collapsed}" : $"({call}) == true ? {Visible} : {Collapsed}"
			: CoalesceRead(call, function.ReturnType, null, returnsNullable ? b.TargetNullValue : null);
		var assign = Set(b, target, value);
		if (checks.Count == 0)
			return assign;
		var otherwise = b.FallbackValue is null ? "" : $" else {Set(b, target, b.FallbackValue)}";
		return $"{{ if ({string.Join(" && ", checks)}) {assign}{otherwise} }}";
	}

	// The first `count` segments of `path` read from `root`, each reference-typed member read null-conditionally so a null
	// anywhere up the chain yields null rather than throwing.
	private static string PathRead(string root, BindPath path, int count)
	{
		var sb = new StringBuilder(root);
		for (var i = 0; i < count; i++)
			sb.Append(i == 0 || !path.ResultTypes[i - 1].IsReferenceType ? "." : "?.").Append(path.Segments[i]);
		return sb.ToString();
	}

	// The control→source assignment for a TwoWay reverse wire: the control's value converted for the source, assigned to
	// the path's leaf. Where the leaf's owner is reference-typed the chain to it is read null-conditionally and the write
	// guarded (a null intermediate is no write rather than an NRE); a value-typed owner is assigned in place, since writing
	// a copy would not reach the source.
	private static string WriteBack(Bind b, string controlValue)
	{
		var path = b.Path!;
		var rhs = b.WriteBack.Replace("{0}", controlValue);
		var leaf = path.Segments[path.Length - 1];
		if (path.Length > 1 && path.ResultTypes[path.Length - 2].IsReferenceType)
			return $"if ({PathRead("_root", path, path.Length - 1)} is {{ }} __o) __o.{leaf} = {rhs};";
		var sb = new StringBuilder("_root");
		foreach (var segment in path.Segments)
			sb.Append('.').Append(segment);
		return $"{sb} = {rhs};";
	}

	// --- The binding helpers: the page's _Bindings, and a DataTemplate's, one per realized element ---

	private static GeneratedSource EmitBindings(string fullClass, string? ns, string cls, List<Bind> binds)
	{
		var sb = new StringBuilder();
		sb.AppendLine("// <auto-generated/> by Vysara.SourceGen");
		sb.AppendLine("#nullable enable");
		if (ns is not null)
			sb.AppendLine($"namespace {ns};");
		sb.AppendLine();
		// The helper is nested in the view's class, so it reaches a named element's field whatever x:FieldModifier gave it.
		sb.AppendLine($"partial class {cls}");
		sb.AppendLine("{");
		sb.Append(BindingsClass($"{cls}_Bindings", cls, binds, template: false, "\t"));
		sb.AppendLine("}");
		return new GeneratedSource(MakeHintName(fullClass, ".Bindings"), sb.ToString());
	}

	// Where a binding's target is reached from inside its helper: the page's field (or the page itself, for a binding on an
	// unsealed root), or the template helper's own field for the element.
	private static string HelperTarget(Bind b, bool template) =>
		template ? $"_e_{b.TargetVar}" : b.TargetVar == "this" ? "_root" : $"_root.{b.TargetVar}";

	// A binding helper class, at `indent`: it pushes each binding's value (Update), wires each TwoWay binding's control back
	// to its source, and tracks INotifyPropertyChanged along every path, so a change to a leaf updates the bindings reading
	// it and a change to an intermediate rewires and refreshes them all. The page's subscribes as it is initialized; a
	// template's (`template`) only while its element is in a live tree (IDataTemplateBindings.Attach / Detach).
	private static string BindingsClass(string className, string rootType, List<Bind> binds, bool template, string indent)
	{
		var sb = new StringBuilder();
		void Line(string text = "") => sb.AppendLine(text.Length == 0 ? "" : indent + text);

		Line(template
			? $"private sealed class {className} : global::Vysara.Controls.IDataTemplateBindings"
			: $"private sealed class {className}");
		Line("{");
		Line($"\tprivate readonly {rootType} _root;");
		Line("\tprivate readonly global::System.Collections.Generic.List<global::System.ComponentModel.INotifyPropertyChanged> _tracked = new();");
		var targets = binds.GroupBy(static b => b.TargetVar).Select(static g => g.First()).ToList();
		// A template's elements are handed over by an object initializer, not the constructor, so a binding added to the
		// template under hot reload adds a field rather than changing a signature, which the runtime can't apply live.
		if (template)
		{
			foreach (var t in targets)
				Line($"\tinternal {Fq(t.TargetElementType)} _e_{t.TargetVar} = null!;");
		}
		Line($"\tpublic {className}({rootType} root) => _root = root;");
		Line();

		// Reentrancy guards for TwoWay bindings, one per binding.
		for (var i = 0; i < binds.Count; i++)
		{
			if (binds[i].TwoWay)
				Line($"\tprivate bool _updating_{i};");
		}
		if (binds.Any(static b => b.TwoWay))
			Line();

		// Initialize: wire each TwoWay control back to its source, then push every value (and, on the page, subscribe).
		Line("\tpublic void Initialize()");
		Line("\t{");
		for (var i = 0; i < binds.Count; i++)
		{
			if (binds[i].TwoWay)
				Line($"\t\t{HelperTarget(binds[i], template)}.{binds[i].TargetProp}Changed += On_{i}_ControlChanged;");
		}
		if (!template)
			Line("\t\tSubscribe();");
		Line("\t\tUpdate();");
		Line("\t}");
		Line();
		if (template)
		{
			Line("\tpublic void Attach()");
			Line("\t{");
			Line("\t\tSubscribe();");
			Line("\t\tUpdate();");
			Line("\t}");
			Line();
			Line("\tpublic void Detach() => StopTracking();");
			Line();
		}

		// Update_N pushes one value; a TwoWay one inside its guard, so the control's change event doesn't write it back.
		for (var i = 0; i < binds.Count; i++)
		{
			var assign = AssignStatement(binds[i], "_root", HelperTarget(binds[i], template));
			if (!binds[i].TwoWay)
			{
				Line($"\tprivate void Update_{i}() {{ {assign} }}");
				continue;
			}
			Line($"\tprivate void Update_{i}()");
			Line("\t{");
			Line($"\t\tif (_updating_{i}) return;");
			Line($"\t\t_updating_{i} = true;");
			Line($"\t\ttry {{ {assign} }}");
			Line($"\t\tfinally {{ _updating_{i} = false; }}");
			Line("\t}");
		}
		Line("\tpublic void Update()");
		Line("\t{");
		for (var i = 0; i < binds.Count; i++)
			Line($"\t\tUpdate_{i}();");
		Line("\t}");
		Line();

		// The reverse wire: the control's value into the source; then the source read again and shown, so a source that
		// refuses or coerces the value (a choice that can't be unselected, a clamp) is shown as it is. The source's own
		// change notice, raised while the guard was up, was not heard.
		for (var i = 0; i < binds.Count; i++)
		{
			var b = binds[i];
			if (!b.TwoWay)
				continue;
			Line($"\tprivate void On_{i}_ControlChanged(object? sender, global::System.EventArgs e)");
			Line("\t{");
			Line($"\t\tif (_updating_{i}) return;");
			Line($"\t\t_updating_{i} = true;");
			Line($"\t\ttry {{ {WriteBack(b, $"{HelperTarget(b, template)}.{b.TargetProp}")} }}");
			Line($"\t\tfinally {{ _updating_{i} = false; }}");
			Line($"\t\tUpdate_{i}();");
			Line("\t}");
			Line();
		}

		// Subscribe to each distinct INPC source: the root, plus every object up each path (the owner of each segment
		// after the first). Each owner is read null-conditionally through its reference-typed parents, so a null
		// intermediate is a null source that Track() skips rather than throwing.
		var trackExprs = new List<string> { "_root" };
		foreach (var b in binds)
		{
			foreach (var path in b.Tracked)
			{
				for (var depth = 1; depth < path.Length; depth++)
				{
					var owner = PathRead("_root", path, depth);
					if (!trackExprs.Contains(owner))
						trackExprs.Add(owner);
				}
			}
		}
		Line("\tpublic void Subscribe()");
		Line("\t{");
		Line("\t\tStopTracking();");
		foreach (var expr in trackExprs)
			Line($"\t\tTrack({expr});");
		Line("\t}");
		Line();
		Line("\tpublic void StopTracking()");
		Line("\t{");
		Line("\t\tforeach (var t in _tracked) t.PropertyChanged -= OnPropertyChanged;");
		Line("\t\t_tracked.Clear();");
		Line("\t}");
		Line();
		Line("\tprivate void Track(object? o)");
		Line("\t{");
		Line("\t\tif (o is global::System.ComponentModel.INotifyPropertyChanged inpc) { inpc.PropertyChanged += OnPropertyChanged; _tracked.Add(inpc); }");
		Line("\t}");
		Line();

		// A change to a segment before any path's leaf swaps an object the paths read through, so it rewires (Subscribe)
		// and refreshes everything; a change to a leaf updates the bindings that read it.
		var intermediateNames = new HashSet<string>();
		foreach (var b in binds)
		{
			foreach (var path in b.Tracked)
			{
				for (var i = 0; i < path.Length - 1; i++)
					intermediateNames.Add(path.Segments[i]);
			}
		}
		var leafUpdates = new Dictionary<string, List<int>>();
		for (var i = 0; i < binds.Count; i++)
		{
			foreach (var path in binds[i].Tracked)
			{
				if (path.Length == 0)
					continue;
				var leaf = path.Segments[path.Length - 1];
				if (intermediateNames.Contains(leaf))
					continue;
				if (!leafUpdates.TryGetValue(leaf, out var list))
					leafUpdates[leaf] = list = [];
				if (!list.Contains(i))
					list.Add(i);
			}
		}
		Line("\tprivate void OnPropertyChanged(object? sender, global::System.ComponentModel.PropertyChangedEventArgs e)");
		Line("\t{");
		Line("\t\tswitch (e.PropertyName)");
		Line("\t\t{");
		foreach (var name in intermediateNames)
			Line($"\t\t\tcase \"{name}\": Subscribe(); Update(); break;");
		foreach (var kv in leafUpdates)
			Line($"\t\t\tcase \"{kv.Key}\": {string.Join(" ", kv.Value.Select(static i => $"Update_{i}();"))} break;");
		Line("\t\t\tcase null: case \"\": Subscribe(); Update(); break;");
		Line("\t\t}");
		Line("\t}");
		Line("}");
		return sb.ToString();
	}
}
