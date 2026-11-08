using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Media;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// <c>FontSize</c> and <c>Foreground</c> are inherited as in WinUI: a <see cref="Control"/>, a <see cref="ContentPresenter"/>
/// or a <see cref="TextBlock"/> takes the values of the nearest one above it, through elements that hold neither, unless
/// it has its own (local, from a style or animated), and passes its values on. Re-parenting and an ancestor's change
/// resolve them again, and inheriting costs a steady frame nothing.
/// </summary>
public sealed class InheritedTextTests
{
	private readonly SolidColorBrush Gold = new(HdrColor.FromRgb(255, 215, 0));
	private readonly SolidColorBrush Ink = new(HdrColor.FromRgb(10, 10, 10));

	private static readonly float LineHeight = FontAtlas.Default.LineHeight;

	private static UIHost Hosted(UIElement content)
	{
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = content;
		host.Tick(0, 0f, new Vector2(300f, 300f));
		return host;
	}

	// A content control templated as WinUI's ContentControl is: a border hosting a presenter of its content.
	private static ContentControl Templated(object content) => new()
	{
		Content = content,
		Template = new ControlTemplate
		{
			Factory = static control =>
			{
				var host = (ContentControl)control;
				var presenter = new ContentPresenter();
				host.RegisterContentTemplateBinding(content => presenter.Content = content);
				return new Border { Child = presenter };
			},
		},
	};

	private static ContentPresenter PresenterOf(Control control) => ((Border)control.GetTemplateRoot()!).Child.ShouldBeOfType<ContentPresenter>();

	// The text a presenter made for string content.
	private static TextBlock TextOf(ContentPresenter presenter) => presenter.VisualChildren[0].ShouldBeOfType<TextBlock>();

	// An animated colour lands within float rounding of where it is going.
	private static void ShouldBeNear(HdrColor actual, HdrColor expected)
	{
		actual.R.ShouldBe(expected.R, 1e-5f);
		actual.G.ShouldBe(expected.G, 1e-5f);
		actual.B.ShouldBe(expected.B, 1e-5f);
		actual.A.ShouldBe(expected.A, 1e-5f);
	}

	[Test]
	public void A_templated_control_sizes_and_paints_its_string_content()
	{
		var tab = Templated("Track");
		tab.FontSize = 20f; // set before the template is realized, which happens at the first layout
		var host = Hosted(tab);
		var presenter = PresenterOf(tab);
		var label = TextOf(presenter);

		presenter.FontSize.ShouldBe(20f);
		label.FontSize.ShouldBe(20f);
		label.DesiredSize.Height.ShouldBe(LineHeight * 20f, 1e-3f);
		label.Foreground.ShouldBeNull();

		tab.Foreground = Gold;
		tab.FontSize = 24f;
		presenter.Foreground.ShouldBeSameAs(Gold);
		label.Foreground.ShouldBeSameAs(Gold);
		label.ShownColor().ShouldBe(Gold.Color);
		host.Tick(0, 0f, new Vector2(300f, 300f));
		label.DesiredSize.Height.ShouldBe(LineHeight * 24f, 1e-3f); // the change re-measured the text

		tab.Content = "Again"; // a new text block takes them as it joins
		TextOf(presenter).FontSize.ShouldBe(24f);
		TextOf(presenter).Foreground.ShouldBeSameAs(Gold);

		tab.ClearValue(Control.FontSizeProperty);
		tab.ClearValue(Control.ForegroundProperty);
		TextOf(presenter).FontSize.ShouldBe(14f);
		TextOf(presenter).Foreground.ShouldBeNull();
	}

	[Test]
	public void Values_pass_through_plain_panels_to_element_content()
	{
		var direct = new TextBlock { Text = "Direct" };
		var bordered = new TextBlock { Text = "Bordered" };
		var deep = new TextBlock { Text = "Deep" };
		var canvas = new Canvas();
		canvas.Children.Add(deep);
		var grid = new Grid();
		grid.Children.Add(canvas);
		var panel = new StackPanel();
		panel.Children.Add(direct);
		panel.Children.Add(new Border { Child = bordered });
		panel.Children.Add(grid);
		var card = new ContentControl { Content = panel, FontSize = 18f, Foreground = Gold };
		var loose = new TextBlock { Text = "Loose" };
		var page = new StackPanel();
		page.Children.Add(card);
		page.Children.Add(loose);
		Hosted(page);

		foreach (var text in new[] { direct, bordered, deep })
		{
			text.FontSize.ShouldBe(18f);
			text.Foreground.ShouldBeSameAs(Gold);
		}
		// Below nothing that holds a value, text keeps its defaults, and holds no value for them at all.
		loose.FontSize.ShouldBe(14f);
		loose.Foreground.ShouldBeNull();
		loose.HasAnyValue(TextBlock.FontSizeProperty).ShouldBeFalse();
		loose.HasAnyValue(TextBlock.ForegroundProperty).ShouldBeFalse();
	}

	/// <summary>
	/// As WinUI's default styles do, a built-in control's look sets its own text size and brush, which outrank what it
	/// would inherit: a page's text properties reach its text, not its buttons and text boxes. Set on the control itself,
	/// they reach the control's own text.
	/// </summary>
	[Test]
	public void Built_in_controls_keep_their_own_text_and_pass_what_is_set_on_them_to_their_content()
	{
		var button = new Button { Content = "Go" };
		var box = new TextBox { Text = "Typed" };
		var plain = new TextBlock { Text = "Label" };
		var panel = new StackPanel();
		panel.Children.Add(button);
		panel.Children.Add(box);
		panel.Children.Add(plain);
		var page = new ContentControl { Content = panel, FontSize = 22f, Foreground = Gold };
		var host = Hosted(page);

		plain.FontSize.ShouldBe(22f);
		plain.Foreground.ShouldBeSameAs(Gold);
		button.FontSize.ShouldBe(14f);
		button.Foreground.SolidColor().ShouldBe(InheritedText.BuiltInForeground);
		box.FontSize.ShouldBe(14f);
		var label = TextOf(PresenterOf(button));
		label.FontSize.ShouldBe(14f);

		button.FontSize = 26f;
		button.Foreground = Gold;
		host.Tick(0, 0f, new Vector2(300f, 300f));
		label.FontSize.ShouldBe(26f);
		label.Foreground.ShouldBeSameAs(Gold); // through the button's own {TemplateBinding Foreground} as well
		label.DesiredSize.Height.ShouldBe(LineHeight * 26f, 1e-3f);
		button.ClearValue(Control.FontSizeProperty);
		host.Tick(0, 0f, new Vector2(300f, 300f));
		label.FontSize.ShouldBe(14f); // back to the built-in look's, not the page's
	}

	[Test]
	public void Local_style_and_animated_values_win_over_inherited_ones()
	{
		var plain = new TextBlock { Text = "Plain" };
		var local = new TextBlock { Text = "Local", FontSize = 10f, Foreground = Ink };
		var styled = new TextBlock { Text = "Styled", Style = new Style { Setters = { new Setter { Property = TextBlock.FontSizeProperty, Value = 12f } } } };
		var animated = new TextBlock { Text = "Animated" };
		var panel = new StackPanel();
		foreach (var text in new[] { plain, local, styled, animated })
			panel.Children.Add(text);
		var presenter = new ContentPresenter { Content = panel, FontSize = 20f, Foreground = Gold };
		var host = Hosted(presenter);

		plain.FontSize.ShouldBe(20f);
		local.FontSize.ShouldBe(10f);
		local.Foreground.ShouldBeSameAs(Ink);
		styled.FontSize.ShouldBe(12f);
		styled.Foreground.ShouldBeSameAs(Gold); // the style sets only the size

		styled.FontSize = 11f;
		styled.FontSize.ShouldBe(11f); // local over style
		styled.ClearValue(TextBlock.FontSizeProperty);
		styled.FontSize.ShouldBe(12f);
		styled.Style = null;
		styled.FontSize.ShouldBe(20f);
		local.ClearValue(TextBlock.FontSizeProperty);
		local.FontSize.ShouldBe(20f);

		var grow = new Storyboard
		{
			Children = { new DoubleAnimation { From = 40.0, To = 50.0, Duration = TimeSpan.FromSeconds(1), Target = animated, TargetProperty = TextBlock.FontSizeProperty } },
		};
		host.Clock.Begin(grow);
		host.Tick(0.1, 0f, new Vector2(300f, 300f));
		var size = animated.FontSize;
		size.ShouldBeInRange(40f, 50f);
		presenter.FontSize = 30f;
		animated.FontSize.ShouldBe(size); // the running animation wins over the inherited value
		plain.FontSize.ShouldBe(30f);
		host.Clock.Stop(grow);
		animated.FontSize.ShouldBe(30f);
	}

	[Test]
	public void An_element_with_its_own_value_passes_that_on_and_stops_its_ancestors()
	{
		var outerText = new TextBlock { Text = "Outer" };
		var innerText = new TextBlock { Text = "Inner" };
		var inner = Templated(innerText);
		var panel = new StackPanel();
		panel.Children.Add(outerText);
		panel.Children.Add(inner);
		var outer = new ContentControl { Content = panel, FontSize = 20f, Foreground = Gold };
		Hosted(outer);

		innerText.FontSize.ShouldBe(20f); // through the inner control and its presenter

		PresenterOf(inner).FontSize = 12f;
		innerText.FontSize.ShouldBe(12f);
		innerText.Foreground.ShouldBeSameAs(Gold); // each property is inherited on its own

		outer.FontSize = 30f;
		outerText.FontSize.ShouldBe(30f);
		inner.FontSize.ShouldBe(30f);
		innerText.FontSize.ShouldBe(12f); // the presenter's own value stands between

		PresenterOf(inner).ClearValue(ContentPresenter.FontSizeProperty);
		innerText.FontSize.ShouldBe(30f);
	}

	[Test]
	public void Re_parenting_resolves_the_values_again()
	{
		var text = new TextBlock { Text = "Moved" };
		var subtree = new Border { Child = text };
		var first = new ContentPresenter { FontSize = 20f, Foreground = Gold };
		var second = new ContentPresenter { Foreground = Ink };
		var plain = new StackPanel();
		var page = new StackPanel();
		page.Children.Add(first);
		page.Children.Add(second);
		page.Children.Add(plain);
		var card = new ContentControl { Content = page, FontSize = 16f };
		Hosted(card);

		first.Content = subtree;
		text.FontSize.ShouldBe(20f);
		text.Foreground.ShouldBeSameAs(Gold);

		first.Content = null;
		text.FontSize.ShouldBe(14f); // out of the tree it gives up what it took
		text.Foreground.ShouldBeNull();

		second.Content = subtree;
		text.FontSize.ShouldBe(16f); // the second presenter passes on the card's size with its own brush
		text.Foreground.ShouldBeSameAs(Ink);

		second.Content = null;
		plain.Children.Add(subtree);
		text.FontSize.ShouldBe(16f);
		text.Foreground.ShouldBeNull();

		card.FontSize = 18f; // an ancestor's change reaches it where it is now
		text.FontSize.ShouldBe(18f);

		plain.Children.RemoveAt(0);
		text.FontSize.ShouldBe(14f);
		text.HasAnyValue(TextBlock.FontSizeProperty).ShouldBeFalse();
	}

	[Test]
	public void A_state_animating_the_presenters_foreground_recolours_string_content()
	{
		var view = new InheritedTextView();
		view.InitializeComponent();
		var host = Hosted(view.Root);
		var label = TextOf(PresenterOf(view.Tab));
		var dim = HdrColor.FromRgb(0x60, 0x60, 0x60);

		label.Text.ShouldBe("Track");
		label.FontSize.ShouldBe(20f); // the presenter's, from the template
		label.DesiredSize.Height.ShouldBe(LineHeight * 20f, 1e-3f);
		label.ShownColor().ShouldBe(dim);

		VisualStateManager.GoToState(view.Tab, "Lit", useTransitions: true);
		host.Tick(0.1, 0f, new Vector2(300f, 300f));
		label.ShownColor().R.ShouldBeInRange(dim.R + 0.1f, 0.9f); // on its way
		host.Tick(0.2, 0f, new Vector2(300f, 300f));
		ShouldBeNear(label.ShownColor(), HdrColor.FromRgb(255, 255, 255));
		label.Foreground.ShouldBeSameAs(PresenterOf(view.Tab).Foreground); // the presenter's brush, recoloured in place

		VisualStateManager.GoToState(view.Tab, "Dim", useTransitions: true);
		host.Tick(0.3, 0f, new Vector2(300f, 300f));
		ShouldBeNear(label.ShownColor(), dim);

		// The other tab shares the template, from the resources, but each realization has a brush of its own.
		view.OtherTab.Template.ShouldBeSameAs(view.Tab.Template);
		var other = TextOf(PresenterOf(view.OtherTab));
		other.FontSize.ShouldBe(20f);
		VisualStateManager.GoToState(view.OtherTab, "Lit", useTransitions: true);
		host.Tick(0.3, 0f, new Vector2(300f, 300f));
		ShouldBeNear(other.ShownColor(), HdrColor.FromRgb(255, 255, 255));
		ShouldBeNear(label.ShownColor(), dim);

		// A style's template: its presenter sizes the label and passes on the tab's own brush.
		var styled = TextOf(view.StyledTab.GetTemplateRoot().ShouldBeOfType<ContentPresenter>());
		styled.Text.ShouldBe("Laps");
		styled.FontSize.ShouldBe(16f);
		styled.Foreground.ShouldBeSameAs(view.StyledTab.Foreground);

		// The card passes its size and brush through its panel and a border; its own and its styled text keep theirs.
		view.Title.FontSize.ShouldBe(18f);
		view.Title.Foreground.SolidColor().ShouldBe(HdrColor.FromRgb(255, 215, 0));
		view.Nested.FontSize.ShouldBe(18f);
		view.Nested.Foreground.ShouldBeSameAs(view.Title.Foreground);
		view.Own.FontSize.ShouldBe(11f);
		view.Own.Foreground.SolidColor().ShouldBe(HdrColor.FromRgb(0, 128, 128));
		view.Own.TextAlignment.ShouldBe(TextAlignment.Right); // End, from markup, is Right
		view.Styled.FontSize.ShouldBe(9f);
		view.Styled.Foreground.ShouldBeSameAs(view.Title.Foreground);
	}

	/// <summary>
	/// A template whose presenter takes the control's own brush and animates
	/// <c>(ContentPresenter.Foreground).(SolidColorBrush.Color)</c>, as WinUI templates do: each built-in control's look gives
	/// it a brush of its own, so the state recolours that control's text and no other's.
	/// </summary>
	[Test]
	[Arguments(true)]
	[Arguments(false)]
	public void A_state_animating_a_built_in_control_s_foreground_recolours_only_that_control(bool useTransitions)
	{
		var lit = HdrColor.FromRgb(255, 215, 0);
		var template = new ControlTemplate
		{
			Factory = control =>
			{
				var host = (ContentControl)control;
				var presenter = new ContentPresenter { Content = host.Content };
				host.RegisterTemplateBinding(Control.ForegroundProperty, presenter, ContentPresenter.ForegroundProperty);
				var border = new Border { Child = presenter };
				var emphasis = new VisualStateGroup { Name = "Emphasis" };
				emphasis.States.Add(new VisualState
				{
					Name = "Lit",
					Storyboard = new Storyboard
					{
						Children =
						{
							new ColorAnimation
							{
								To = lit, Duration = TimeSpan.FromSeconds(0.2), Target = presenter,
								TargetProperty = new PropertyPath(ContentPresenter.ForegroundProperty, SolidColorBrush.ColorProperty),
							},
						},
					},
				});
				VisualStateManager.SetVisualStateGroups(border, [emphasis]);
				return border;
			},
		};
		var button = new Button { Content = "Go", Template = template };
		var other = new Button { Content = "Stay", Template = template };
		var panel = new StackPanel();
		panel.Children.Add(button);
		panel.Children.Add(other);
		var host = Hosted(panel);
		var label = TextOf(PresenterOf(button));
		label.ShownColor().ShouldBe(HdrColor.FromRgb(255, 255, 255));

		VisualStateManager.GoToState(button, "Lit", useTransitions).ShouldBeTrue();
		for (var i = 0; i < 4; i++)
			host.Tick(0.1, 0f, new Vector2(300f, 300f));

		ShouldBeNear(label.ShownColor(), lit);
		ShouldBeNear(button.Foreground.SolidColor(), lit); // the button's own brush, recoloured in place
		TextOf(PresenterOf(other)).ShownColor().ShouldBe(HdrColor.FromRgb(255, 255, 255));
		other.Foreground.SolidColor().ShouldBe(HdrColor.FromRgb(255, 255, 255));
		new Button().Foreground.SolidColor().ShouldBe(HdrColor.FromRgb(255, 255, 255));
	}

	[Test]
	public void Recolouring_and_resizing_inherited_text_every_frame_allocates_nothing()
	{
		var view = new InheritedTextView();
		view.InitializeComponent();
		var host = Hosted(view.Root);
		var label = TextOf(PresenterOf(view.Tab));
		var tweenLength = TimeSpan.FromSeconds(4.0 / 60.0);

		const int warm = 32;
		const int measured = 64;
		for (var i = 0; i < warm; i++)
			Frame(i);
		var shaped = view.Title.Visual.Content.ShouldBeOfType<GlyphRunContent>().Shape();

		Allocations.ShouldAllocateNothing(() =>
		{
			for (var i = 0; i < measured; i++)
				Frame(i);
		});

		// The tab's state recolours its label, and the card's size reaches its text and re-measures it, with no reshaping.
		Frame(0);
		Frame(1);
		host.Tick(0, 0f, new Vector2(300f, 300f));
		label.ShownColor().ShouldNotBe(HdrColor.FromRgb(0x60, 0x60, 0x60));
		view.Title.FontSize.ShouldBeInRange(18f, 30f);
		view.Title.FontSize.ShouldNotBe(18f);
		view.Own.FontSize.ShouldBe(11f);
		view.Title.DesiredSize.Height.ShouldBe(LineHeight * view.Title.FontSize, 1e-3f);
		view.Title.Visual.Content.ShouldBeOfType<GlyphRunContent>().Shape().ShouldBeSameAs(shaped);

		void Frame(int i)
		{
			if (i % 8 == 0)
			{
				var forward = i % 16 == 0;
				VisualStateManager.GoToState(view.Tab, forward ? "Lit" : "Dim", useTransitions: true);
				host.Clock.Tween(view.Card, Control.FontSizeProperty, forward ? 30f : 18f, tweenLength);
			}
			host.Tick(1.0 / 60.0, 0f, new Vector2(300f, 300f));
			host.Render();
		}
	}
}
