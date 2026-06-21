namespace SUIsei;

/// <summary>
/// Converts a value between binding source and target types. Implement this for converter= attributes in XAML
/// bindings; pass a singleton instance via <c>{StaticResource key}</c>.
/// </summary>
public interface IValueConverter
{
	object? Convert(object? value, Type targetType, object? parameter);
	object? ConvertBack(object? value, Type targetType, object? parameter);
}
