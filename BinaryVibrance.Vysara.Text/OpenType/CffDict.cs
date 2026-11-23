using System.Globalization;
using System.Text;

namespace BinaryVibrance.Vysara.Text.OpenType;

/// <summary>
/// A parsed CFF DICT (top dict / private dict): a map of operator → operand list. Operators ≥ 1200 are the
/// two-byte (escape) operators (1200 + second byte).
/// </summary>
internal sealed class CffDict
{
	private readonly Dictionary<int, double[]> _entries;

	private CffDict(Dictionary<int, double[]> entries) => _entries = entries;

	public bool TryGet(int op, out double[] operands) => _entries.TryGetValue(op, out operands!);

	public double GetSingle(int op, double fallback) =>
		_entries.TryGetValue(op, out var v) && v.Length > 0 ? v[0] : fallback;

	public static CffDict Parse(ReadOnlySpan<byte> data)
	{
		var entries = new Dictionary<int, double[]>();
		var operands = new List<double>();
		var i = 0;
		while (i < data.Length)
		{
			var b0 = data[i];
			if (b0 <= 21) // operator
			{
				int op = b0;
				i++;
				if (b0 == 12)
				{
					op = 1200 + data[i];
					i++;
				}

				entries[op] = operands.ToArray();
				operands.Clear();
			}
			else if (b0 == 28)
			{
				operands.Add((short)((data[i + 1] << 8) | data[i + 2]));
				i += 3;
			}
			else if (b0 == 29)
			{
				operands.Add((data[i + 1] << 24) | (data[i + 2] << 16) | (data[i + 3] << 8) | data[i + 4]);
				i += 5;
			}
			else if (b0 == 30) // real number, nibble-encoded
			{
				i++;
				var sb = new StringBuilder();
				var done = false;
				while (i < data.Length && !done)
				{
					var b = data[i++];
					done = AppendNibble(sb, b >> 4) || AppendNibble(sb, b & 0xF);
				}

				operands.Add(double.TryParse(sb.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0);
			}
			else if (b0 is >= 32 and <= 246)
			{
				operands.Add(b0 - 139);
				i++;
			}
			else if (b0 is >= 247 and <= 250)
			{
				operands.Add((b0 - 247) * 256 + data[i + 1] + 108);
				i += 2;
			}
			else if (b0 is >= 251 and <= 254)
			{
				operands.Add(-(b0 - 251) * 256 - data[i + 1] - 108);
				i += 2;
			}
			else
			{
				i++; // reserved (255, 22..27 outside the above) — skip defensively
			}
		}

		return new CffDict(entries);
	}

	/// <summary>Append a real-number nibble; returns true when the 0xF terminator is hit.</summary>
	private static bool AppendNibble(StringBuilder sb, int nibble)
	{
		switch (nibble)
		{
			case <= 9: sb.Append((char)('0' + nibble)); return false;
			case 0xa: sb.Append('.'); return false;
			case 0xb: sb.Append('E'); return false;
			case 0xc: sb.Append("E-"); return false;
			case 0xe: sb.Append('-'); return false;
			case 0xf: return true;
			default: return false; // 0xd reserved
		}
	}
}
