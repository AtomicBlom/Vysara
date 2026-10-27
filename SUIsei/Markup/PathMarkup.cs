using System.Globalization;

namespace SUIsei.Markup;

/// <summary>
/// WinUI's path markup, the move-and-draw command syntax of <c>Path.Data</c>: an optional fill rule (<c>F0</c> even-odd,
/// <c>F1</c> nonzero), then figures, each a move (<c>M</c>) followed by lines (<c>L</c>, <c>H</c>, <c>V</c>), cubic
/// Béziers (<c>C</c>, smooth <c>S</c>), quadratic Béziers (<c>Q</c>, smooth <c>T</c>), elliptical arcs (<c>A</c>) and an
/// optional close (<c>Z</c>). A lowercase command is relative to the current point, and a command's parameters may
/// repeat to draw several of it (after <c>M</c>, further points are lines). Numbers are separated by commas and/or
/// whitespace and must be finite; arc flags are <c>0</c> or <c>1</c>.
/// <para>
/// The grammar is held strictly: markup must start with a move, a close is followed by a move or the end, and anything
/// else is an error naming its character position. This file is compiled into both the runtime and the netstandard2.0
/// XAML generator, so markup in XAML is checked at compile time by the parser the runtime uses.
/// </para>
/// </summary>
internal static class PathMarkup
{
	/// <summary>A one-line summary of the syntax, for a diagnostic that cannot point at one mistake.</summary>
	public const string Syntax = "path markup: an optional F0 or F1, then figures of M, L, H, V, C, S, Q, T, A and Z commands";

	/// <summary>
	/// Parse <paramref name="text"/>, reporting it to <paramref name="sink"/> (null only validates). On failure
	/// <paramref name="error"/> says what was wrong and at which character (1-based); the sink may then have seen part of
	/// the markup.
	/// </summary>
	public static bool TryParse(string text, IPathMarkupSink? sink, out string error)
	{
		var reader = new Reader(text, sink);
		error = reader.Run() ?? "";
		return error.Length == 0;
	}

	private sealed class Reader(string text, IPathMarkupSink? sink)
	{
		private int _pos;
		private double _x, _y;               // the current point
		private double _startX, _startY;     // the current figure's start, where a close returns to
		private double _ctrlX, _ctrlY;       // the last curve's second control point, for S and T
		private char _lastCurve;             // 'C' after C/S, 'Q' after Q/T, else '\0'
		private bool _inFigure;
		private bool _closed;                // the last command closed a figure: only a move may follow

		public string? Run()
		{
			SkipSeparators();
			if (_pos < text.Length && (text[_pos] == 'F' || text[_pos] == 'f'))
			{
				var at = _pos++;
				if (_pos >= text.Length || (text[_pos] != '0' && text[_pos] != '1'))
					return Error(at, "a fill rule is F0 (even-odd) or F1 (nonzero)");
				var nonzero = text[_pos++] == '1';
				sink?.SetFillRule(nonzero);
				if (_pos < text.Length && !IsSeparator(text[_pos]) && !IsCommand(text[_pos]))
					return Error(at, "a fill rule is F0 (even-odd) or F1 (nonzero)");
			}

			SkipSeparators();
			if (_pos >= text.Length)
				return Error(_pos, "the path has no figures; it starts with a move (M)");

			while (true)
			{
				SkipSeparators();
				if (_pos >= text.Length)
					return null;
				var at = _pos;
				var c = text[_pos];
				if (!IsCommand(c))
					return Error(at, IsNumberStart(c) ? "a number with no command before it" : $"'{c}' is not a path command");
				_pos++;
				var command = char.ToUpperInvariant(c);
				var relative = c != command;
				if (command != 'M' && !_inFigure)
					return Error(at, _closed ? $"'{c}' follows a close; a new figure starts with a move (M)" : $"'{c}' comes before any move; a path starts with M");
				var failure = command switch
				{
					'M' => Move(at, c, relative),
					'L' => Repeat(at, c, 2, () => Line(Abs(relative, Num(), true), Abs(relative, Num(), false))),
					'H' => Repeat(at, c, 1, () => Line(relative ? _x + Num() : Num(), _y)),
					'V' => Repeat(at, c, 1, () => Line(_x, relative ? _y + Num() : Num())),
					'C' => Repeat(at, c, 6, () => Cubic(relative, reflect: false)),
					'S' => Repeat(at, c, 4, () => Cubic(relative, reflect: true)),
					'Q' => Repeat(at, c, 4, () => Quadratic(relative, reflect: false)),
					'T' => Repeat(at, c, 2, () => Quadratic(relative, reflect: true)),
					'A' => Repeat(at, c, 7, () => Arc(at, c, relative)),
					_ => Close(),
				};
				if (failure is not null)
					return failure;
			}
		}

		private string? Move(int at, char c, bool relative)
		{
			if (!HasNumbers(2))
				return Error(at, $"'{c}' needs 2 numbers, x and y");
			var x = Abs(relative, Num(), true);
			var y = Abs(relative, Num(), false);
			_x = _startX = x;
			_y = _startY = y;
			_inFigure = true;
			_closed = false;
			_lastCurve = '\0';
			sink?.BeginFigure(x, y);
			// Further pairs after a move are lines, relative when the move was.
			return Repeat(at, c, 2, () => Line(Abs(relative, Num(), true), Abs(relative, Num(), false)), required: false);
		}

		private string? Close()
		{
			sink?.CloseFigure();
			_x = _startX;
			_y = _startY;
			_inFigure = false;
			_closed = true;
			_lastCurve = '\0';
			return null;
		}

		private string? Line(double x, double y)
		{
			sink?.LineTo(x, y);
			_x = x;
			_y = y;
			_lastCurve = '\0';
			return null;
		}

		private string? Cubic(bool relative, bool reflect)
		{
			double x1, y1;
			if (reflect)
			{
				x1 = _lastCurve == 'C' ? 2 * _x - _ctrlX : _x;
				y1 = _lastCurve == 'C' ? 2 * _y - _ctrlY : _y;
			}
			else
			{
				x1 = Abs(relative, Num(), true);
				y1 = Abs(relative, Num(), false);
			}
			var x2 = Abs(relative, Num(), true);
			var y2 = Abs(relative, Num(), false);
			var x = Abs(relative, Num(), true);
			var y = Abs(relative, Num(), false);
			sink?.CubicTo(x1, y1, x2, y2, x, y);
			_ctrlX = x2;
			_ctrlY = y2;
			_x = x;
			_y = y;
			_lastCurve = 'C';
			return null;
		}

		private string? Quadratic(bool relative, bool reflect)
		{
			double x1, y1;
			if (reflect)
			{
				x1 = _lastCurve == 'Q' ? 2 * _x - _ctrlX : _x;
				y1 = _lastCurve == 'Q' ? 2 * _y - _ctrlY : _y;
			}
			else
			{
				x1 = Abs(relative, Num(), true);
				y1 = Abs(relative, Num(), false);
			}
			var x = Abs(relative, Num(), true);
			var y = Abs(relative, Num(), false);
			sink?.QuadraticTo(x1, y1, x, y);
			_ctrlX = x1;
			_ctrlY = y1;
			_x = x;
			_y = y;
			_lastCurve = 'Q';
			return null;
		}

		private string? Arc(int at, char c, bool relative)
		{
			var rx = Num();
			var ry = Num();
			var rotation = Num();
			var largeAt = NextTokenStart();
			var large = Num();
			var sweepAt = NextTokenStart();
			var sweep = Num();
			if (rx < 0 || ry < 0)
				return Error(at, $"'{c}' needs radii of 0 or more");
			if (large is not (0 or 1))
				return Error(largeAt, "an arc's large-arc flag is 0 or 1");
			if (sweep is not (0 or 1))
				return Error(sweepAt, "an arc's sweep-direction flag is 0 or 1");
			var x = Abs(relative, Num(), true);
			var y = Abs(relative, Num(), false);
			sink?.ArcTo(rx, ry, rotation, large == 1, sweep == 1, x, y);
			_x = x;
			_y = y;
			_lastCurve = '\0';
			return null;
		}

		// Run `draw` for each set of `count` numbers that follows: at least one set unless `required` is false. A set cut
		// short is an error at the command.
		private string? Repeat(int at, char c, int count, Func<string?> draw, bool required = true)
		{
			var sets = 0;
			while (true)
			{
				SkipSeparators();
				if (_pos >= text.Length || !IsNumberStart(text[_pos]))
				{
					if (sets == 0 && required)
						return Error(at, $"'{c}' needs {Arity(c)}");
					return null;
				}
				if (!HasNumbers(count))
					return Error(at, $"'{c}' needs {Arity(c)}" + (sets > 0 ? " for each repeat" : ""));
				if (draw() is { } failure)
					return failure;
				sets++;
			}
		}

		private static string Arity(char c) => char.ToUpperInvariant(c) switch
		{
			'M' or 'L' or 'T' => "2 numbers, x and y",
			'H' => "1 number, x",
			'V' => "1 number, y",
			'C' => "6 numbers, two control points and an end point",
			'S' or 'Q' => "4 numbers, a control point and an end point",
			_ => "7 numbers: radius x, radius y, rotation, large-arc flag, sweep flag and an end point",
		};

		private double Abs(bool relative, double value, bool isX) => relative ? value + (isX ? _x : _y) : value;

		// Whether `count` well-formed, finite numbers follow, without consuming them.
		private bool HasNumbers(int count)
		{
			var save = _pos;
			var ok = true;
			for (var i = 0; ok && i < count; i++)
				ok = TryNumber(out _);
			_pos = save;
			return ok;
		}

		// The next number; HasNumbers has already checked that it is there.
		private double Num()
		{
			TryNumber(out var value);
			return value;
		}

		private int NextTokenStart()
		{
			SkipSeparators();
			return _pos;
		}

		// Read one number: an optional sign, digits with an optional fraction, and an optional exponent. Numbers may abut
		// when the next one starts with a sign or a second decimal point ("10-5", "0.5.5").
		private bool TryNumber(out double value)
		{
			value = 0;
			SkipSeparators();
			var start = _pos;
			if (_pos < text.Length && (text[_pos] == '+' || text[_pos] == '-'))
				_pos++;
			var digits = 0;
			while (_pos < text.Length && char.IsDigit(text[_pos])) { _pos++; digits++; }
			if (_pos < text.Length && text[_pos] == '.')
			{
				_pos++;
				while (_pos < text.Length && char.IsDigit(text[_pos])) { _pos++; digits++; }
			}
			if (digits == 0)
			{
				_pos = start;
				return false;
			}
			if (_pos < text.Length && (text[_pos] == 'e' || text[_pos] == 'E'))
			{
				var save = _pos++;
				if (_pos < text.Length && (text[_pos] == '+' || text[_pos] == '-'))
					_pos++;
				var expDigits = 0;
				while (_pos < text.Length && char.IsDigit(text[_pos])) { _pos++; expDigits++; }
				if (expDigits == 0)
					_pos = save;
			}
			if (!double.TryParse(text.Substring(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
				|| double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > float.MaxValue)
			{
				_pos = start;
				return false;
			}
			return true;
		}

		private void SkipSeparators()
		{
			while (_pos < text.Length && IsSeparator(text[_pos]))
				_pos++;
		}

		private static bool IsSeparator(char c) => c is ',' or ' ' or '\t' or '\r' or '\n';

		private static bool IsNumberStart(char c) => char.IsDigit(c) || c is '+' or '-' or '.';

		private static bool IsCommand(char c) => "MmLlHhVvCcSsQqTtAaZz".IndexOf(c) >= 0;

		private static string Error(int at, string message) =>
			"at character " + (at + 1).ToString(CultureInfo.InvariantCulture) + ": " + message;
	}
}
