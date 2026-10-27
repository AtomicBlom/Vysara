using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// The layout properties as dependency properties: FrameworkElement's sizes, margin and alignments and Border's padding
/// and border thickness, which a style sets and a storyboard animates, and <c>Tag</c>. They are field-backed, so a value
/// only code or markup set lives in the element's field and holds nothing in the property store, and a style or an
/// animation ranks against it as against any local value. Also WinUI's <c>Auto</c> for Width and Height in markup, and
/// the rule that a size animation needs a <c>From</c> when the size is Auto.
/// </summary>
public sealed class LayoutPropertyTests
{
	private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);
	private static readonly Vector2 Surface = new(400f, 600f);

	private static Storyboard StoryboardOf(params Timeline[] animations)
	{
		var storyboard = new Storyboard();
		foreach (var animation in animations)
			storyboard.Children.Add(animation);
		return storyboard;
	}

	private static UIRoot Root(UIElement content)
	{
		var root = new UIRoot(new DrawListId(0)) { Content = content };
		root.LayoutPass(new Size(Surface.X, Surface.Y));
		return root;
	}

	private static Style CardStyle(float width) => new()
	{
		Setters =
		{
			new Setter { Property = FrameworkElement.WidthProperty, Value = width },
			new Setter { Property = FrameworkElement.HeightProperty, Value = 40f },
			new Setter { Property = FrameworkElement.MinWidthProperty, Value = 10f },
			new Setter { Property = FrameworkElement.MinHeightProperty, Value = 5f },
			new Setter { Property = FrameworkElement.MaxWidthProperty, Value = 300f },
			new Setter { Property = FrameworkElement.MaxHeightProperty, Value = 80f },
			new Setter { Property = FrameworkElement.MarginProperty, Value = new Thickness(4f, 6f, 8f, 10f) },
			new Setter { Property = FrameworkElement.HorizontalAlignmentProperty, Value = HorizontalAlignment.Left },
			new Setter { Property = FrameworkElement.VerticalAlignmentProperty, Value = VerticalAlignment.Top },
			new Setter { Property = FrameworkElement.TagProperty, Value = "card" },
			new Setter { Property = Border.PaddingProperty, Value = new Thickness(3f) },
			new Setter { Property = Border.BorderThicknessProperty, Value = new Thickness(1f) },
		},
	};

	// --- Styles ---

	[Test]
	public void A_style_sets_every_layout_property_and_layout_reads_them()
	{
		var child = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
		var border = new Border { Style = CardStyle(120f), Child = child };
		Root(border);

		border.Width.ShouldBe(120f);
		border.Height.ShouldBe(40f);
		border.MinWidth.ShouldBe(10f);
		border.MinHeight.ShouldBe(5f);
		border.MaxWidth.ShouldBe(300f);
		border.MaxHeight.ShouldBe(80f);
		border.Margin.ShouldBe(new Thickness(4f, 6f, 8f, 10f));
		border.HorizontalAlignment.ShouldBe(HorizontalAlignment.Left);
		border.VerticalAlignment.ShouldBe(VerticalAlignment.Top);
		border.Tag.ShouldBe("card");
		border.Padding.ShouldBe(new Thickness(3f));
		border.BorderThickness.ShouldBe(new Thickness(1f));

		border.LayoutRect.ShouldBe(new Rect(4f, 6f, 120f, 40f));
		child.LayoutRect.ShouldBe(new Rect(4f, 4f, 0f, 0f)); // inside the border and the padding
	}

	[Test]
	public void Each_property_reads_the_same_through_the_store_and_its_field()
	{
		var border = new Border();
		void Same()
		{
			border.GetValue(FrameworkElement.WidthProperty).ShouldBe(border.Width);
			border.GetValue(FrameworkElement.MarginProperty).ShouldBe(border.Margin);
			border.GetValue(FrameworkElement.HorizontalAlignmentProperty).ShouldBe(border.HorizontalAlignment);
			border.GetValue(Border.PaddingProperty).ShouldBe(border.Padding);
		}

		Same(); // the defaults
		border.Width.ShouldBe(float.NaN);
		border.MaxWidth.ShouldBe(float.PositiveInfinity);
		border.HorizontalAlignment.ShouldBe(HorizontalAlignment.Stretch);

		border.SetValue(FrameworkElement.WidthProperty, 50f);
		border.Margin = new Thickness(2f);
		border.Padding = new Thickness(1f);
		Same();
		border.Width.ShouldBe(50f);

		border.Style = CardStyle(120f);
		Same();
		border.Width.ShouldBe(50f); // the local value outranks the style
		border.HorizontalAlignment.ShouldBe(HorizontalAlignment.Left);
	}

	[Test]
	public void A_local_value_outranks_the_style_whether_it_was_set_before_or_after()
	{
		var before = new Border { Width = 60f, HorizontalAlignment = HorizontalAlignment.Stretch };
		before.Style = CardStyle(120f);
		before.Width.ShouldBe(60f);
		before.HorizontalAlignment.ShouldBe(HorizontalAlignment.Stretch); // a local value that is the default still counts
		before.HasLocalValue(FrameworkElement.HorizontalAlignmentProperty).ShouldBeTrue();
		before.HasLocalValue(FrameworkElement.VerticalAlignmentProperty).ShouldBeFalse();

		var after = new Border { Style = CardStyle(120f) };
		after.Width = 70f;
		after.Width.ShouldBe(70f);

		before.Style = null;
		before.Width.ShouldBe(60f); // the style's removal leaves the local value
		before.Height.ShouldBe(float.NaN);
		before.HorizontalAlignment.ShouldBe(HorizontalAlignment.Stretch);
	}

	[Test]
	public void Changing_or_removing_the_style_lays_the_element_out_again()
	{
		var border = new Border { Style = CardStyle(120f) };
		var root = Root(border);
		border.LayoutRect.Width.ShouldBe(120f);

		border.Style = CardStyle(90f);
		root.LayoutPass(new Size(Surface.X, Surface.Y));
		border.LayoutRect.Width.ShouldBe(90f);

		border.Style = null; // back to Auto, stretched across the surface
		root.LayoutPass(new Size(Surface.X, Surface.Y));
		border.LayoutRect.ShouldBe(new Rect(0f, 0f, Surface.X, Surface.Y));
	}

	// --- Animation ---

	[Test]
	public void An_animated_width_drives_layout_and_stopping_it_returns_the_local_value()
	{
		var border = new Border { Width = 100f, HorizontalAlignment = HorizontalAlignment.Left };
		var root = Root(border);
		var clock = new AnimationClock();
		var grow = StoryboardOf(new DoubleAnimation { To = 200.0, Duration = OneSecond, Target = border, TargetProperty = FrameworkElement.WidthProperty });

		clock.Begin(grow);
		clock.Advance(0.25);
		root.LayoutPass(new Size(Surface.X, Surface.Y));
		border.Width.ShouldBe(125f, 1e-4f); // from its own 100
		border.LayoutRect.Width.ShouldBe(125f, 1e-4f);

		clock.Stop(grow);
		root.LayoutPass(new Size(Surface.X, Surface.Y));
		border.Width.ShouldBe(100f);
		border.LayoutRect.Width.ShouldBe(100f);

		clock.Begin(grow);
		clock.Advance(1.5);
		root.LayoutPass(new Size(Surface.X, Surface.Y));
		border.LayoutRect.Width.ShouldBe(200f); // held at the end
	}

	[Test]
	public void An_animation_from_a_size_left_Auto_needs_a_From()
	{
		var border = new Border();
		var toWide = StoryboardOf(new DoubleAnimation { To = 200.0, Duration = OneSecond, Target = border, TargetProperty = FrameworkElement.WidthProperty });

		var message = Should.Throw<InvalidOperationException>(() => new AnimationClock().Begin(toWide)).Message;
		message.ShouldContain("(Width) on a Border is NaN (Auto, for a size)");
		message.ShouldContain("give it a From");

		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new DoubleAnimation { From = 100.0, To = 200.0, Duration = OneSecond, Target = border, TargetProperty = FrameworkElement.WidthProperty }));
		clock.Advance(0.5);
		border.Width.ShouldBe(150f);
	}

	[Test]
	public void An_animation_from_an_unbounded_maximum_needs_a_From()
	{
		var border = new Border();
		var shrink = StoryboardOf(new DoubleAnimation { To = 50.0, Target = border, TargetProperty = FrameworkElement.MaxWidthProperty });

		Should.Throw<InvalidOperationException>(() => new AnimationClock().Begin(shrink)).Message.ShouldContain("is Infinity: give it a From");
	}

	[Test]
	public void A_tween_from_a_size_left_Auto_throws_and_from_a_number_runs()
	{
		var border = new Border();
		var clock = new AnimationClock();

		Should.Throw<InvalidOperationException>(() => clock.Tween(border, FrameworkElement.WidthProperty, 200f, OneSecond)).Message
			.ShouldContain("Width's value now, which is NaN (Auto, for a size)");
		Should.Throw<InvalidOperationException>(() => clock.Tween(border, static b => b.Height, static (b, v) => b.Height = v, 20f, OneSecond));
		clock.HasActiveClocks.ShouldBeFalse(); // a refused tween leaves nothing running

		border.Width = 100f;
		clock.Tween(border, FrameworkElement.WidthProperty, 200f, OneSecond);
		clock.Advance(0.5);
		border.Width.ShouldBe(150f);
	}

	// --- No store, no allocation ---

	[Test]
	public void Setting_layout_properties_in_code_allocates_nothing()
	{
		var border = new Border();
		var i = 0;
		Allocations.ShouldAllocateNothing(() =>
		{
			var n = ++i;
			border.Width = 10f * n;
			border.Height = 5f * n;
			border.MinWidth = n;
			border.MinHeight = n;
			border.MaxWidth = 1000f * n;
			border.MaxHeight = 1000f * n;
			border.Margin = new Thickness(n);
			border.HorizontalAlignment = (n & 1) == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right;
			border.VerticalAlignment = (n & 1) == 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
			border.Padding = new Thickness(n);
			border.BorderThickness = new Thickness(n);
		});
	}

	[Test]
	public void Animating_a_width_and_laying_out_allocates_nothing_once_warm()
	{
		var border = new Border { Width = 100f, HorizontalAlignment = HorizontalAlignment.Left, Child = new Border { Margin = new Thickness(2f) } };
		var panel = new StackPanel();
		panel.Children.Add(border);
		panel.Children.Add(new Border { Height = 20f });
		var root = Root(panel);
		var clock = new AnimationClock();
		clock.Begin(StoryboardOf(new DoubleAnimation
		{
			From = 50.0,
			To = 250.0,
			Duration = OneSecond,
			AutoReverse = true,
			RepeatBehavior = RepeatBehavior.Forever,
			Target = border,
			TargetProperty = FrameworkElement.WidthProperty,
		}));
		void Frames(int count)
		{
			for (var f = 0; f < count; f++)
			{
				clock.Advance(1.0 / 60.0);
				root.LayoutPass(new Size(Surface.X, Surface.Y));
			}
		}

		Frames(30);
		Allocations.ShouldAllocateNothing(() => Frames(60));
		border.LayoutRect.Width.ShouldBe(border.Width);
	}

	[Test]
	public void Diagnostics_see_a_local_value_held_in_its_field()
	{
		var border = new Border { Width = 50f, Margin = new Thickness(1f) };
		border.Style = new Style
		{
			Setters =
			{
				new Setter { Property = FrameworkElement.HeightProperty, Value = 20f },
				new Setter { Property = FrameworkElement.MarginProperty, Value = new Thickness(5f) },
			},
		};
		var properties = Diagnostics.Properties(border);
		Diagnostics.PropertyValue Of(DependencyProperty dp) => properties.Single(p => ReferenceEquals(p.Property, dp));

		Of(FrameworkElement.WidthProperty).ShouldSatisfyAllConditions( // in its field, with no slot
			p => p.Level.ShouldBe(Diagnostics.ValueLevel.Local),
			p => p.Value.ShouldBe(50f));
		Of(FrameworkElement.MarginProperty).ShouldSatisfyAllConditions( // moved into a slot by the style
			p => p.Level.ShouldBe(Diagnostics.ValueLevel.Local),
			p => p.Value.ShouldBe(new Thickness(1f)));
		Of(FrameworkElement.HeightProperty).Level.ShouldBe(Diagnostics.ValueLevel.Style);
		Of(FrameworkElement.MaxWidthProperty).Level.ShouldBe(Diagnostics.ValueLevel.Default);
		border.StoredPropertyIndices().ShouldBe(
			[FrameworkElement.WidthProperty.GlobalIndex, FrameworkElement.HeightProperty.GlobalIndex, FrameworkElement.MarginProperty.GlobalIndex,
				FrameworkElement.StyleProperty.GlobalIndex],
			ignoreOrder: true);
	}

	// --- Registration ---

	[Test]
	public void A_field_backed_property_takes_no_coercion_or_per_type_default()
	{
		Should.Throw<ArgumentException>(() => DependencyProperty.Register("Coerced", new PropertyMetadata<Probe, float>
		{
			Field = static p => ref p.Value,
			Coerce = static (_, v) => v,
		}));
		Should.Throw<ArgumentException>(() => DependencyProperty.Register("PerType", new PropertyMetadata<Probe, float>
		{
			Field = static p => ref p.Value,
			DefaultValueFor = static _ => 1f,
		}));
	}

	private sealed class Probe : DependencyObject
	{
		public float Value;
	}

	// --- Markup ---

	private static (LayoutPropertiesView View, UIHost Host) LayOut()
	{
		var view = new LayoutPropertiesView();
		view.InitializeComponent();
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = view.Root;
		host.Tick(0, 0f, Surface);
		return (view, host);
	}

	[Test]
	public void Markup_styles_set_the_layout_properties_and_local_values_outrank_them()
	{
		var (view, _) = LayOut();

		view.Styled.Width.ShouldBe(120f);
		view.Styled.MaxHeight.ShouldBe(80f);
		view.Styled.Margin.ShouldBe(new Thickness(4f, 6f, 8f, 10f));
		view.Styled.Padding.ShouldBe(new Thickness(3f));
		view.Styled.BorderThickness.ShouldBe(new Thickness(1f));
		view.Styled.VerticalAlignment.ShouldBe(VerticalAlignment.Top);
		view.Styled.Tag.ShouldBe("card");
		view.Styled.LayoutRect.ShouldBe(new Rect(4f, 6f, 120f, 40f));

		view.LocalFirst.Width.ShouldBe(60f);
		view.LocalFirst.HorizontalAlignment.ShouldBe(HorizontalAlignment.Stretch);
		view.LocalAfter.Width.ShouldBe(70f);
		view.LocalAfter.HorizontalAlignment.ShouldBe(HorizontalAlignment.Left);
	}

	[Test]
	public void Markup_Auto_is_a_size_left_to_layout()
	{
		var (view, _) = LayOut();

		view.AutoWidth.Width.ShouldBe(float.NaN);
		view.AutoWidth.Height.ShouldBe(40f); // the rest of the style it is based on
		view.AutoWidth.LayoutRect.Width.ShouldBe(10f); // its border and padding (8), raised to its MinWidth
		view.Tagged.Height.ShouldBe(float.NaN);
	}

	[Test]
	public void Markup_Tag_takes_an_x_Bind() => LayOut().View.Tagged.Tag.ShouldBe("bound");

	[Test]
	public void A_markup_storyboard_animates_width_and_height_through_layout()
	{
		var (view, host) = LayOut();

		VisualStateManager.GoToState(view.Host, "Wide", useTransitions: true).ShouldBeTrue();
		host.Tick(0.25, 0f, Surface);
		view.Grower.Width.ShouldBe(125f, 1e-4f);
		view.Grower.LayoutRect.Width.ShouldBe(125f, 1e-4f);
		view.Grower.LayoutRect.Height.ShouldBe(25f, 1e-4f);

		VisualStateManager.GoToState(view.Host, "Narrow", useTransitions: false).ShouldBeTrue();
		host.Tick(0.25, 0f, Surface);
		view.Grower.LayoutRect.Width.ShouldBe(100f);
		view.Grower.LayoutRect.Height.ShouldBe(10f);
	}

	// --- Generator ---

	private const string CodeBehind = "namespace Demo { public partial class V { } }";

	private static string View(string body) => $$"""
		<StackPanel xmlns="https://github.com/AtomicBlom/SUIsei/ui"
		            xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
		            x:Class="Demo.V">
		  {{body}}
		</StackPanel>
		""";

	private static string Generates(string body)
	{
		var xaml = View(body);
		var result = XamlGeneratorHarness.Run(xaml, CodeBehind);
		result.Diagnostics.ShouldBeEmpty();
		XamlGeneratorHarness.CompileErrors(xaml, CodeBehind).ShouldBeEmpty();
		return result.AllGeneratedText();
	}

	private static string StyleSetting(string property, string value) => $$"""
		<StackPanel.Resources>
		  <Style x:Key="S" TargetType="Border"><Setter Property="{{property}}" Value="{{value}}" /></Style>
		</StackPanel.Resources>
		<Border Style="{StaticResource S}" />
		""";

	[Test]
	[Arguments("Width", "120", "global::SUIsei.FrameworkElement.WidthProperty, Value = 120f")]
	[Arguments("Height", "40", "global::SUIsei.FrameworkElement.HeightProperty, Value = 40f")]
	[Arguments("MinWidth", "10", "global::SUIsei.FrameworkElement.MinWidthProperty, Value = 10f")]
	[Arguments("MinHeight", "5", "global::SUIsei.FrameworkElement.MinHeightProperty, Value = 5f")]
	[Arguments("MaxWidth", "300", "global::SUIsei.FrameworkElement.MaxWidthProperty, Value = 300f")]
	[Arguments("MaxHeight", "80", "global::SUIsei.FrameworkElement.MaxHeightProperty, Value = 80f")]
	[Arguments("Margin", "4,6", "global::SUIsei.FrameworkElement.MarginProperty, Value = new global::SUIsei.Thickness(4f, 6f)")]
	[Arguments("HorizontalAlignment", "Right", "global::SUIsei.FrameworkElement.HorizontalAlignmentProperty, Value = global::SUIsei.HorizontalAlignment.Right")]
	[Arguments("VerticalAlignment", "Center", "global::SUIsei.FrameworkElement.VerticalAlignmentProperty, Value = global::SUIsei.VerticalAlignment.Center")]
	[Arguments("Tag", "first", "global::SUIsei.FrameworkElement.TagProperty, Value = \"first\"")]
	[Arguments("Padding", "3", "global::SUIsei.Controls.Border.PaddingProperty, Value = new global::SUIsei.Thickness(3f)")]
	[Arguments("BorderThickness", "1", "global::SUIsei.Controls.Border.BorderThicknessProperty, Value = new global::SUIsei.Thickness(1f)")]
	[Arguments("Width", "Auto", "global::SUIsei.FrameworkElement.WidthProperty, Value = float.NaN")]
	[Arguments("Height", "auto", "global::SUIsei.FrameworkElement.HeightProperty, Value = float.NaN")]
	public void A_setter_sets_each_layout_property(string property, string value, string expected) =>
		Generates(StyleSetting(property, value)).ShouldContain($"Property = {expected} }}");

	[Test]
	public void Auto_is_NaN_for_Width_and_Height_wherever_markup_sets_them()
	{
		var code = Generates("""
			<Border x:Name="A" Width="Auto" Height=" AUTO " />
			<Border x:Name="B"><Border.Width>Auto</Border.Width></Border>
			<StackPanel.Resources>
			  <Style x:Key="S" TargetType="Border"><Setter Property="Width"><Setter.Value>Auto</Setter.Value></Setter></Style>
			</StackPanel.Resources>
			""");

		code.ShouldContain(".Width = float.NaN;");
		code.ShouldContain(".Height = float.NaN;");
		code.ShouldContain("Property = global::SUIsei.FrameworkElement.WidthProperty, Value = float.NaN }");
	}

	[Test]
	[Arguments("<Border MinWidth=\"Auto\" />")]
	[Arguments("<Border Opacity=\"Auto\" />")]
	[Arguments("<Canvas><Border Canvas.Left=\"Auto\" /></Canvas>")]
	public void Auto_is_not_a_number_anywhere_else(string body) =>
		XamlGeneratorHarness.Run(View(body), CodeBehind).Diagnostics.ShouldHaveSingleItem().Id.ShouldBe("SUISEI012");

	[Test]
	[Arguments("Width")]
	[Arguments("(FrameworkElement.Width)")]
	[Arguments("(FrameworkElement.MaxHeight)")]
	[Arguments("Height")]
	public void A_storyboard_animates_the_sizes(string path)
	{
		var property = path.Trim('(', ')').Replace("FrameworkElement.", "");
		Generates($$"""
			<Border x:Name="Box" Width="10" Height="10" />
			<ContentControl x:Name="Host">
			  <VisualStateManager.VisualStateGroups>
			    <VisualStateGroup Name="G">
			      <VisualState Name="S">
			        <Storyboard>
			          <DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="{{path}}" From="10" To="20" Duration="0:0:1" EnableDependentAnimation="True" />
			        </Storyboard>
			      </VisualState>
			    </VisualStateGroup>
			  </VisualStateManager.VisualStateGroups>
			</ContentControl>
			""").ShouldContain($".TargetProperty = new global::SUIsei.Animation.PropertyPath(global::SUIsei.FrameworkElement.{property}Property);");
	}

	[Test]
	public void A_double_animation_cannot_drive_a_margin()
	{
		var diagnostic = XamlGeneratorHarness.Run(View("""
			<Border x:Name="Box" />
			<ContentControl>
			  <VisualStateManager.VisualStateGroups>
			    <VisualStateGroup Name="G">
			      <VisualState Name="S">
			        <Storyboard>
			          <DoubleAnimation Storyboard.TargetName="Box" Storyboard.TargetProperty="Margin" To="20" />
			        </Storyboard>
			      </VisualState>
			    </VisualStateGroup>
			  </VisualStateManager.VisualStateGroups>
			</ContentControl>
			"""), CodeBehind).Diagnostics.ShouldHaveSingleItem();

		diagnostic.Id.ShouldBe("SUISEI016");
		diagnostic.GetMessage().ShouldContain("is a Thickness property");
	}

	[Test]
	public void Tag_takes_text() => Generates("<Border Tag=\"first tab\" />").ShouldContain(".Tag = \"first tab\";");
}
