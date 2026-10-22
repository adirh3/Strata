using System.Collections.Generic;

namespace StrataTheme.Controls;

/// <summary>
/// One section of <see cref="StrataModelPicker"/>'s list, as returned by
/// <see cref="StrataModelPicker.GetModelSections"/>: the pinned items, or one provider's.
/// </summary>
/// <param name="Label">The heading the picker draws over the section ("PINNED", "GITHUB COPILOT").</param>
/// <param name="IsPinned">True for the pinned section, which always comes first.</param>
/// <param name="Models">The section's items in the order the picker lists them.</param>
public sealed record StrataModelSection(string Label, bool IsPinned, IReadOnlyList<object?> Models);
