namespace StrataTheme.Controls;

public interface IStrataVirtualizedItem
{
    object? VirtualizationRecycleKey { get; }
    object? VirtualizationMeasureKey { get; }
    double? VirtualizationHeightHint { get; }
}
