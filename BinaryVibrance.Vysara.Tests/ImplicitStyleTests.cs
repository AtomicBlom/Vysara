using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Tests.Sample;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Implicit styles, as WinUI's: a style kept with no key styles every element of exactly its target type under the
/// resources it is in, the nearest resources winning, unless the element has a style of its own, which replaces it, and
/// whose going brings the implicit one back. And <c>FontWeight</c>: inherited, set by a style, and choosing among a
/// family's fonts the nearest weight, as CSS does.
/// </summary>
public sealed class ImplicitStyleTests
{
	private static readonly Vector2 Surface = new(400f, 300f);

	private static FontAtlas Atlas()
	{
		using var json = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.font.json")!;
		using var png = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.0.png")!;
		return FontAtlas.Load(json, png);
	}

	private static UIHost Hosted(UIElement content, Action<UIHost>? fonts = null)
	{
		var host = new UIHost();
		fonts?.Invoke(host);
		host.CreateRoot(UILayer.Default).Root.Content = content;
		host.Tick(0, 0f, Surface);
		host.Render();
		return host;
	}

	private static FontAtlas DrawnIn(TextBlock text) => text.Visual.Content.ShouldBeOfType<GlyphRunContent>().Font;

	[Test]
	public void Xaml_styles_every_element_of_the_type_under_it_the_nearest_winning_and_its_own_style_kept()
	{
		var view = new ImplicitStyleView();
		view.InitializeComponent();
		Hosted(view.Root);

		view.Page.FontSize.ShouldBe(18f);
		view.Nested.FontSize.ShouldBe(24f);
		view.Own.FontSize.ShouldBe(9f);
		view.Lit.FontWeight.ShouldBe(FontWeights.Bold);
		view.Lit.FontFamily.ShouldBe(new FontFamily("Label"));
	}

	[Test]
	public void A_toggles_text_takes_the_bold_its_style_sets_and_a_missing_weight_the_nearest()
	{
		var regular = Atlas();
		var bold = Atlas();
		var view = new ImplicitStyleView();
		view.InitializeComponent();
		Hosted(view.Root, host =>
		{
			host.Fonts.Set("Label", regular);
			host.Fonts.Set("Label", FontWeights.Bold, bold);
		});

		var label = view.Lit.GetTemplateRoot().ShouldNotBeNull();
		var text = FindText(label).ShouldNotBeNull();
		text.FontWeight.ShouldBe(FontWeights.Bold);
		DrawnIn(text).ShouldBeSameAs(bold);
		// 900 is over 500: the heaviest held at or over it, else the nearest under, which is bold
		DrawnIn(view.Heavy).ShouldBeSameAs(bold);
	}

	[Test]
	public void A_style_of_its_own_replaces_the_implicit_one_and_its_going_brings_it_back()
	{
		var page = new StackPanel { Resources = new ResourceDictionary() };
		page.Resources.Add(new Style { TargetType = typeof(TextBlock), Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 18f } } });
		var text = new TextBlock { Text = "styled" };
		page.Children.Add(text);
		Hosted(page);
		text.FontSize.ShouldBe(18f);

		text.Style = new Style { TargetType = typeof(TextBlock), Setters = { new Setter { Property = TextBlock.ForegroundProperty, Value = new SolidColorBrush(HdrColor.FromRgb(255, 0, 0)) } } };
		text.FontSize.ShouldBe(14f); // the implicit style's size is gone with it
		text.Foreground.ShouldNotBeNull();

		text.Style = null;
		text.FontSize.ShouldBe(18f);
		text.Foreground.ShouldBeNull();
	}

	[Test]
	public void An_implicit_style_is_for_exactly_its_type()
	{
		var page = new StackPanel { Resources = new ResourceDictionary() };
		page.Resources.Add(new Style { TargetType = typeof(ButtonBase), Setters = { new Setter { Property = Control.FontWeightProperty, Value = FontWeights.Bold } } });
		var button = new Button { Content = "Play" };
		page.Children.Add(button);
		Hosted(page);
		button.FontWeight.ShouldBe(FontWeights.Normal);
	}

	[Test]
	public void A_weight_the_family_lacks_takes_the_nearest_as_css_does()
	{
		var (light, regular, medium, bold) = (Atlas(), Atlas(), Atlas(), Atlas());
		var fonts = new FontCollection();
		fonts.Set("Label", FontWeights.Light, light);
		fonts.Set("Label", regular);
		fonts.Set("Label", FontWeights.Bold, bold);

		FontAtlas? Get(ushort weight) => fonts.TryGet("label", new FontWeight(weight), out var font) ? font : null;
		Get(400).ShouldBeSameAs(regular);
		Get(450).ShouldBeSameAs(regular);   // 400-500: up to 500 first (none), then down
		Get(600).ShouldBeSameAs(bold);      // over 500: up first
		Get(950).ShouldBeSameAs(bold);      // nothing heavier: the nearest lighter
		Get(350).ShouldBeSameAs(light);     // under 400: down first
		Get(100).ShouldBeSameAs(light);     // nothing lighter: the nearest heavier
		fonts.Set("Label", FontWeights.Medium, medium);
		Get(450).ShouldBeSameAs(medium);    // 400-500: up to 500 first

		fonts.Remove("Label", FontWeights.Bold).ShouldBeTrue();
		Get(700).ShouldBeSameAs(medium);
		fonts.TryGet("Nowhere", FontWeights.Bold, out _).ShouldBeFalse();
	}

	[Test]
	public void Weights_are_written_by_name_or_number()
	{
		FontWeights.TryParse("semibold", out var semi).ShouldBeTrue();
		semi.ShouldBe(FontWeights.SemiBold);
		FontWeights.TryParse(" 650 ", out var number).ShouldBeTrue();
		number.Weight.ShouldBe((ushort)650);
		FontWeights.TryParse("heavy", out _).ShouldBeFalse();
		FontWeights.TryParse("0", out _).ShouldBeFalse();
	}

	private static TextBlock? FindText(UIElement element)
	{
		if (element is TextBlock text)
			return text;
		foreach (var child in element.VisualChildren)
			if (FindText(child) is { } found)
				return found;
		return null;
	}
}
