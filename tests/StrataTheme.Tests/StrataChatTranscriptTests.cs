using Avalonia.Controls;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

public class StrataChatTranscriptTests
{
    [Fact]
    public void NeedsContainerOverride_UsesVirtualizedRecycleKey()
    {
        var transcript = new TestTranscript();
        var item = new TestVirtualizedItem
        {
            VirtualizationRecycleKey = "assistant-message",
            VirtualizationMeasureKey = "message:1",
            VirtualizationHeightHint = 420d,
        };

        var (needsContainer, recycleKey) = transcript.GetContainerDecision(item, 0);

        Assert.True(needsContainer);
        Assert.Equal("assistant-message", recycleKey);
    }

    [Fact]
    public void NeedsContainerOverride_FallsBackToItemType_WhenNoVirtualizationMetadataExists()
    {
        var transcript = new TestTranscript();
        var item = new PlainTranscriptItem();

        var (needsContainer, recycleKey) = transcript.GetContainerDecision(item, 0);

        Assert.True(needsContainer);
        Assert.Equal(typeof(PlainTranscriptItem), recycleKey);
    }

    [Fact]
    public void NeedsContainerOverride_DoesNotWrapControls()
    {
        var transcript = new TestTranscript();
        var control = new Border();

        var (needsContainer, recycleKey) = transcript.GetContainerDecision(control, 0);

        Assert.False(needsContainer);
        Assert.Null(recycleKey);
    }

    private sealed class TestTranscript : StrataChatTranscript
    {
        public (bool NeedsContainer, object? RecycleKey) GetContainerDecision(object? item, int index)
        {
            var needsContainer = NeedsContainerOverride(item, index, out var recycleKey);
            return (needsContainer, recycleKey);
        }
    }

    private sealed class TestVirtualizedItem : IStrataVirtualizedItem
    {
        public object? VirtualizationRecycleKey { get; init; }
        public object? VirtualizationMeasureKey { get; init; }
        public double? VirtualizationHeightHint { get; init; }
    }

    private sealed class PlainTranscriptItem;
}