using System.Numerics;
using SUIsei.Animation;
using SUIsei.Controls;
using SUIsei.Drawing;
using SUIsei.Media;
using SUIsei.Tests.Sample;

namespace SUIsei.Tests;

/// <summary>
/// The built-in templates paint a control's <see cref="Control.Background"/>, <see cref="Control.BorderBrush"/>,
/// <see cref="Control.BorderThickness"/> and <see cref="Control.Foreground"/> where WinUI's default templates do, live,
/// and show their own built-in look where those are unset. Their visual states show the template's own brushes and
/// hand the background back afterwards, never changing the control's brush; and a XAML template binds brushes with
/// <c>{TemplateBinding}</c> as any other property.
/// </summary>
public sealed class TemplateBindingTests
{
	private readonly SolidColorBrush Red = new(HdrColor.FromRgb(200, 30, 30));
	private readonly SolidColorBrush Gold = new(HdrColor.FromRgb(255, 215, 0));
	private readonly SolidColorBrush Ink = new(HdrColor.FromRgb(10, 10, 10));

	private static UIHost Hosted(UIElement content)
	{
		var host = new UIHost();
		host.CreateRoot(UILayer.Default).Root.Content = content;
		host.Tick(0, 0f, new Vector2(300f, 200f));
		return host;
	}

	// The text a presenter made for string content.
	private static TextBlock TextOf(ContentPresenter presenter) => presenter.VisualChildren[0].ShouldBeOfType<TextBlock>();

	[Test]
	public void A_button_s_template_paints_its_brushes_live_and_its_own_look_where_they_are_unset()
	{
		var button = new Button { Content = "Go" };
		Hosted(button);
		var border = (Border)button.GetTemplateRoot()!;
		var presenter = (ContentPresenter)border.Child!;
		border.ShownColor().ShouldBe(HdrColor.FromRgb(60, 60, 70)); // the built-in look
		TextOf(presenter).Foreground.ShouldBeSameAs(button.Foreground);
		TextOf(presenter).Foreground.SolidColor().ShouldBe(InheritedText.BuiltInForeground); // the built-in look's white

		button.Background = Red;
		button.BorderBrush = Gold;
		button.BorderThickness = new Thickness(2f);
		button.Foreground = Ink;

		border.Background.ShouldBeSameAs(Red);
		border.BorderBrush.ShouldBeSameAs(Gold);
		border.BorderThickness.ShouldBe(new Thickness(2f));
		TextOf(presenter).Foreground.ShouldBeSameAs(Ink);

		button.Background = null;
		border.Background.SolidColor().ShouldBe(HdrColor.FromRgb(60, 60, 70));
	}

	/// <summary>
	/// A template's <c>{TemplateBinding Content}</c> is dropped with the template, as its other template bindings are: once a
	/// control is re-templated, its content reaches the new template's presenter and not the old one, built in or generated.
	/// </summary>
	[Test]
	public void Replacing_a_template_drops_its_content_wire()
	{
		var button = new Button { Content = "one" };
		var host = Hosted(button);
		var old = (ContentPresenter)((Border)button.GetTemplateRoot()!).Child!;
		old.Content.ShouldBe("one");

		button.Template = new ControlTemplate
		{
			Factory = static control =>
			{
				var presenter = new ContentPresenter();
				((ContentControl)control).RegisterContentTemplateBinding(content => presenter.Content = content);
				return presenter;
			},
		};
		host.Tick(0, 0f, new Vector2(300f, 200f));
		button.Content = "two";

		old.Content.ShouldBe("one"); // the detached presenter hears nothing more
		button.GetTemplateRoot().ShouldBeOfType<ContentPresenter>().Content.ShouldBe("two");

		// A generated template's {TemplateBinding Content} goes the same way.
		var view = new InheritedTextView();
		view.InitializeComponent();
		Hosted(view.Root);
		var generated = ((Border)view.Tab.GetTemplateRoot()!).Child.ShouldBeOfType<ContentPresenter>();
		generated.Content.ShouldBe("Track");
		view.Tab.Template = view.StyledTab.Template;
		view.Tab.Content = "Laps";
		generated.Content.ShouldBe("Track");
		view.Tab.GetTemplateRoot().ShouldBeOfType<ContentPresenter>().Content.ShouldBe("Laps");
	}

	/// <summary>As WinUI's default style does, a slider's look gives it its own text size and a foreground of its fill's
	/// colour, so a page's foreground no longer recolours the fill; set on the slider, it does.</summary>
	[Test]
	public void A_slider_keeps_its_own_fill_under_a_page_s_foreground()
	{
		var slider = new Slider { Width = 100f, Value = 50d };
		var page = new ContentControl { Content = slider, Foreground = Gold, FontSize = 22f };
		var host = Hosted(page);
		var fill = (Border)((Panel)slider.GetTemplateRoot()!).Children[1];

		slider.FontSize.ShouldBe(14f);
		slider.Foreground.SolidColor().ShouldBe(HdrColor.FromRgb(76, 140, 224));
		fill.Background.SolidColor().ShouldBe(HdrColor.FromRgb(76, 140, 224));

		slider.Foreground = Red;
		host.Tick(0, 0f, new Vector2(300f, 200f));
		fill.Background.ShouldBeSameAs(Red);
	}

	[Test]
	public void The_built_in_look_s_shared_brushes_cannot_change_and_keep_no_listeners()
	{
		var button = new Button { Content = "Go" };
		Hosted(button);
		var builtIn = ((Border)button.GetTemplateRoot()!).Background.ShouldBeOfType<SolidColorBrush>();

		builtIn.ListenerCount.ShouldBe(0); // shared by every default button on every UI thread, so nothing registers
		Should.Throw<InvalidOperationException>(() => builtIn.Color = HdrColor.FromRgb(1, 2, 3)).Message.ShouldContain("built-in look");
		builtIn.Color.ShouldBe(HdrColor.FromRgb(60, 60, 70));
	}

	[Test]
	public void A_visual_state_shows_the_template_s_brush_and_hands_the_control_s_back_without_changing_it()
	{
		var own = new SolidColorBrush(HdrColor.FromRgb(200, 30, 30));
		var button = new Button { Content = "Go", Background = own };
		var host = Hosted(button);
		var border = (Border)button.GetTemplateRoot()!;
		border.ShownColor().ShouldBe(own.Color);

		VisualStateManager.GoToState(button, "PointerOver", useTransitions: true);
		host.Tick(0.06, 0f, new Vector2(300f, 200f));
		border.ShownColor().ShouldBe(Lerps.Color(own.Color, HdrColor.FromRgb(80, 80, 95), 0.5f), "half way through the 120 ms fade");
		host.Tick(0.1, 0f, new Vector2(300f, 200f));
		border.ShownColor().ShouldBe(HdrColor.FromRgb(80, 80, 95)); // WinUI's rule: the state's own brush, not a tint of Background

		VisualStateManager.GoToState(button, "Normal", useTransitions: true);
		host.Tick(0.2, 0f, new Vector2(300f, 200f));
		border.Background.ShouldBeSameAs(own);
		border.ShownColor().ShouldBe(own.Color);
		own.Color.ShouldBe(HdrColor.FromRgb(200, 30, 30)); // never animated
	}

	[Test]
	public void A_toggle_button_and_menu_items_paint_their_brushes()
	{
		var toggle = new ToggleButton { Content = "T", Background = Red, BorderBrush = Gold, Foreground = Ink };
		var flyoutItem = new MenuFlyoutItem { Text = "F", Background = Red, BorderBrush = Gold, Foreground = Ink };
		var barItem = new MenuBarItem { Title = "B", Background = Red, BorderBrush = Gold, Foreground = Ink };
		var panel = new StackPanel();
		panel.Children.Add(toggle);
		panel.Children.Add(flyoutItem);
		panel.Children.Add(barItem);
		Hosted(panel);

		var toggleRoot = (Grid)toggle.GetTemplateRoot()!;
		var baseBorder = (Border)toggleRoot.Children[0];
		baseBorder.Background.ShouldBeSameAs(Red);
		baseBorder.BorderBrush.ShouldBeSameAs(Gold);
		TextOf((ContentPresenter)toggleRoot.Children[2]).Foreground.ShouldBeSameAs(Ink);

		foreach (var item in new Control[] { flyoutItem, barItem })
		{
			var border = (Border)item.GetTemplateRoot()!;
			border.Background.ShouldBeSameAs(Red);
			border.BorderBrush.ShouldBeSameAs(Gold);
			TextOf((ContentPresenter)border.Child!).Foreground.ShouldBeSameAs(Ink);
		}
	}

	[Test]
	public void A_check_box_paints_its_background_behind_box_and_label_and_its_label_in_its_foreground()
	{
		var checkBox = new CheckBox { Content = "C" };
		Hosted(checkBox);
		var row = (StackPanel)checkBox.GetTemplateRoot()!;
		row.Background.ShouldBeNull();
		row.Visual.Content.ShouldBeNull(); // nothing painted behind it by default, as before

		checkBox.Background = Red;
		checkBox.Foreground = Ink;
		row.Background.ShouldBeSameAs(Red);
		TextOf((ContentPresenter)row.Children[1]).Foreground.ShouldBeSameAs(Ink);
	}

	[Test]
	public void A_slider_s_rail_paints_its_background_and_its_fill_its_foreground()
	{
		var slider = new Slider { Width = 100f };
		Hosted(slider);
		var track = (Panel)slider.GetTemplateRoot()!;
		var rail = (Border)track.Children[0];
		var fill = (Border)track.Children[1];
		rail.Background.SolidColor().ShouldBe(HdrColor.FromRgb(70, 74, 86));
		fill.Background.SolidColor().ShouldBe(HdrColor.FromRgb(76, 140, 224));

		slider.Background = Red;
		slider.Foreground = Gold;
		rail.Background.ShouldBeSameAs(Red);
		fill.Background.ShouldBeSameAs(Gold);
	}

	[Test]
	public void A_list_item_shows_its_selection_and_then_its_own_background_again()
	{
		var item = new ListViewItem { Content = "Row", Background = Red, Width = 100f, Height = 20f };
		var host = Hosted(item);
		item.ShownColor().ShouldBe(Red.Color);

		item.SetSelected(true);
		host.Tick(0.1, 0f, new Vector2(300f, 200f));
		item.ShownColor().ShouldBe(HdrColor.FromRgb(50, 90, 160));
		item.Background.ShouldNotBeSameAs(Red); // the selection holds the background while selected

		item.SetSelected(false);
		host.Tick(0.1, 0f, new Vector2(300f, 200f));
		item.Background.ShouldBeSameAs(Red);
		item.ShownColor().ShouldBe(Red.Color);
	}

	[Test]
	public void Foreground_reaches_the_text_a_control_or_presenter_makes_for_string_content()
	{
		var control = new ContentControl { Content = "Plain" };
		var presenter = new ContentPresenter { Content = "Presented" };
		var panel = new StackPanel();
		panel.Children.Add(control);
		panel.Children.Add(presenter);
		Hosted(panel);

		control.Foreground = Gold;
		presenter.Foreground = Ink;
		control.VisualChildren[0].ShouldBeOfType<TextBlock>().Foreground.ShouldBeSameAs(Gold);
		TextOf(presenter).Foreground.ShouldBeSameAs(Ink);

		control.Content = "Again"; // a new text block takes the foreground too
		control.VisualChildren[0].ShouldBeOfType<TextBlock>().Foreground.ShouldBeSameAs(Gold);
		var element = new TextBlock { Text = "Inherits" };
		control.Content = element;
		element.Foreground.ShouldBeSameAs(Gold); // text given as element content inherits it too, as in WinUI
		var own = new TextBlock { Text = "Own", Foreground = Ink };
		control.Content = own;
		own.Foreground.ShouldBeSameAs(Ink); // unless it sets its own
	}

	[Test]
	public void A_text_box_paints_its_brushes_and_its_own_look_where_they_are_unset()
	{
		var box = new TextBox { Text = "Hi", Width = 100f };
		Hosted(box);
		var chrome = box.Visual.Content.ShouldBeOfType<RectContent>();
		chrome.Fill.SolidColor().ShouldBe(HdrColor.FromRgb(30, 30, 38));
		chrome.BorderBrush.SolidColor().ShouldBe(HdrColor.FromRgb(90, 90, 105));

		box.Background = Red;
		box.BorderBrush = Gold;
		box.Foreground = Ink;
		chrome.Fill.ShouldBeSameAs(Red.CompositionBrush);
		chrome.BorderBrush.ShouldBeSameAs(Gold.CompositionBrush);
		box.VisualChildren[1].ShouldBeOfType<TextBlock>().Foreground.ShouldBeSameAs(Ink);
	}

	[Test]
	public void A_XAML_template_binds_brushes_with_TemplateBinding()
	{
		var view = new TemplatedView();
		view.InitializeComponent();
		view.Card.Content = "Card";
		var root = new UIRoot(UISurface.Hud) { Content = view.Root };
		root.LayoutPass(new Size(200f, 120f));

		var border = (Border)view.Card.GetTemplateRoot()!;
		border.Background.ShouldBeSameAs(view.Card.Background);
		border.BorderBrush.ShouldBeSameAs(view.Card.BorderBrush);
		TextOf((ContentPresenter)border.Child!).Foreground.ShouldBeSameAs(view.Card.Foreground);

		var teal = new SolidColorBrush(HdrColor.FromRgb(0, 128, 128));
		view.Card.Background = teal; // live
		border.Background.ShouldBeSameAs(teal);
	}

	/// <summary>
	/// A value a template sets on a part sits at the templated-parent level, as in WinUI, whether written plainly or
	/// bound: below the part's local value, so ClearValue on the part goes back to the template's value. Both the
	/// generated templates and the built-in ones set their values there.
	/// </summary>
	[Test]
	public void A_templates_values_sit_on_the_template_level_below_the_parts_own()
	{
		var view = new TemplatedView();
		view.InitializeComponent();
		var root = new UIRoot(UISurface.Hud) { Content = view.Root };
		root.LayoutPass(new Size(200f, 120f));
		var border = (Border)view.Card.GetTemplateRoot()!;

		border.TopRung(Border.CornerRadiusProperty).ShouldBe(DependencyObject.ValueRung.Template); // CornerRadius="4"
		border.TopRung(Border.BackgroundProperty).ShouldBe(DependencyObject.ValueRung.Template); // {TemplateBinding}
		border.CornerRadius = new CornerRadius(9f);
		border.ClearValue(Border.CornerRadiusProperty);
		border.CornerRadius.ShouldBe(new CornerRadius(4f)); // back to the template's, not the default

		var button = new Button();
		var buttonRoot = new UIRoot(UISurface.Hud) { Content = button };
		buttonRoot.LayoutPass(new Size(200f, 120f));
		var chrome = (Border)button.GetTemplateRoot()!;
		chrome.TopRung(Border.BackgroundProperty).ShouldBe(DependencyObject.ValueRung.Template);
		chrome.TopRung(Border.BorderThicknessProperty).ShouldBe(DependencyObject.ValueRung.Template);
	}
}
