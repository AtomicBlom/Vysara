namespace SUIsei.RoseTap;

/// <summary>One property a live edit changed on an element already in the game: what the overlay's change marks show.</summary>
/// <param name="Handle">The element, by its handle, so a mark never keeps the element alive.</param>
/// <param name="Property">The property, as the edit named it.</param>
/// <param name="Old">What it read before the edit.</param>
/// <param name="New">What the edit set, or empty for a cleared value.</param>
/// <param name="At">When the edit was applied.</param>
internal sealed record TapChange(ulong Handle, string Property, string Old, string New, DateTime At);
