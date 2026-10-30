using System;
using System.Collections.Generic;

namespace StrataTheme.Controls;

/// <summary>
/// Contract a rich model item can implement so <see cref="StrataModelPicker"/> keeps working as a
/// value picker while each row renders host-supplied metadata.
/// </summary>
/// <remarks>
/// <para>Without this interface the picker treats every item as its own value and derives its
/// identity from <c>ToString()</c>. A host that wants badge-style rows binds a collection of view
/// models instead, and the picker uses <see cref="ModelId"/> for identity, grouping and the value it
/// writes back to <see cref="StrataModelPicker.SelectedModel"/> — so the host's selected-model
/// property stays a plain id.</para>
/// <para><see cref="IsPinned"/> items are listed first, under their own section, ahead of the
/// provider groups. Pinning is toggled through <see cref="StrataModelPicker.ModelPinCommand"/>.</para>
/// </remarks>
public interface IStrataModelOption
{
    /// <summary>Stable default selection when choosing this row from a different model.</summary>
    string ModelId { get; }

    /// <summary>Other selection values represented by this row, such as a model's configured variants.</summary>
    IReadOnlyList<string> SelectionAliases => Array.Empty<string>();

    /// <summary>Whether this row represents the selected value, without replacing that value.</summary>
    bool MatchesModelId(string modelId)
    {
        if (string.Equals(ModelId, modelId, StringComparison.Ordinal))
            return true;
        foreach (var alias in SelectionAliases)
            if (string.Equals(alias, modelId, StringComparison.Ordinal))
                return true;
        return false;
    }

    /// <summary>Searchable model name. Defaults to the identifier for existing hosts.</summary>
    string DisplayName => ModelId;

    /// <summary>True when the item should be listed in the picker's pinned section.</summary>
    bool IsPinned { get; }

    /// <summary>Higher values appear first within a provider, before version sorting.
    /// Pinned items keep their original order.</summary>
    int SortPriority => 0;

    /// <summary>Optional host-supplied provider/account label. When unset, grouping is derived
    /// from the model id as before.</summary>
    string? ProviderName => null;
}
