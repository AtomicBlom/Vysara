namespace BinaryVibrance.Vysara;

/// <summary>
/// Declares which property of a control receives child elements authored without an explicit property-element
/// wrapper in XAML — the WinUI <c>[ContentProperty(Name = "…")]</c>. Read by <c>BinaryVibrance.Vysara.SourceGen</c>: a single child
/// is assigned to a scalar content property, multiple children are appended to a collection one. Inherited by
/// derived controls unless re-declared.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ContentPropertyAttribute : Attribute
{
	public ContentPropertyAttribute() { }

	public ContentPropertyAttribute(string name) => Name = name;

	/// <summary>The name of the content property (e.g. <c>Children</c>, <c>Child</c>, <c>Content</c>).</summary>
	public string Name { get; set; } = "";
}
