namespace Vysara.Extensions;

/// <summary>Which tracks a <see cref="GridSplitter"/> resizes: worked out from its shape (wider than tall, rows), rows,
/// or columns.</summary>
public enum GridResizeDirection
{
	Auto,
	Rows,
	Columns,
}
