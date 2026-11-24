using System.Numerics;
using BinaryVibrance.Vysara.Composition;
using BinaryVibrance.Vysara.Controls;
using BinaryVibrance.Vysara.Media;
using BinaryVibrance.Vysara.Tests.Sample;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// <c>FontFamily</c>: text names a font by family, which the host's <see cref="UIHost.Fonts"/> looks up, ignoring case.
/// It is inherited as <c>FontSize</c> is, from XAML as from code; a family the host doesn't hold, or none, draws in the
/// root's <see cref="UIRoot.Font"/>; and a name given another font re-measures the text in it, so a name can stand for a
/// role whose font an app changes while it runs.
/// </summary>
public sealed class FontFamilyTests
{
	private static readonly Vector2 Surface = new(400f, 300f);

	// The default atlas loaded on its own: another font as far as identity goes, the same glyphs
	private static FontAtlas Atlas()
	{
		using var json = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.font.json")!;
		using var png = typeof(FontAtlas).Assembly.GetManifestResourceStream("ui-default.0.png")!;
		return FontAtlas.Load(json, png);
	}

	private static (UIHost Host, UIRoot Root) Hosted(UIElement content)
	{
		var host = new UIHost();
		var root = host.CreateRoot(UILayer.Default).Root;
		root.Content = content;
		host.Tick(0, 0f, Surface);
		host.Render();
		return (host, root);
	}

	private static void Frame(UIHost host)
	{
		host.Tick(0, 0f, Surface);
		host.Render();
	}

	// The font the text was laid out and drawn with
	private static FontAtlas DrawnIn(TextBlock text) => text.Visual.Content.ShouldBeOfType<GlyphRunContent>().Font;

	[Test]
	public void Text_draws_in_the_font_its_family_names_and_without_one_in_the_roots()
	{
		var label = Atlas();
		var titled = new TextBlock { Text = "afterhours", FontFamily = new FontFamily("Label") };
		var plain = new TextBlock { Text = "stopped" };
		var unknown = new TextBlock { Text = "?", FontFamily = new FontFamily("Nowhere") };
		var panel = new StackPanel();
		panel.Children.Add(titled);
		panel.Children.Add(plain);
		panel.Children.Add(unknown);
		var (host, root) = Hosted(panel);
		host.Fonts.Set("label", label);
		Frame(host);

		DrawnIn(titled).ShouldBeSameAs(label);
		DrawnIn(plain).ShouldBeSameAs(root.Font);
		DrawnIn(unknown).ShouldBeSameAs(root.Font);
	}

	[Test]
	public void A_family_set_in_xaml_is_inherited_overridden_and_styled()
	{
		var label = Atlas();
		var readout = Atlas();
		var view = new FontFamilyView();
		view.InitializeComponent();
		var (host, root) = Hosted(view.Root);
		host.Fonts.Set("Label", label);
		host.Fonts.Set("Readout", readout);
		Frame(host);

		view.Panel.FontFamily.ShouldBe(new FontFamily("label"));
		view.Title.FontFamily.ShouldBe(new FontFamily("Label"));
		DrawnIn(view.Title).ShouldBeSameAs(label);
		DrawnIn(view.Own).ShouldBeSameAs(readout);
		DrawnIn(view.Styled).ShouldBeSameAs(readout);
		DrawnIn(view.Tab.GetTemplateRoot().ShouldBeOfType<ContentPresenter>().VisualChildren[0].ShouldBeOfType<TextBlock>()).ShouldBeSameAs(readout);
		DrawnIn(view.Plain).ShouldBeSameAs(root.Font);
	}

	[Test]
	public void A_name_given_another_font_lays_its_text_out_again_and_one_let_go_falls_back()
	{
		var first = Atlas();
		var second = Atlas();
		var text = new TextBlock { Text = "135 bpm", FontFamily = new FontFamily("Readout") };
		var (host, root) = Hosted(text);
		host.Fonts.Set("Readout", first);
		Frame(host);
		DrawnIn(text).ShouldBeSameAs(first);

		host.Fonts.Set("Readout", second);
		Frame(host);
		DrawnIn(text).ShouldBeSameAs(second);

		host.Fonts.Remove("readout").ShouldBeTrue();
		Frame(host);
		DrawnIn(text).ShouldBeSameAs(root.Font);
		host.Fonts.Remove("readout").ShouldBeFalse();
	}

	[Test]
	public void A_family_moves_with_its_text_between_parents()
	{
		var label = Atlas();
		var text = new TextBlock { Text = "moved" };
		var labelled = new ContentControl { FontFamily = new FontFamily("Label") };
		var plain = new StackPanel();
		var panel = new StackPanel();
		panel.Children.Add(labelled);
		panel.Children.Add(plain);
		var (host, root) = Hosted(panel);
		host.Fonts.Set("Label", label);

		labelled.Content = text;
		Frame(host);
		DrawnIn(text).ShouldBeSameAs(label);

		labelled.Content = null;
		plain.Children.Add(text);
		Frame(host);
		text.FontFamily.ShouldBeNull();
		DrawnIn(text).ShouldBeSameAs(root.Font);
	}

	[Test]
	public void A_family_is_a_name_alike_in_any_case()
	{
		new FontFamily(" Label ").Source.ShouldBe("Label");
		new FontFamily("label").ShouldBe(new FontFamily("LABEL"));
		(new FontFamily("Label") == new FontFamily("label")).ShouldBeTrue();
		new FontFamily("Label").GetHashCode().ShouldBe(new FontFamily("LABEL").GetHashCode());
		Should.Throw<ArgumentException>(() => new FontFamily(" "));
	}
}
