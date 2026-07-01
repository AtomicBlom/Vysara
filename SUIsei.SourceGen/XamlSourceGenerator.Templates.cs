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

		gen.Report(BadPropertyElement, propEl, localName);
	}

	private static void EmitVisualStateGroups(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var groupsVar = NewName(gen, "__vsg");
		scope.Emit($"var {groupsVar} = new global::System.Collections.Generic.List<global::SUIsei.Animation.VisualStateGroup>();");
		foreach (var groupEl in propEl.Elements())
		{
			if (groupEl.Name.LocalName != "VisualStateGroup") { gen.Report(BadPropertyElement, groupEl, groupEl.Name.LocalName); continue; }
			var groupVar = NewName(gen, "__vsgrp");
			var groupName = groupEl.Attribute("Name")?.Value ?? "";
			scope.Emit($"var {groupVar} = new global::SUIsei.Animation.VisualStateGroup {{ Name = {Quote(groupName)} }};");
			foreach (var stateEl in groupEl.Elements())
			{
				if (stateEl.Name.LocalName != "VisualState") { gen.Report(BadPropertyElement, stateEl, stateEl.Name.LocalName); continue; }
				var stateName = stateEl.Attribute("Name")?.Value ?? "";
				var sbEl = stateEl.Elements().FirstOrDefault(e => e.Name.LocalName == "Storyboard");
				var sbExpr = sbEl is null ? "null" : EmitStoryboard(gen, scope, sbEl);
				scope.Emit($"{groupVar}.States.Add(new global::SUIsei.Animation.VisualState {{ Name = {Quote(stateName)}, Storyboard = {sbExpr} }});");
			}
			scope.Emit($"{groupsVar}.Add({groupVar});");
		}
		scope.Emit($"global::SUIsei.Animation.VisualStateManager.SetVisualStateGroups({ownerVar}, {groupsVar});");
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
		var kind = animEl.Name.LocalName;
		var typeName = kind switch
		{
			"ColorAnimation" => "global::SUIsei.Animation.ColorAnimation",
			"DoubleAnimation" => "global::SUIsei.Animation.DoubleAnimation",
			_ => null,
		};
		if (typeName is null) { gen.Report(BadPropertyElement, animEl, kind); return; }

		var animVar = NewName(gen, "__anim");
		scope.Emit($"var {animVar} = new {typeName}();");

		string? targetName = null, targetProp = null;
		foreach (var attr in animEl.Attributes())
		{
			if (attr.IsNamespaceDeclaration)
				continue;
			switch (attr.Name.LocalName)
			{
				case "Storyboard.TargetName": targetName = attr.Value.Trim(); break;
				case "Storyboard.TargetProperty": targetProp = attr.Value.Trim(); break;
				case "From": EmitAnimationValue(gen, scope, animVar, "From", kind, attr); break;
				case "To": EmitAnimationValue(gen, scope, animVar, "To", kind, attr); break;
				case "By" when kind == "DoubleAnimation": EmitAnimationValue(gen, scope, animVar, "By", kind, attr); break;
				case "Duration":
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

		if (targetName is not null)
		{
			if (!scope.Names.Contains(targetName)) gen.Report(BadStoryboardTarget, animEl, targetName);
			else scope.Emit($"{animVar}.Target = {targetName};");
		}
		if (targetProp is not null)
		{
			if (!IsAnimatableChannel(targetProp)) gen.Report(BadAnimationProperty, animEl, targetProp);
			scope.Emit($"{animVar}.TargetProperty = {Quote(targetProp)};");
		}
		scope.Emit($"{sbVar}.Children.Add({animVar});");
	}

	private static void EmitAnimationValue(Gen gen, Scope scope, string animVar, string prop, string kind, XAttribute attr)
	{
		if (kind == "ColorAnimation")
		{
			if (TryParseColor(attr.Value, out var code)) scope.Emit($"{animVar}.{prop} = {code};");
			else gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "colour");
		}
		else // DoubleAnimation
		{
			if (double.TryParse(attr.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
				scope.Emit($"{animVar}.{prop} = {attr.Value.Trim()}d;");
			else
				gen.Report(BadLiteralValue, attr, attr.Value.Trim(), "number");
		}
	}

	private static bool IsAnimatableChannel(string property) => property is
		"Opacity" or "Background" or "Foreground" or "Fill" or "RenderTransform.Translate.X" or "RenderTransform.Translate.Y";

	// --- Templates: ControlTemplate / DataTemplate / ItemsPanelTemplate factories ---

	private static void EmitControlTemplateProperty(Gen gen, Scope scope, string ownerVar, INamedTypeSymbol ownerType, XElement propEl)
	{
		var templateEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
		if (templateEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }

		// TargetType (optional) types the data root so non-Content {TemplateBinding}s and storyboard targets resolve;
		// default to the owning control's type.
		var targetType = ResolveTypeName(gen, templateEl.Attribute("TargetType")?.Value) ?? ownerType;
		var method = EmitTemplateFactory(gen, templateEl, isControlTemplate: true,
			paramDecl: "global::SUIsei.Controls.Control __tc", castLocal: null, dataRootExpr: "__tc", dataRootType: targetType);
		if (method is null)
			return;
		scope.Emit($"{ownerVar}.Template = new global::SUIsei.Controls.ControlTemplate {{ Factory = {method} }};");
	}

	private static void EmitDataTemplateProperty(Gen gen, Scope scope, string ownerVar, string targetProp, XElement propEl)
	{
		var templateEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "DataTemplate");
		if (templateEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }

		// x:DataType types the item so the template's {x:Bind}s resolve; without it, an {x:Bind} inside diagnoses.
		var dataType = ResolveTypeName(gen, templateEl.Attribute(X + "DataType")?.Value);
		var castLocal = dataType is null ? null : $"var __data = ({Fq(dataType)})__item!;";
		var method = EmitTemplateFactory(gen, templateEl, isControlTemplate: false,
			paramDecl: "object? __item", castLocal: castLocal, dataRootExpr: "__data", dataRootType: dataType);
		if (method is null)
			return;
		scope.Emit($"{ownerVar}.{targetProp} = new global::SUIsei.Controls.DataTemplate {{ Factory = {method} }};");
	}

	private static void EmitItemsPanelProperty(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		var tmplEl = propEl.Elements().FirstOrDefault(e => e.Name.LocalName == "ItemsPanelTemplate");
		var panelEl = tmplEl?.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
		if (panelEl is null) { gen.Report(BadPropertyElement, propEl, propEl.Name.LocalName); return; }
		var panelType = ResolveType(gen.Compilation, panelEl.Name.LocalName);
		if (panelType is null) { gen.Report(UnknownElement, panelEl, panelEl.Name.LocalName); return; }
		scope.Emit($"{ownerVar}.ItemsPanel = new global::SUIsei.Controls.ItemsPanelTemplate {{ Factory = static () => new {Fq(panelType)}() }};");
	}

	// Emit a static factory method that news the template subtree and returns its root, then return its name.
	private static string? EmitTemplateFactory(Gen gen, XElement templateEl, bool isControlTemplate, string paramDecl, string? castLocal, string dataRootExpr, INamedTypeSymbol? dataRootType)
	{
		var rootChild = templateEl.Elements().FirstOrDefault(e => !e.Name.LocalName.Contains('.'));
		if (rootChild is null) { gen.Report(BadPropertyElement, templateEl, templateEl.Name.LocalName); return null; }

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
		};
		var rootVar = EmitElement(gen, scope, rootChild);
		if (rootVar is null)
			return null;

		gen.Methods.AppendLine($"\tprivate static global::SUIsei.FrameworkElement {method}({paramDecl})");
		gen.Methods.AppendLine("\t{");
		gen.Methods.Append(body);
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

	// A {TemplateBinding source} wire: the templated control's property flows OneWay into a child setter.
	private static void EmitTemplateBinding(Gen gen, Scope scope, XAttribute attr, string targetVar, IPropertySymbol prop, string sourceProp)
	{
		// Content is a CLR property carrying a ContentChanged event (not a dependency property); wire via that event.
		if (sourceProp == "Content")
		{
			var host = $"((global::SUIsei.Controls.ContentControl){scope.DataRootExpr})";
			scope.Emit($"{targetVar}.{prop.Name} = {host}.Content;");
			scope.Emit($"{host}.ContentChanged += (_, _) => {targetVar}.{prop.Name} = {host}.Content;");
			return;
		}
		// Otherwise the source must be a dependency property on the templated control.
		if (scope.DataRootType is null || FindDependencyProperty(scope.DataRootType, sourceProp) is not { } dp)
		{
			gen.Report(BadAnimationProperty, attr, sourceProp);
			return;
		}
		scope.Emit($"{scope.DataRootExpr}.RegisterTemplateBinding({Fq(dp.ContainingType!)}.{dp.Name}, __v => {targetVar}.{prop.Name} = ({Fq(prop.Type)})__v!);");
	}

	// --- Resources + Style markup ---

	private static void EmitResources(Gen gen, Scope scope, string ownerVar, XElement propEl)
	{
		// <X.Resources> holds keyed resources, optionally wrapped in an explicit <ResourceDictionary>.
		var items = propEl.Elements().ToList();
		if (items.Count == 1 && items[0].Name.LocalName == "ResourceDictionary")
			items = items[0].Elements().ToList();

		scope.Emit($"{ownerVar}.Resources ??= new global::SUIsei.ResourceDictionary();");
		foreach (var resEl in items)
		{
			var key = resEl.Attribute(X + "Key")?.Value;
			if (key is null) { gen.Report(BadPropertyElement, resEl, $"{resEl.Name.LocalName} (resource needs an x:Key)"); continue; }
			if (resEl.Name.LocalName != "Style") { gen.Report(BadPropertyElement, resEl, resEl.Name.LocalName); continue; }
			if (EmitStyle(gen, scope, ownerVar, resEl) is { } styleVar)
				scope.Emit($"{ownerVar}.Resources.Add({Quote(key)}, {styleVar});");
		}
	}

	private static string? EmitStyle(Gen gen, Scope scope, string ownerVar, XElement styleEl)
	{
		var targetType = ResolveTypeName(gen, styleEl.Attribute("TargetType")?.Value);
		if (targetType is null) { gen.Report(BadPropertyElement, styleEl, "Style (needs a resolvable TargetType)"); return null; }

		var styleVar = NewName(gen, "__style");
		// BasedOn="{StaticResource Key}" chains a base style, resolved from the element-tree resources at runtime.
		var basedOnInit = "";
		var basedOn = styleEl.Attribute("BasedOn")?.Value;
		if (basedOn is not null && IsMarkup(basedOn, out var inner) && TryParseStaticResource(inner, out var baseKey))
			basedOnInit = $" BasedOn = {ownerVar}.FindResource({Quote(baseKey)}) as global::SUIsei.Style,";
		scope.Emit($"var {styleVar} = new global::SUIsei.Style {{{basedOnInit} }};");

		foreach (var setterEl in styleEl.Elements())
		{
			if (setterEl.Name.LocalName != "Setter") { gen.Report(BadPropertyElement, setterEl, setterEl.Name.LocalName); continue; }
			var propName = setterEl.Attribute("Property")?.Value;
			var rawValue = setterEl.Attribute("Value")?.Value;
			if (propName is null || rawValue is null) { gen.Report(BadPropertyElement, setterEl, "Setter (needs Property and Value)"); continue; }

			// Resolve Property to the dependency-property static field <TargetType>.<Property>Property and convert the
			// value against that property's CLR type.
			var dp = FindDependencyProperty(targetType, propName);
			var clrProp = FindProperty(targetType, propName);
			if (dp is null || clrProp is null) { gen.Report(BadSetterProperty, setterEl, propName, targetType.ToDisplayString()); continue; }
			if (!TrySetterValue(clrProp.Type, rawValue, out var code))
			{
				gen.Report(InvalidLiteral, setterEl, rawValue.Trim(), propName, clrProp.Type.ToDisplayString());
				continue;
			}
			scope.Emit($"{styleVar}.Setters.Add(new global::SUIsei.Setter {{ Property = {Fq(dp.ContainingType!)}.{dp.Name}, Value = {code} }});");
		}
		return styleVar;
	}

	// A Setter value: the same attribute-value subset as a plain property (literal/enum + colour + thickness).
	private static bool TrySetterValue(ITypeSymbol type, string raw, out string code) =>
		TryAttributeValue(type, raw, out code) == LiteralStatus.Ok;

	// <Grid.ColumnDefinitions>/<Grid.RowDefinitions> → grid.<collection>.Add(GridLength …) per ColumnDefinition/
	// RowDefinition child, reading its Width/Height as a GridLength (pixels / "*" weight / "Auto").
	private static void EmitGridDefinitions(Gen gen, Scope scope, string ownerVar, XElement propEl, string collection)
	{
		foreach (var def in propEl.Elements())
		{
			var raw = def.Attribute("Width")?.Value ?? def.Attribute("Height")?.Value;
			if (!TryGridLength(raw, out var code))
			{
				gen.Report(BadLiteralValue, def, raw ?? "", "grid length");
				continue;
			}
			scope.Emit($"{ownerVar}.{collection}.Add({code});");
		}
	}

	// Parse a WinUI grid-length string: "Auto", "*" / "N*" (star weight), or a pixel number. Empty defaults to "*".
	private static bool TryGridLength(string? raw, out string code)
	{
		var value = (raw ?? "*").Trim();
		if (value.Length == 0 || value == "*")
		{
			code = "global::SUIsei.GridLength.Star";
			return true;
		}
		if (string.Equals(value, "Auto", StringComparison.OrdinalIgnoreCase))
		{
			code = "global::SUIsei.GridLength.Auto";
			return true;
		}
		if (value.EndsWith("*", StringComparison.Ordinal))
		{
			var weight = value.Substring(0, value.Length - 1);
			if (weight.Length == 0)
			{
				code = "global::SUIsei.GridLength.Star";
				return true;
			}
			if (float.TryParse(weight, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
			{
				code = $"global::SUIsei.GridLength.Stars({weight}f)";
				return true;
			}
			code = "";
			return false;
		}
		if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
		{
			code = $"global::SUIsei.GridLength.Pixels({value}f)";
			return true;
		}
		code = "";
		return false;
	}
}
