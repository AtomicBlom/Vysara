using System.Globalization;
using System.Text;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SUIsei.SourceGen;

partial class XamlSourceGenerator
{
	// --- Property elements (<Owner.Prop> children): styles, templates, storyboards, visual states ---

	private static void HandlePropertyElement(Gen gen, Scope scope, string ownerVar, INamedTypeSymbol ownerType, XElement propEl)
	{
		var localName = propEl.Name.LocalName; // "Owner.Prop"
		var dot = localName.IndexOf('.');
		var owner = localName.Substring(0, dot);
		var prop = localName.Substring(dot + 1);

		// The forms with a grammar of their own.
		switch (prop)
		{
			case "VisualStateGroups" when owner == "VisualStateManager":
				EmitVisualStateGroups(gen, scope, ownerVar, propEl);
				return;
			case "Resources":
				EmitResources(gen, scope, ownerVar, propEl);
				return;
			case "Template" when FindProperty(ownerType, "Template") is not null:
				EmitControlTemplateProperty(gen, scope, ownerVar, ownerType, propEl);
				return;
			case "ItemTemplate" when FindProperty(ownerType, "ItemTemplate") is not null:
				EmitDataTemplateProperty(gen, scope, ownerVar, "ItemTemplate", propEl);
				return;
			case "ContentTemplate" when FindProperty(ownerType, "ContentTemplate") is not null:
				EmitDataTemplateProperty(gen, scope, ownerVar, "ContentTemplate", propEl);
				return;
			case "ItemsPanel" when FindProperty(ownerType, "ItemsPanel") is not null:
				EmitItemsPanelProperty(gen, scope, ownerVar, propEl);
				return;
			case "ColumnDefinitions" when owner == "Grid":
				EmitGridDefinitions(gen, scope, ownerVar, propEl, "ColumnDefinitions");
				return;
			case "RowDefinitions" when owner == "Grid":
				EmitGridDefinitions(gen, scope, ownerVar, propEl, "RowDefinitions");
				return;
		}

		// A property of this element, where Owner (in the property element's XML namespace) is its type or a base:
		// <Border.Background>, <StackPanel.Children>, <suisei:TestBadge.Content>.
		var ns = propEl.Name.Namespace;
		var ownsMember = ResolveType(gen.Compilation, ns, owner) is { } named && IsAssignableTo(ownerType, named);
		if (ownsMember && FindAnyProperty(ownerType, prop) is { } member)
		{
			EmitMemberPropertyElement(gen, scope, ownerVar, member, propEl);
			return;
		}

		// Otherwise an attached property of the Owner type: <Grid.Row>, <suisei:Owner.Prop>.
		if (FindAttachedSetter(gen, ns, owner, prop, out var attachedOwner) is { } setter)
		{
			if (AttachesTo(setter, ownerType))
				EmitAttachedPropertyElement(gen, scope, ownerVar, attachedOwner!, setter, propEl);
			else
				gen.Report(UnknownProperty, propEl, ownerType.ToDisplayString(), propEl.Name.LocalName);
			return;
		}

		if (ownsMember)
			gen.Report(UnknownProperty, propEl, ownerType.ToDisplayString(), prop);
		else if (ns == ExtensionNs)
			gen.Report(UnknownExtensionMember, propEl, localName);
		else if (attachedOwner is null)
			gen.Report(UnknownElement, propEl, owner);
		else
			gen.Report(UnknownProperty, propEl, attachedOwner.ToDisplayString(), prop);
	}

	// Set a property the markup gives a value. Inside a ControlTemplate a dependency property takes it on its Template rung,
	// as WinUI puts every value a template sets on its parts at the templated-parent level: below Local, so a value the app
	// sets on a part wins, and ClearValue on the part reverts to the template's value. A CLR property whose type differs from
	// its dependency property's takes the value converted, where the conversion is implicit, and is reported (SUISEI027)
	// where it is not, since the value then goes through the CLR setter and is the part's own. Elsewhere, and for a CLR
	// property with no dependency property, which has no levels, the value goes through the property's setter.
	private static void EmitSet(Gen gen, Scope scope, XObject at, INamedTypeSymbol ownerType, string ownerVar, IPropertySymbol prop, string code)
	{
		if (scope.IsControlTemplate && FindDependencyProperty(ownerType, prop.Name) is { } dp && DependencyValueType(dp) is { } valueType)
		{
			if (SymbolEqualityComparer.Default.Equals(valueType, prop.Type))
			{
				scope.Emit($"global::SUIsei.Generated.TemplateValues.Set({ownerVar}, {Fq(dp.ContainingType!)}.{dp.Name}, {code});");
				return;
			}
			if (gen.Compilation.ClassifyConversion(prop.Type, valueType).IsImplicit)
			{
				scope.Emit($"global::SUIsei.Generated.TemplateValues.Set({ownerVar}, {Fq(dp.ContainingType!)}.{dp.Name}, ({Fq(valueType)})({code}));");
				return;
			}
			gen.Report(TemplateValueOnLocal, at, $"{ownerType.Name}.{prop.Name}",
				$"its type, {prop.Type.ToDisplayString()}, does not convert implicitly to its dependency property's, {valueType.ToDisplayString()}");
		}
		scope.Emit($"{ownerVar}.{prop.Name} = {code};");
	}

	// Set an attached property through its owner's static setter. Inside a ControlTemplate, where the owner declares the
	// dependency property the setter sets (<Prop>Property, of the setter's value type), it goes on the part's Template rung
	// instead, as every other value a template gives its parts; one with no such dependency property is reported
	// (SUISEI027), since the setter makes it the part's own.
	private static void EmitAttachedSet(Gen gen, Scope scope, XObject at, INamedTypeSymbol owner, IMethodSymbol setter, string targetVar, string code)
	{
		if (scope.IsControlTemplate)
		{
			var property = setter.Name.Substring("Set".Length);
			if (FindDependencyProperty(owner, property) is { } dp && DependencyValueType(dp) is { } valueType
				&& SymbolEqualityComparer.Default.Equals(valueType, setter.Parameters[1].Type))
			{
				scope.Emit($"global::SUIsei.Generated.TemplateValues.Set({targetVar}, {Fq(dp.ContainingType!)}.{dp.Name}, {code});");
				return;
			}
			gen.Report(TemplateValueOnLocal, at, $"{owner.Name}.{property}",
				$"{owner.Name} declares no dependency property {property}Property of the setter's value type");
		}
		scope.Emit($"{Fq(owner)}.{setter.Name}({targetVar}, {code});");
	}

	// <Owner.Prop> for a property of the element: text is converted for the property's type, one object is assigned, and
	// for a collection (a type with a one-argument Add) each object is added — the only form a get-only collection takes.
	private static void EmitMemberPropertyElement(Gen gen, Scope scope, string ownerVar, IPropertySymbol member, XElement propEl)
	{
		var children = ObjectChildren(propEl);
		var text = TextContent(propEl);
		if (children.Count == 0)
		{
			if (text is null || member.SetMethod is null)
				gen.Report(BadPropertyElement, propEl, $"{propEl.Name.LocalName} (needs a value, or objects for a collection)");
			else if (ConvertOrReport(gen, propEl, member.Type, text, member.Name, member) is { } code)
				EmitSet(gen, scope, propEl, member.ContainingType, ownerVar, member, code);
			return;
		}
		if (text is not null)
		{
			gen.Report(BadPropertyElement, propEl, $"{propEl.Name.LocalName} (mixes text with objects)");
			return;
		}

		if (children.Count == 1 && member.SetMethod is not null && ChildFits(gen, children[0], member.Type))
		{
			if (EmitElement(gen, scope, children[0]) is { } childVar)
				EmitSet(gen, scope, propEl, member.ContainingType, ownerVar, member, childVar);
			return;
		}
		if (!IsAddable(member.Type))
		{
			gen.Report(BadPropertyElement, propEl, $"{propEl.Name.LocalName} (takes one {member.Type.Name})");
			return;
		}
		foreach (var child in children)
		{
			if (EmitElement(gen, scope, child) is { } childVar)
				scope.Emit($"{ownerVar}.{member.Name}.Add({childVar});");
		}
	}

	// <Owner.Prop> for an attached property: text converted for the setter's value type, or one object.
	private static void EmitAttachedPropertyElement(Gen gen, Scope scope, string ownerVar, INamedTypeSymbol owner, IMethodSymbol setter, XElement propEl)
	{
		var children = ObjectChildren(propEl);
		var text = TextContent(propEl);
		string? code = null;
		if (children.Count == 0 && text is not null)
			code = ConvertOrReport(gen, propEl, setter.Parameters[1].Type, text, propEl.Name.LocalName);
		else if (children.Count == 1 && text is null)
			code = EmitElement(gen, scope, children[0]);
		else
			gen.Report(BadPropertyElement, propEl, $"{propEl.Name.LocalName} (takes one value)");
		if (code is not null)
			EmitAttachedSet(gen, scope, propEl, owner, setter, ownerVar, code);
	}

	// A property element's object children, skipping design-time and other ignorable markup.
	private static List<XElement> ObjectChildren(XElement propEl) =>
		propEl.Elements().Where(e => !IsIgnorable(e, e.Name.Namespace)).ToList();

	// A property element's text, trimmed; null when it holds none.
	private static string? TextContent(XElement propEl)
	{
		var text = string.Concat(propEl.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
		return text.Length > 0 ? text : null;
	}

	// Whether an object element would be a value of `type` (so it is assigned rather than added to a collection).
	private static bool ChildFits(Gen gen, XElement child, ITypeSymbol type)
	{
		var ns = child.Name.Namespace == XNamespace.None ? Ui : child.Name.Namespace;
		return ResolveType(gen.Compilation, ns, child.Name.LocalName) is { } childType
			&& gen.Compilation.ClassifyConversion(childType, type).IsImplicit;
	}

	private static void EmitVisualStateGroups(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var groupsVar = NewName(gen, "__vsg");
		scope.Emit($"var {groupsVar} = new global::System.Collections.Generic.List<global::SUIsei.Animation.VisualStateGroup>();");
		foreach (var groupEl in propEl.Elements())
		{
			if (groupEl.Name.LocalName != "VisualStateGroup") { gen.Report(BadPropertyElement, groupEl, groupEl.Name.LocalName); continue; }
			var groupVar = NewName(gen, "__vsgrp");
			var groupName = StateName(gen, groupEl);
			scope.Emit($"var {groupVar} = new global::SUIsei.Animation.VisualStateGroup {{ Name = {Quote(groupName)} }};");
			foreach (var stateEl in groupEl.Elements())
			{
				if (IsIgnorable(stateEl, stateEl.Name.Namespace))
					continue;
				if (stateEl.Name.LocalName != "VisualState")
				{
					gen.Report(OutsideSubset, stateEl, $"<{stateEl.Name.LocalName}> in a VisualStateGroup", stateEl.Name.LocalName == "VisualStateGroup.Transitions"
						? " (states change at once, or by their own storyboards' timing)" : " (a group holds VisualStates)");
					continue;
				}
				var stateName = StateName(gen, stateEl);
				// A state holds one Storyboard; WinUI's setters and triggers are reported rather than dropped.
				XElement? sbEl = null;
				foreach (var child in stateEl.Elements())
				{
					if (IsIgnorable(child, child.Name.Namespace))
						continue;
					if (child.Name.LocalName is "Storyboard" or "VisualState.Storyboard" && sbEl is null)
						sbEl = child.Name.LocalName == "Storyboard" ? child : child.Elements().FirstOrDefault();
					else
						gen.Report(OutsideSubset, child, $"<{child.Name.LocalName}> in a VisualState", child.Name.LocalName switch
						{
							"VisualState.Setters" => " (write the values as animations in the state's Storyboard)",
							"VisualState.StateTriggers" => " (go to the state from code with VisualStateManager.GoToState)",
							_ => " (a state holds one Storyboard)",
						});
				}
				var sbExpr = sbEl is null ? "null" : EmitStoryboard(gen, scope, ownerVar, sbEl);
				scope.Emit($"{groupVar}.States.Add(new global::SUIsei.Animation.VisualState {{ Name = {Quote(stateName)}, Storyboard = {sbExpr} }});");
			}
			scope.Emit($"{groupsVar}.Add({groupVar});");
		}
		scope.Emit($"global::SUIsei.Animation.VisualStateManager.SetVisualStateGroups({ownerVar}, {groupsVar});");
	}

	// A visual state's or group's name: x:Name, as WinUI markup writes it. A bare Name is reported: WinUI's is read-only,
	// set by x:Name alone. Every other attribute is reported too, since a state or group takes none.
	private static string StateName(Gen gen, XElement el)
	{
		foreach (var attr in el.Attributes())
		{
			if (attr.IsNamespaceDeclaration || IsIgnorable(el, attr.Name.Namespace) || IsDirective(gen, el, attr))
				continue;
			gen.Report(OutsideSubset, attr, $"'{attr.Name.LocalName}' on a {el.Name.LocalName}",
				attr.Name.LocalName == "Name" ? " (WinUI names a state or group with x:Name)" : "");
		}
		return el.Attribute(X + "Name")?.Value ?? "";
	}

	// --- Templates: ControlTemplate / DataTemplate / ItemsPanelTemplate factories ---

	private static void EmitControlTemplateProperty(Gen gen, Scope scope, string ownerVar, INamedTypeSymbol ownerType, XElement propEl)
	{
		var templateEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
		if (templateEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }
		if (ControlTemplateValue(gen, templateEl, ownerType) is { } template)
			scope.Emit($"{ownerVar}.Template = {template};");
	}

	// A ControlTemplate as a value: a template object whose static factory builds the tree for each control it is applied
	// to, so one template is shared by every control that uses it, set directly, kept as a resource or set by a style.
	// TargetType (optional) types the data root so non-Content {TemplateBinding}s and storyboard targets resolve; without
	// one it is `targetType`, the control's type where that is known.
	private static string? ControlTemplateValue(Gen gen, XElement templateEl, INamedTypeSymbol targetType)
	{
		var dataRootType = ResolveTypeName(gen, templateEl, templateEl.Attribute("TargetType")?.Value) ?? targetType;
		var method = EmitTemplateFactory(gen, templateEl, isControlTemplate: true,
			paramDecl: "global::SUIsei.Controls.Control __tc", castLocal: null, dataRootExpr: "__tc", dataRootType: dataRootType);
		return method is null ? null : $"new global::SUIsei.Controls.ControlTemplate {{ Factory = {method} }}";
	}

	private static void EmitDataTemplateProperty(Gen gen, Scope scope, string ownerVar, string targetProp, XElement propEl)
	{
		var templateEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "DataTemplate");
		if (templateEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }

		// x:DataType types the item so the template's {x:Bind}s resolve; without it, an {x:Bind} inside diagnoses.
		var dataType = ResolveTypeName(gen, templateEl, templateEl.Attribute(X + "DataType")?.Value);
		var castLocal = dataType is null ? null : $"var __data = ({Fq(dataType)})__item!;";
		var method = EmitTemplateFactory(gen, templateEl, isControlTemplate: false,
			paramDecl: "object? __item", castLocal: castLocal, dataRootExpr: "__data", dataRootType: dataType);
		if (method is null)
			return;
		scope.Emit($"{ownerVar}.{targetProp} = new global::SUIsei.Controls.DataTemplate {{ Factory = {method} }};");
	}

	// <X.ItemsPanel><ItemsPanelTemplate><SomePanel …/></ItemsPanelTemplate></X.ItemsPanel> → a static factory that builds
	// the panel with its attributes, as any template root is built.
	private static void EmitItemsPanelProperty(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var tmplEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ItemsPanelTemplate");
		var panelEl = tmplEl?.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
		if (panelEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }
		var panelType = ResolveType(gen.Compilation, panelEl.Name.Namespace, panelEl.Name.LocalName);
		if (panelType is null) { gen.Report(UnknownElement, panelEl, panelEl.Name.LocalName); return; }
		if (gen.Compilation.GetTypeByMetadataName("SUIsei.Controls.Panel") is not { } panel || !IsAssignableTo(panelType, panel))
		{
			gen.Report(BadPropertyElement, panelEl, $"ItemsPanelTemplate (its root must be a Panel; '{panelType.Name}' is not)");
			return;
		}
		var method = EmitTemplateFactory(gen, tmplEl!, isControlTemplate: false,
			paramDecl: "", castLocal: null, dataRootExpr: "", dataRootType: null, returnType: "global::SUIsei.Controls.Panel");
		if (method is not null)
			scope.Emit($"{ownerVar}.ItemsPanel = new global::SUIsei.Controls.ItemsPanelTemplate {{ Factory = {method} }};");
	}

	// Emit a static factory method that news the template subtree and returns its root as `returnType`, then return its
	// name.
	private static string? EmitTemplateFactory(Gen gen, XElement templateEl, bool isControlTemplate, string paramDecl, string? castLocal, string dataRootExpr, INamedTypeSymbol? dataRootType,
		string returnType = "global::SUIsei.FrameworkElement")
	{
		var roots = templateEl.Elements().Where(e => !e.Name.LocalName.Contains('.') && !IsIgnorable(e, e.Name.Namespace)).ToList();
		var rootChild = roots.FirstOrDefault();
		if (rootChild is null) { gen.Report(BadPropertyElement, templateEl, templateEl.Name.LocalName); return null; }
		for (var i = 1; i < roots.Count; i++)
			gen.Report(OutsideSubset, roots[i], $"A second root, {roots[i].Name.LocalName}, in a {templateEl.Name.LocalName}", " (a template builds one element; wrap them in a panel)");

		var method = NewName(gen, "__Template");
		var body = new StringBuilder();
		if (castLocal is not null)
			body.AppendLine("\t\t" + castLocal);
		var scope = new Scope
		{
			Body = body,
			Names = [],
			IsTemplate = true,
			IsControlTemplate = isControlTemplate,
			DataRootExpr = dataRootExpr,
			DataRootType = dataRootType,
			// The templated control is parented when its template is applied, so resources above it are reachable.
			ResourceOwners = isControlTemplate ? [dataRootExpr] : [],
		};
		var rootVar = EmitElement(gen, scope, rootChild);
		if (rootVar is null)
			return null;

		gen.Methods.AppendLine($"\tprivate static {returnType} {method}({paramDecl})");
		gen.Methods.AppendLine("\t{");
		gen.Methods.Append(body);
		gen.Methods.Append(SourceRecordBlock(scope));
		gen.Methods.AppendLine($"\t\treturn {rootVar};");
		gen.Methods.AppendLine("\t}");
		return method;
	}

	// A template-scoped {x:Bind} — a OneTime read against the template's data root (the item is fixed for the
	// realized container's lifetime, so no INPC tracking is wired).
	private static void EmitTemplateBind(Gen gen, Scope scope, XAttribute attr, string targetVar, IPropertySymbol prop, string[] path, string? fallback, string? targetNull)
	{
		if (scope.DataRootType is null)
		{
			gen.Report(BindPathUnresolved, attr, string.Join(".", path), "the template data type (set x:DataType)");
			return;
		}
		if (!TryResolvePath(scope.DataRootType, path, out var resultTypes, out var leafType))
		{
			gen.Report(BindPathUnresolved, attr, string.Join(".", path), scope.DataRootType.ToDisplayString());
			return;
		}
		if (!gen.Compilation.ClassifyConversion(leafType, prop.Type).IsImplicit)
		{
			gen.Report(BindTypeMismatch, attr, leafType.ToDisplayString(), prop.Name, prop.Type.ToDisplayString());
			return;
		}

		var sb = new StringBuilder(scope.DataRootExpr);
		for (var i = 0; i < path.Length; i++)
			sb.Append(i == 0 ? "." : (resultTypes[i - 1].IsReferenceType ? "?." : ".")).Append(path[i]);
		scope.Emit($"{targetVar}.{prop.Name} = {CoalesceRead(sb.ToString(), leafType, fallback, targetNull)};");
	}

	// A {TemplateBinding source} wire: the templated control's property flows OneWay into a template part's property.
	private static void EmitTemplateBinding(Gen gen, Scope scope, XAttribute attr, string targetVar, INamedTypeSymbol targetType, IPropertySymbol prop, string sourceProp)
	{
		// Content is a CLR property, not a dependency property: its template binding is the control's own, dropped with the
		// template as the others are.
		if (sourceProp == "Content")
		{
			var host = $"((global::SUIsei.Controls.ContentControl){scope.DataRootExpr})";
			scope.Emit($"{host}.RegisterContentTemplateBinding(__v => {targetVar}.{prop.Name} = __v);");
			return;
		}
		// Otherwise the source must be a typed dependency property on the templated control; the push is typed too, so a
		// value-type property reaches the child unboxed.
		if (scope.DataRootType is null || FindDependencyProperty(scope.DataRootType, sourceProp) is not { } dp || DependencyValueType(dp) is not { } sourceType)
		{
			gen.Report(BadPropertyPath, attr, sourceProp,
				$"{scope.DataRootType?.Name ?? "the templated control"} has no dependency property '{sourceProp}Property'");
			return;
		}
		var source = $"{Fq(dp.ContainingType!)}.{dp.Name}";

		// A dependency-property target takes the value on its Template rung, below Local, so a value the app sets on the
		// part wins over the template and the template's next push does not overwrite it.
		if (FindDependencyProperty(targetType, prop.Name) is { } targetDp && DependencyValueType(targetDp) is { } targetValueType)
		{
			var target = $"{Fq(targetDp.ContainingType!)}.{targetDp.Name}";
			scope.Emit(SymbolEqualityComparer.IncludeNullability.Equals(sourceType, targetValueType)
				? $"{scope.DataRootExpr}.RegisterTemplateBinding({source}, {targetVar}, {target});"
				: $"{scope.DataRootExpr}.RegisterTemplateBinding({source}, {targetVar}, {target}, static __v => ({NullableCast(targetValueType)})__v);");
			return;
		}

		// A CLR property has no precedence levels, so the push writes it through its setter.
		scope.Emit($"{scope.DataRootExpr}.RegisterTemplateBinding({source}, __v => {targetVar}.{prop.Name} = ({NullableCast(prop.Type)})__v);");

		// The cast keeps a nullable reference target nullable (a Brush? background), so pushing null is no warning.
		static string NullableCast(ITypeSymbol type) =>
			type is { IsReferenceType: true, NullableAnnotation: NullableAnnotation.Annotated } ? Fq(type) + "?" : Fq(type);
	}

	// --- Resources + Style markup ---

	private static void EmitResources(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		// <X.Resources> holds keyed resources, optionally wrapped in an explicit <ResourceDictionary> that may also merge
		// inline dictionaries.
		var items = ObjectChildren(propEl);
		if (items.Count == 1 && items[0].Name.LocalName == "ResourceDictionary")
		{
			ReportDictionarySource(gen, items[0]);
			items = ObjectChildren(items[0]);
		}

		scope.Emit($"{ownerVar}.Resources ??= new global::SUIsei.ResourceDictionary();");
		foreach (var resEl in items)
		{
			if (resEl.Name.LocalName != "ResourceDictionary.MergedDictionaries")
			{
				EmitResourceEntry(gen, scope, ownerVar, $"{ownerVar}.Resources", resEl);
				continue;
			}

			// The merged list is rebuilt on every InitializeComponent, so a hot-reload rebuild does not merge twice. Each
			// dictionary joins the list before it is filled, so a style can be BasedOn one declared earlier in it.
			scope.Emit($"{ownerVar}.Resources.MergedDictionaries.Clear();");
			foreach (var mergedEl in ObjectChildren(resEl))
			{
				if (mergedEl.Name.LocalName != "ResourceDictionary")
				{
					gen.Report(BadPropertyElement, mergedEl, $"{mergedEl.Name.LocalName} (MergedDictionaries holds ResourceDictionary elements)");
					continue;
				}
				ReportDictionarySource(gen, mergedEl);
				var mergedVar = NewName(gen, "__merged");
				scope.Emit($"var {mergedVar} = new global::SUIsei.ResourceDictionary();");
				scope.Emit($"{ownerVar}.Resources.MergedDictionaries.Add({mergedVar});");
				foreach (var entry in ObjectChildren(mergedEl))
					EmitResourceEntry(gen, scope, ownerVar, mergedVar, entry);
			}
		}
		scope.ResourceOwners.Add(ownerVar);
	}

	// One keyed resource into the dictionary `dictionaryExpr`: a Style, a ControlTemplate shared by the controls that name
	// it (Template="{StaticResource Key}"), or any other object, such as a brush shared by the elements that name it with
	// {StaticResource}. A resource lookup made while building it (a BasedOn) starts from `ownerVar`. Data and items-panel
	// templates are set with <X.ItemTemplate> and the like, not kept as resources.
	private static void EmitResourceEntry(Gen gen, Scope scope, string ownerVar, string dictionaryExpr, XElement resEl)
	{
		var key = resEl.Attribute(X + "Key")?.Value;
		if (key is null) { gen.Report(BadPropertyElement, resEl, $"{resEl.Name.LocalName} (resource needs an x:Key)"); return; }
		if (resEl.Name.LocalName is "DataTemplate" or "ItemsPanelTemplate") { gen.Report(BadPropertyElement, resEl, resEl.Name.LocalName); return; }
		var valueVar = resEl.Name.LocalName switch
		{
			"Style" => EmitStyle(gen, scope, ownerVar, resEl),
			"ControlTemplate" => ControlTemplateValue(gen, resEl, gen.Compilation.GetTypeByMetadataName("SUIsei.Controls.Control")!),
			_ => EmitElement(gen, scope, resEl),
		};
		if (valueVar is not null)
			scope.Emit($"{dictionaryExpr}.Add({Quote(key)}, {valueVar});");
	}

	// A dictionary loaded from a file (Source="…") needs that file at runtime, which an inline-only subset cannot give.
	private static void ReportDictionarySource(Gen gen, XElement dictionaryEl)
	{
		if (dictionaryEl.Attribute("Source") is { } source)
			gen.Report(BadPropertyElement, source, "ResourceDictionary Source (a dictionary loaded from another file)");
	}

	private static string? EmitStyle(Gen gen, Scope scope, string ownerVar, XElement styleEl)
	{
		var targetType = ResolveTypeName(gen, styleEl, styleEl.Attribute("TargetType")?.Value);
		if (targetType is null) { gen.Report(BadPropertyElement, styleEl, "Style (needs a resolvable TargetType)"); return null; }

		var styleVar = NewName(gen, "__style");
		// BasedOn="{StaticResource Key}" chains a base style, looked up like any {StaticResource} as the dictionary is built.
		var basedOnInit = "";
		if (styleEl.Attribute("BasedOn") is { } basedOn)
		{
			if (IsMarkup(basedOn.Value, out var basedOnInner) && TryParseStaticResource(basedOnInner, out var baseKey))
				basedOnInit = $" BasedOn = ({ResourceLookup(scope, ownerVar, baseKey)}) as global::SUIsei.Style,";
			else
				gen.Report(UnsupportedMarkup, basedOn, $"BasedOn=\"{basedOn.Value.Trim()}\" (a base style is named with {{StaticResource Key}})");
		}
		scope.Emit($"var {styleVar} = new global::SUIsei.Style {{{basedOnInit} }};");
		var setterCount = 0;

		foreach (var setterEl in ObjectChildren(styleEl))
		{
			if (setterEl.Name.LocalName != "Setter") { gen.Report(BadPropertyElement, setterEl, setterEl.Name.LocalName); continue; }
			var propName = setterEl.Attribute("Property")?.Value;
			var valueAttr = setterEl.Attribute("Value");
			var valueEl = setterEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value");
			if (propName is null || (valueAttr is null) == (valueEl is null))
			{
				gen.Report(BadPropertyElement, setterEl, "Setter (needs Property, and Value as an attribute or a <Setter.Value>)");
				continue;
			}

			// Resolve Property to the dependency-property static field <TargetType>.<Property>Property, or for an attached
			// property "Owner.Prop" (prefixed for the extension or a using: namespace) <Owner>.<Prop>Property, and convert
			// the value for that property's type.
			if (ResolveSetterProperty(gen, setterEl, targetType, propName, out var dpOwner) is not { } setterTarget)
			{
				gen.Report(BadSetterProperty, setterEl, propName, dpOwner);
				continue;
			}
			var (dp, valueType) = setterTarget;

			string? code;
			if (valueEl is not null)
				code = SetterElementValue(gen, scope, valueEl, valueType, propName, dp, targetType);
			else if (!IsMarkup(valueAttr!.Value, out var inner))
				code = ConvertOrReport(gen, valueAttr, valueType, valueAttr.Value, propName, dp);
			else if (!TryMarkupValue(gen, scope, valueAttr, ownerVar, inner, valueType, propName, out code))
			{
				gen.Report(UnsupportedMarkup, valueAttr, valueAttr.Value.Trim());
				continue;
			}
			if (code is null)
				continue;
			scope.Emit($"{styleVar}.Setters.Add(new global::SUIsei.Setter {{ Property = {Fq(dp.ContainingType!)}.{dp.Name}, Value = {code} }});");
			RecordSetterSource(gen, scope, setterEl, $"{styleVar}.Setters[{setterCount++}]");
		}
		return styleVar;
	}

	// A Setter's Property: the dependency-property field it names and the property's value type. "Prop" is looked up on
	// the style's TargetType (with its CLR property giving the type); "Owner.Prop" is an attached property, looked up on
	// Owner, typed by the field. Null when it does not resolve, with `owner` naming where it was looked for.
	private static (IFieldSymbol Field, ITypeSymbol ValueType)? ResolveSetterProperty(Gen gen, XElement setterEl, INamedTypeSymbol targetType, string propName, out string owner)
	{
		var dot = propName.LastIndexOf('.');
		if (dot < 0)
		{
			owner = targetType.ToDisplayString();
			return FindDependencyProperty(targetType, propName) is { } field && FindProperty(targetType, propName) is { } clr
				? (field, clr.Type)
				: null;
		}

		var ownerName = propName.Substring(0, dot);
		var ownerType = ResolveTypeName(gen, setterEl, ownerName);
		owner = ownerType?.ToDisplayString() ?? ownerName;
		return ownerType is not null && FindDependencyProperty(ownerType, propName.Substring(dot + 1)) is { } attached && DependencyValueType(attached) is { } valueType
			? (attached, valueType)
			: null;
	}

	// <Setter.Value>: text converted for the property, or one object, made once and shared by every element the style
	// applies to; a ControlTemplate is such an object, its factory building a tree for each control, typed by the style's
	// `targetType` unless it names its own. Data and items-panel templates are set with <X.ItemTemplate> and the like.
	private static string? SetterElementValue(Gen gen, Scope scope, XElement valueEl, ITypeSymbol valueType, string propertyName, IFieldSymbol property,
		INamedTypeSymbol targetType)
	{
		var children = ObjectChildren(valueEl);
		var text = TextContent(valueEl);
		if (children.Count == 0 && text is not null)
			return ConvertOrReport(gen, valueEl, valueType, text, propertyName, property);
		if (children.Count == 1 && text is null && children[0].Name.LocalName == "ControlTemplate"
			&& SymbolEqualityComparer.Default.Equals(valueType, gen.Compilation.GetTypeByMetadataName("SUIsei.Controls.ControlTemplate")))
			return ControlTemplateValue(gen, children[0], targetType);
		if (children.Count == 1 && text is null && children[0].Name.LocalName is not ("ControlTemplate" or "DataTemplate" or "ItemsPanelTemplate"))
			return EmitElement(gen, scope, children[0]);
		gen.Report(BadPropertyElement, valueEl, "Setter.Value (takes one value or object; a ControlTemplate only for Template, and other templates are set with <X.ItemTemplate> and the like)");
		return null;
	}

	// <Grid.ColumnDefinitions>/<Grid.RowDefinitions> → grid.<collection>.Add(GridLength …) per ColumnDefinition/
	// RowDefinition child, reading its Width/Height as a GridLength. A definition with neither is "*".
	private static void EmitGridDefinitions(Gen gen, Scope scope, string ownerVar, XElement propEl, string collection)
	{
		var gridLength = gen.Compilation.GetTypeByMetadataName("SUIsei.GridLength")!;
		var definition = collection == "RowDefinitions" ? "RowDefinition" : "ColumnDefinition";
		var sizeName = collection == "RowDefinitions" ? "Height" : "Width";
		foreach (var def in propEl.Elements())
		{
			if (IsIgnorable(def, def.Name.Namespace))
				continue;
			if (def.Name.LocalName != definition)
			{
				gen.Report(OutsideSubset, def, $"<{def.Name.LocalName}> in Grid.{collection}", $" (it holds {definition}s)");
				continue;
			}
			foreach (var attr in def.Attributes())
			{
				if (attr.IsNamespaceDeclaration || IsIgnorable(def, attr.Name.Namespace) || IsDirective(gen, def, attr) || attr.Name.LocalName == sizeName)
					continue;
				gen.Report(OutsideSubset, attr, $"'{attr.Name.LocalName}' on a {definition}", $" (a {definition} takes its {sizeName} alone)");
			}
			var size = def.Attribute(sizeName);
			var code = size is null ? "global::SUIsei.GridLength.Star" : ConvertOrReport(gen, size, gridLength, size.Value, size.Name.LocalName);
			if (code is not null)
				scope.Emit($"{ownerVar}.{collection}.Add({code});");
		}
	}
}
