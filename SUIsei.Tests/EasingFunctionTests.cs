using SUIsei.Animation;

namespace SUIsei.Tests;

/// <summary>
/// The easing functions against WinUI's curves. WinUI's (microsoft-ui-xaml, <c>EasingFunctions.cpp</c>) are WPF's
/// formulas computed in float, with clamps of their own at the edges of their parameters. The expected values below are
/// WPF's own eases (PresentationCore's <c>System.Windows.Media.Animation</c>) evaluated at the sample times, for parameters
/// where WinUI's clamps do not apply; the clamps are checked one by one after.
/// </summary>
public sealed class EasingFunctionTests
{
	private static readonly double[] Times = [0, 0.1, 0.25, 0.4, 0.5, 0.6, 0.75, 0.9, 1];

	// Each curve's ease at Times in EaseIn, EaseOut and EaseInOut, as WPF computes it.
	private static readonly Dictionary<string, (Func<EasingFunctionBase> Make, double[] In, double[] Out, double[] InOut)> Curves = new()
	{
		["Sine"] = (() => new SineEase(),
			[0, 0.01231165940486223, 0.07612046748871326, 0.19098300562505255, 0.2928932188134524, 0.41221474770752686, 0.6173165676349102, 0.8435655349597692, 1],
			[0, 0.15643446504023084, 0.38268343236508984, 0.5877852522924731, 0.7071067811865476, 0.8090169943749475, 0.9238795325112867, 0.9876883405951378, 1],
			[0, 0.024471741852423234, 0.1464466094067262, 0.34549150281252633, 0.5, 0.6545084971874737, 0.8535533905932737, 0.9755282581475768, 1]),
		["Quadratic"] = (() => new QuadraticEase(),
			[0, 0.010000000000000002, 0.0625, 0.16000000000000003, 0.25, 0.36, 0.5625, 0.81, 1],
			[0, 0.18999999999999995, 0.4375, 0.64, 0.75, 0.84, 0.9375, 0.99, 1],
			[0, 0.020000000000000004, 0.125, 0.32000000000000006, 0.5, 0.6799999999999999, 0.875, 0.98, 1]),
		["Cubic"] = (() => new CubicEase(),
			[0, 0.0010000000000000002, 0.015625, 0.06400000000000002, 0.125, 0.216, 0.421875, 0.7290000000000001, 1],
			[0, 0.2709999999999999, 0.578125, 0.784, 0.875, 0.9359999999999999, 0.984375, 0.999, 1],
			[0, 0.004000000000000001, 0.0625, 0.25600000000000006, 0.5, 0.744, 0.9375, 0.996, 1]),
		["Quartic"] = (() => new QuarticEase(),
			[0, 0.00010000000000000003, 0.00390625, 0.025600000000000008, 0.0625, 0.1296, 0.31640625, 0.6561000000000001, 1],
			[0, 0.3438999999999999, 0.68359375, 0.8704000000000001, 0.9375, 0.9744, 0.99609375, 0.9999, 1],
			[0, 0.0008000000000000003, 0.03125, 0.20480000000000007, 0.5, 0.7951999999999999, 0.96875, 0.9992, 1]),
		["Quintic"] = (() => new QuinticEase(),
			[0, 1.0000000000000004E-05, 0.0009765625, 0.010240000000000004, 0.03125, 0.07776, 0.2373046875, 0.5904900000000002, 1],
			[0, 0.4095099999999998, 0.7626953125, 0.92224, 0.96875, 0.98976, 0.9990234375, 0.99999, 1],
			[0, 0.00016000000000000007, 0.015625, 0.16384000000000007, 0.5, 0.8361599999999999, 0.984375, 0.9998400000000001, 1]),
		["Circle"] = (() => new CircleEase(),
			[0, 0.005012562893380035, 0.031754163448145745, 0.08348486100883201, 0.1339745962155614, 0.19999999999999996, 0.3385621722338523, 0.5641101056459328, 1],
			[0, 0.4358898943540672, 0.6614378277661477, 0.8, 0.8660254037844386, 0.916515138991168, 0.9682458365518543, 0.99498743710662, 1],
			[0, 0.010102051443364402, 0.0669872981077807, 0.20000000000000007, 0.5, 0.7999999999999999, 0.9330127018922193, 0.9898979485566356, 1]),
		["Power"] = (() => new PowerEase(),
			[0, 0.010000000000000002, 0.0625, 0.16000000000000003, 0.25, 0.36, 0.5625, 0.81, 1],
			[0, 0.18999999999999995, 0.4375, 0.64, 0.75, 0.84, 0.9375, 0.99, 1],
			[0, 0.020000000000000004, 0.125, 0.32000000000000006, 0.5, 0.6799999999999999, 0.875, 0.98, 1]),
		["Power 3.5"] = (() => new PowerEase { Power = 3.5 },
			[0, 0.000316227766016838, 0.0078125, 0.04047715405015526, 0.08838834764831845, 0.1673128805561604, 0.36535446722156006, 0.6915901242788246, 1],
			[0, 0.3084098757211754, 0.6346455327784399, 0.8326871194438397, 0.9116116523516815, 0.9595228459498447, 0.9921875, 0.9996837722339832, 1],
			[0, 0.001788854381999832, 0.04419417382415922, 0.2289733608959785, 0.5, 0.7710266391040215, 0.9558058261758408, 0.9982111456180002, 1]),
		["Exponential"] = (() => new ExponentialEase(),
			[0, 0.03465343780550409, 0.1015363240915518, 0.19181877722087762, 0.2689414213699951, 0.3631392316503325, 0.5449457660765887, 0.7903589178467405, 1],
			[0, 0.2096410821532595, 0.45505423392341127, 0.6368607683496674, 0.7310585786300049, 0.8081812227791224, 0.8984636759084482, 0.9653465621944959, 1],
			[0, 0.03848962116043934, 0.13447071068499755, 0.3093596583896596, 0.5, 0.6906403416103404, 0.8655292893150024, 0.9615103788395607, 1]),
		["Exponential 7"] = (() => new ExponentialEase { Exponent = 7 },
			[0, 0.0009252665453504268, 0.004339593630796628, 0.01409654924395531, 0.02931223075135632, 0.05995285058289359, 0.17301983515224917, 0.4961258300228986, 1],
			[0, 0.5038741699771014, 0.8269801648477508, 0.9400471494171064, 0.9706877692486436, 0.9859034507560447, 0.9956604063692034, 0.9990747334546496, 1],
			[0, 0.0013942622780908518, 0.01465611537567816, 0.1229546611260878, 0.5, 0.8770453388739122, 0.9853438846243219, 0.9986057377219091, 1]),
		["Exponential -3"] = (() => new ExponentialEase { Exponent = -3 },
			[0, 0.2727617891635065, 0.5552791692202023, 0.7354202040667792, 0.8175744761936438, 0.8784358578920691, 0.9414740061488345, 0.9816689041034551, 1],
			[0, 0.018331095896544913, 0.058525993851165525, 0.12156414210793087, 0.18242552380635624, 0.2645797959332208, 0.44472083077979774, 0.7272382108364934, 1],
			[0, 0.23741434624078867, 0.4087872380968219, 0.47846225642749174, 0.5, 0.5215377435725083, 0.5912127619031782, 0.7625856537592114, 1]),
		["Back"] = (() => new BackEase(),
			[0, -0.02990169943749474, -0.1611516952966369, -0.3164226065180614, -0.375, -0.3546339097770922, -0.10845508588991071, 0.45088470506254735, 0.9999999999999999],
			[1.1102230246251565E-16, 0.5491152949374527, 1.1084550858899107, 1.354633909777092, 1.375, 1.3164226065180613, 1.161151695296637, 1.0299016994374948, 1],
			[0, -0.05477852522924731, -0.1875, 0.02088589908301075, 0.5, 0.9791141009169892, 1.1875, 1.0547785252292474, 1]),
		["Back 0.4"] = (() => new BackEase { Amplitude = 0.4 },
			[0, -0.011360679774997898, -0.055085678118654766, -0.0881690426072246, -0.07500000000000001, -0.012253563910836901, 0.2097429656440357, 0.617753882025019, 1],
			[0, 0.382246117974981, 0.7902570343559643, 1.012253563910837, 1.075, 1.0881690426072246, 1.0550856781186548, 1.0113606797749979, 1],
			[0, -0.01951141009169893, -0.037500000000000006, 0.16195435963320431, 0.5, 0.8380456403667957, 1.0375, 1.0195114100916989, 1]),
		["Elastic"] = (() => new ElasticEase(),
			[0, 0.016333126039344194, -0.05407096783897328, 0.11561436949957771, -0.12899432494498259, -0.08175965331162097, 0.170187293967067, -0.33015923876735354, 1],
			[0, 1.3301592387673535, 0.829812706032933, 1.081759653311621, 1.1289943249449825, 0.8843856305004223, 1.0540709678389732, 0.9836668739606558, 1],
			[0, -0.017424400570648996, -0.06449716247249129, -0.1543439747432427, 0.5, 1.1543439747432427, 1.0644971624724913, 1.017424400570649, 1]),
		["Elastic 5, 1.5"] = (() => new ElasticEase { Oscillations = 5, Springiness = 1.5 },
			[0, -0.007271313628224621, 0.12073371485561663, 0.13879163151550494, -0.22685491736216903, 0.3391582930332894, -0.2286431980930298, -0.8105967603000923, 1],
			[0, 1.8105967603000923, 1.22864319809303, 0.6608417069667105, 1.226854917362169, 0.8612083684844951, 0.8792662851443833, 1.0072713136282245, 1],
			[0, 0.01552584320262727, -0.11342745868108452, 0.31688101282993125, 0.5, 0.6831189871700687, 1.1134274586810844, 0.9844741567973728, 1]),
		["Bounce"] = (() => new BounceEase(),
			[0, 0.047500000000000014, 0.10937500000000003, 0.45500000000000007, 0.46875000000000006, 0.18000000000000005, 0.52734375, 0.9243750000000002, 1],
			[0, 0.07562499999999983, 0.47265625, 0.82, 0.53125, 0.5449999999999999, 0.890625, 0.9525000000000001, 1],
			[0, 0.12, 0.23437500000000003, 0.3487500000000001, 0.5, 0.6512499999999999, 0.765625, 0.8799999999999999, 1]),
		["Bounce 5, 1.5"] = (() => new BounceEase { Bounces = 5, Bounciness = 1.5 },
			[0, 0.1965972222222222, 0.20598636831275713, 0.42463648834019196, 0.17938703449677396, 0.6441081136005689, 0.2156286516283089, 0.7999002523328084, 0.9999999999999998],
			[2.220446049250313E-16, 0.20009974766719163, 0.7843713483716911, 0.35589188639943115, 0.820612965503226, 0.575363511659808, 0.7940136316872428, 0.8034027777777778, 1],
			[0, 0.1420564700502972, 0.08969351724838698, 0.09980050466561692, 0.5000000000000001, 0.900199495334383, 0.910306482751613, 0.8579435299497028, 1]),
	};

	private static double[] Sample(EasingFunctionBase ease, EasingMode mode)
	{
		ease.EasingMode = mode;
		return [.. Times.Select(ease.Ease)];
	}

	private static double[] Sample(EasingFunctionBase ease) => Sample(ease, EasingMode.EaseIn);

	[Test]
	[Arguments("Sine")]
	[Arguments("Quadratic")]
	[Arguments("Cubic")]
	[Arguments("Quartic")]
	[Arguments("Quintic")]
	[Arguments("Circle")]
	[Arguments("Power")]
	[Arguments("Power 3.5")]
	[Arguments("Exponential")]
	[Arguments("Exponential 7")]
	[Arguments("Exponential -3")]
	[Arguments("Back")]
	[Arguments("Back 0.4")]
	[Arguments("Elastic")]
	[Arguments("Elastic 5, 1.5")]
	[Arguments("Bounce")]
	[Arguments("Bounce 5, 1.5")]
	public void Each_ease_matches_WinUIs_curve_in_each_mode(string curve)
	{
		var (make, easeIn, easeOut, easeInOut) = Curves[curve];

		Sample(make(), EasingMode.EaseIn).ShouldBe(easeIn, 1e-9);
		Sample(make(), EasingMode.EaseOut).ShouldBe(easeOut, 1e-9);
		Sample(make(), EasingMode.EaseInOut).ShouldBe(easeInOut, 1e-9);
		make().EasingMode.ShouldBe(EasingMode.EaseOut); // WinUI's default
	}

	[Test]
	public void The_defaults_are_WinUIs()
	{
		new PowerEase().Power.ShouldBe(2.0);
		new ExponentialEase().Exponent.ShouldBe(2.0);
		new ElasticEase().Oscillations.ShouldBe(3);
		new ElasticEase().Springiness.ShouldBe(3.0);
		new BounceEase().Bounces.ShouldBe(3);
		new BounceEase().Bounciness.ShouldBe(2.0);
	}

	[Test]
	public void A_bounciness_of_one_or_less_is_taken_as_1_01_as_in_WinUI()
	{
		// WPF takes it as 1.001; WinUI's EaseInCore clamps to 1.01, as its composition easing does.
		var clamped = Sample(new BounceEase { Bounciness = 1.01 });
		Sample(new BounceEase { Bounciness = 1 }).ShouldBe(clamped);
		Sample(new BounceEase { Bounciness = 1.00005 }).ShouldBe(clamped); // within WinUI's epsilon of 1
		Sample(new BounceEase { Bounciness = 0.5 }).ShouldBe(clamped);
		Sample(new BounceEase { Bounciness = -2 }).ShouldBe(clamped);
		Sample(new BounceEase { Bounciness = 1.001 }).ShouldNotBe(clamped);
	}

	[Test]
	public void A_negative_count_or_power_is_taken_as_zero()
	{
		Sample(new PowerEase { Power = -1 }).ShouldBe(Sample(new PowerEase { Power = 0 }));
		Sample(new ElasticEase { Oscillations = -2 }).ShouldBe(Sample(new ElasticEase { Oscillations = 0 }));
		Sample(new BounceEase { Bounces = -1 }).ShouldBe(Sample(new BounceEase { Bounces = 0 }));
	}

	[Test]
	public void An_exponent_within_WinUIs_epsilon_of_zero_is_the_straight_line_it_tends_to()
	{
		Sample(new ExponentialEase { Exponent = 0 }).ShouldBe(Times);
		Sample(new ExponentialEase { Exponent = -5e-5 }).ShouldBe(Times);
		Sample(new ElasticEase { Springiness = 0 }).ShouldBe([.. Times.Select(static t => t * Math.Sin(t * (6.0 * Math.PI + Math.PI / 2.0)))]);
	}

	[Test]
	public void A_negative_springiness_is_kept_as_in_WinUI()
	{
		// WPF clamps it to 0; WinUI's envelope takes it as it is.
		var ease = new ElasticEase { Springiness = -2, EasingMode = EasingMode.EaseIn };
		var envelope = (Math.Exp(-2 * 0.3) - 1.0) / (Math.Exp(-2) - 1.0);
		ease.Ease(0.3).ShouldBe(envelope * Math.Sin(0.3 * (6.0 * Math.PI + Math.PI / 2.0)), 1e-12);
	}

	[Test]
	public void A_circle_clamps_its_time_to_where_the_root_is_real()
	{
		var ease = new CircleEase { EasingMode = EasingMode.EaseIn };
		ease.Ease(1.5).ShouldBe(1.0);
		ease.Ease(-1.5).ShouldBe(1.0);
	}

	[Test]
	public void Sine_ease_in_out_follows_smoothstep_three_times_closer_than_quadratic()
	{
		// RubberTrack blends its camera by smoothstep, and a layout morph runs alongside it.
		static double Smoothstep(double t) => t * t * (3.0 - 2.0 * t);
		static double MaxGap(IEasingFunction ease) =>
			Enumerable.Range(0, 101).Select(static i => i / 100.0).Max(t => Math.Abs(ease.Ease(t) - Smoothstep(t)));

		MaxGap(new SineEase { EasingMode = EasingMode.EaseInOut }).ShouldBeLessThan(0.011);
		MaxGap(new QuadraticEase { EasingMode = EasingMode.EaseInOut }).ShouldBeGreaterThan(0.035);
		for (var i = 0; i <= 100; i++)
			new SineEase { EasingMode = EasingMode.EaseInOut }.Ease(i / 100.0).ShouldBe((1.0 - Math.Cos(Math.PI * i / 100.0)) / 2.0, 1e-12);
	}
}
