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
    /// <summary>Stable model identifier. Also the value the picker assigns when the row is chosen.</summary>
    string ModelId { get; }

    /// <summary>True when the item should be listed in the picker's pinned section.</summary>
    bool IsPinned { get; }
}
