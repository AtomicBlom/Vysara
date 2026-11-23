namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Coverage for the reflection-free dependency-property model + value-precedence ladder:
/// resolve <c>Animation &gt; Local &gt; Template &gt; Style &gt; Inherited &gt; Default</c>, fire the
/// typed change callback only on a <em>resolved</em> change, and ensure <c>SetValue</c> never clobbers an active
/// animation. Asserted directly on a probe object — no engine, no GPU.
/// </summary>
public sealed class DependencyPropertyTests
{
	private sealed class Probe : DependencyObject
	{
		public static readonly DependencyProperty<Probe, int> ValueProperty =
			DependencyProperty.Register<Probe, int>("Value",
				new PropertyMetadata<Probe, int> { DefaultValue = -1, PropertyChanged = static (o, ov, nv) => o.Log.Add((ov, nv)) });

		public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
		public readonly List<(int Old, int New)> Log = [];
	}

	[Test]
	public void Unset_property_resolves_to_its_typed_default()
	{
		new Probe().Value.ShouldBe(-1);
	}

	[Test]
	public void Local_overrides_default_and_fires_the_typed_change_once()
	{
		var p = new Probe();
		p.Value = 5;
		p.Value.ShouldBe(5);
		p.Log.ShouldHaveSingleItem();
		p.Log[0].ShouldBe((-1, 5));
	}

	[Test]
	public void Setting_the_same_resolved_value_does_not_refire()
	{
		var p = new Probe { Value = 7 };
		p.Log.Clear();
		p.Value = 7;
		p.Log.ShouldBeEmpty();
	}

	[Test]
	public void Local_outranks_style_which_outranks_inherited()
	{
		var p = new Probe();
		p.SetInheritedValue(Probe.ValueProperty, 1);
		p.Value.ShouldBe(1);
		p.SetStyleValue(Probe.ValueProperty, 2);
		p.Value.ShouldBe(2);
		p.Value = 3; // Local
		p.Value.ShouldBe(3);
	}

	[Test]
	public void Template_sits_between_local_and_style()
	{
		var p = new Probe();
		p.SetStyleValue(Probe.ValueProperty, 2);
		p.SetTemplateValue(Probe.ValueProperty, 4);
		p.Value.ShouldBe(4); // Template outranks Style
		p.Value = 3;
		p.Value.ShouldBe(3); // Local outranks Template
		p.SetTemplateValue(Probe.ValueProperty, 5);
		p.Value.ShouldBe(3); // a later template push does not overwrite Local
		Probe.ValueProperty.ClearRung(p, DependencyObject.ValueRung.Local);
		p.Value.ShouldBe(5); // clearing Local reveals the template's latest value
		p.ClearTemplateValue(Probe.ValueProperty);
		p.Value.ShouldBe(2); // and clearing that, the style's
	}

	[Test]
	public void Animation_outranks_a_template_value()
	{
		var p = new Probe();
		p.SetTemplateValue(Probe.ValueProperty, 4);
		p.SetAnimatedValue(Probe.ValueProperty, 9);
		p.Value.ShouldBe(9);
		p.ClearAnimatedValue(Probe.ValueProperty);
		p.Value.ShouldBe(4);
	}

	[Test]
	public void A_built_in_value_sits_between_style_and_inherited()
	{
		var p = new Probe();
		p.SetInheritedValue(Probe.ValueProperty, 1);
		p.SetBuiltInValue(Probe.ValueProperty, 6);
		p.Value.ShouldBe(6); // BuiltInStyle outranks Inherited
		p.SetStyleValue(Probe.ValueProperty, 2);
		p.Value.ShouldBe(2); // Style outranks BuiltInStyle
		p.ClearStyleValue(Probe.ValueProperty);
		p.Value.ShouldBe(6);
		p.Value = 3;
		p.Value.ShouldBe(3);
		p.HasLocalValue(Probe.ValueProperty).ShouldBeTrue();
	}

	[Test]
	public void A_controls_built_in_template_and_tab_stop_are_not_local_values()
	{
		var button = new BinaryVibrance.Vysara.Controls.Button();

		button.IsTabStop.ShouldBeTrue();
		button.Template.ShouldNotBeNull();
		button.HasLocalValue(UIElement.IsTabStopProperty).ShouldBeFalse();
		button.HasLocalValue(BinaryVibrance.Vysara.Controls.Control.TemplateProperty).ShouldBeFalse();
	}

	[Test]
	public void A_style_replaces_a_controls_built_in_template()
	{
		var replacement = new BinaryVibrance.Vysara.Controls.ControlTemplate { Factory = static _ => new BinaryVibrance.Vysara.Controls.Border() };
		var button = new BinaryVibrance.Vysara.Controls.Button
		{
			Style = new Style
			{
				Setters =
				{
					new Setter { Property = BinaryVibrance.Vysara.Controls.Control.TemplateProperty, Value = replacement },
					new Setter { Property = UIElement.IsTabStopProperty, Value = false },
				},
			},
		};

		button.Template.ShouldBe(replacement);
		button.IsTabStop.ShouldBeFalse();
	}

	[Test]
	public void Animation_outranks_local_and_clearing_it_reverts_to_local()
	{
		var p = new Probe { Value = 5 };
		p.SetAnimatedValue(Probe.ValueProperty, 42);
		p.Value.ShouldBe(42);
		p.ClearAnimatedValue(Probe.ValueProperty);
		p.Value.ShouldBe(5); // Local, never clobbered by the animation
	}

	[Test]
	public void Every_precedence_transition_that_changes_the_resolved_value_fires_the_callback()
	{
		var p = new Probe();
		p.SetInheritedValue(Probe.ValueProperty, 1);  // default(-1) -> 1
		p.SetStyleValue(Probe.ValueProperty, 2);      // 1 -> 2 (Style outranks Inherited)
		p.Value = 3;                                  // 2 -> 3 (Local outranks Style)
		p.SetAnimatedValue(Probe.ValueProperty, 4);   // 3 -> 4 (Animation outranks Local)
		p.ClearAnimatedValue(Probe.ValueProperty);    // 4 -> 3 (revert to Local)

		// The callback fires once per resolved change, with the right (old, new) — for every rung, not just Local.
		p.Log.ShouldBe([(-1, 1), (1, 2), (2, 3), (3, 4), (4, 3)]);
	}

	[Test]
	public void A_lower_precedence_change_masked_by_a_higher_rung_does_not_fire()
	{
		var p = new Probe { Value = 5 };              // Local 5
		p.SetAnimatedValue(Probe.ValueProperty, 9);   // Animation 9 outranks Local
		p.Log.Clear();

		// Style sits below the active Animation, so the resolved value stays 9 — no callback must fire.
		p.SetStyleValue(Probe.ValueProperty, 2);
		p.Value.ShouldBe(9);
		p.Log.ShouldBeEmpty();
	}

	private sealed class CoerceProbe : DependencyObject
	{
		public static readonly DependencyProperty<CoerceProbe, int> MaxProperty =
			DependencyProperty.Register<CoerceProbe, int>("Max", new PropertyMetadata<CoerceProbe, int> { DefaultValue = 100 });

		public static readonly DependencyProperty<CoerceProbe, int> ValueProperty =
			DependencyProperty.Register<CoerceProbe, int>("Value",
				new PropertyMetadata<CoerceProbe, int> { Coerce = static (o, v) => Math.Min(v, o.Max) });

		public int Max { get => GetValue(MaxProperty); set => SetValue(MaxProperty, value); }
		public int Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
	}

	[Test]
	public void Coercion_applies_to_the_resolved_value_and_reflects_a_dependency_on_read()
	{
		var p = new CoerceProbe();
		p.Value = 50;
		p.Value.ShouldBe(50); // under the default Max=100, uncoerced

		// Lower Max below the stored Value: coercion runs against the resolved value on read, so Value follows Max —
		// it is not frozen at the value coerced when it was written (which would still read 50).
		p.Max = 30;
		p.Value.ShouldBe(30);
	}
}
