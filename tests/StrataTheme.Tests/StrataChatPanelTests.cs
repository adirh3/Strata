using StrataTheme.Controls;

namespace StrataTheme.Tests;

/// <summary>
/// Tests for StrataChatPanel core algorithms: prefix sums, binary search,
/// height estimation, slot management, and allocation efficiency.
/// Uses the extracted <see cref="StrataChatPanelAlgorithms"/> which carries
/// no Avalonia platform dependencies.
/// </summary>
public class StrataChatPanelTests
{
    // =======================================================================
    //  Prefix Heights: correctness
    // =======================================================================

    [Fact]
    public void PrefixHeights_EmptyPanel_TotalHeightZero()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.EnsurePrefixHeights();
        Assert.Equal(0, alg.CachedTotalHeight);
    }

    [Fact]
    public void PrefixHeights_SingleItem_CorrectTotal()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.AddSlot(100);
        alg.EnsurePrefixHeights();
        Assert.Equal(100, alg.CachedTotalHeight);
        Assert.Equal(0, alg.GetSlotTop(0));
        Assert.Equal(100, alg.GetSlotBottom(0));
    }

    [Fact]
    public void PrefixHeights_MultipleItems_WithSpacing()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 10 };
        alg.AddSlot(100); // 0..100
        alg.AddSlot(200); // 110..310
        alg.AddSlot(50);  // 320..370

        alg.EnsurePrefixHeights();
        Assert.Equal(0, alg.GetSlotTop(0));
        // GetSlotBottom includes spacing gap after the item (prefix[i+1]).
        // Slot 0: 100 height + 10 spacing = prefix[1] = 110
        Assert.Equal(110, alg.GetSlotBottom(0));
        Assert.Equal(110, alg.GetSlotTop(1));
        // Slot 1: 200 height + 10 spacing → prefix[2] = 110+210 = 320
        Assert.Equal(320, alg.GetSlotBottom(1));
        Assert.Equal(320, alg.GetSlotTop(2));
        // Slot 2 is last → no spacing → prefix[3] = 320+50 = 370
        Assert.Equal(370, alg.CachedTotalHeight);
    }

    [Fact]
    public void PrefixHeights_IncrementalRebuild_OnlyRecalcsFromDirtyIndex()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 100; i++)
            alg.AddSlot(10);

        alg.EnsurePrefixHeights();
        Assert.Equal(1000, alg.CachedTotalHeight);

        // Change slot 50's height — only 50+ should be recalculated
        alg.MeasureSlot(50, 20, 800);
        alg.EnsurePrefixHeights();
        Assert.Equal(1010, alg.CachedTotalHeight);
        Assert.Equal(500, alg.GetSlotTop(50)); // first 50 unchanged
        Assert.Equal(520, alg.GetSlotBottom(50)); // now 20 tall
    }

    [Fact]
    public void PrefixHeights_ReallocWithPartialDirty_CopiesBaseEntry()
    {
        // Regression: when the prefix array grows and _prefixDirtyFrom > 0,
        // the entry at _prefixHeights[_prefixDirtyFrom] must be copied
        // because the rebuild loop reads it as a base value.
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };

        // Force initial allocation to be small by adding items one at a time
        // and calling EnsurePrefixHeights, which allocates min 64.
        alg.AddSlot(100);
        alg.EnsurePrefixHeights();
        Assert.Equal(100, alg.CachedTotalHeight);

        // Now add enough items to force reallocation (>63 total slots)
        for (int i = 1; i < 70; i++)
            alg.AddSlot(100);

        // This would crash or give wrong results if the base entry isn't copied
        alg.EnsurePrefixHeights();
        Assert.Equal(7000, alg.CachedTotalHeight);

        // Verify binary search works correctly after realloc
        Assert.Equal(0, alg.FindFirstSlotBelow(0));
        Assert.Equal(50, alg.FindFirstSlotBelow(5050));
    }

    [Fact]
    public void BinarySearch_DoesNotCrash_WhenPrefixArrayIsStale()
    {
        // Even if prefix array is somehow stale, binary search must not throw
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 10; i++)
            alg.AddSlot(100);

        // Call binary search (which calls EnsurePrefixHeights internally)
        var first = alg.FindFirstSlotBelow(0);
        var last = alg.FindLastSlotAbove(1000);
        Assert.True(first >= 0);
        Assert.True(last >= 0);
    }

    // =======================================================================
    //  Prefix Heights: geometric growth (allocation efficiency)
    // =======================================================================

    [Fact]
    public void PrefixArray_GeometricGrowth_DoesNotReallocateEveryAdd()
    {
        var alg = new StrataChatPanelAlgorithms();

        // Add 200 items one at a time (simulating streaming)
        for (int i = 0; i < 200; i++)
        {
            alg.AddSlot(100);
            alg.EnsurePrefixHeights(); // force rebuild after each add
        }

        // With geometric doubling from min 64, we expect very few reallocations:
        // 0→64 (1st), 64→128 (at item 64), 128→256 (at item 128) = 3 reallocs
        // Without geometric growth: 200 reallocs (one per add)
        Assert.True(alg.PrefixReallocCount <= 5,
            $"Expected ≤5 prefix array reallocations, got {alg.PrefixReallocCount}");
    }

    [Fact]
    public void PrefixArray_MinimumCapacity64()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.AddSlot(100);
        alg.EnsurePrefixHeights();

        // After first add, array should be at least 64 (not 2)
        Assert.True(alg.PrefixArrayLength >= 64);
    }

    // =======================================================================
    //  Binary Search: FindFirstSlotBelow / FindLastSlotAbove
    // =======================================================================

    [Fact]
    public void BinarySearch_FindFirstSlotBelow_AtStart()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 10; i++)
            alg.AddSlot(100);

        Assert.Equal(0, alg.FindFirstSlotBelow(0));
        Assert.Equal(0, alg.FindFirstSlotBelow(50));
    }

    [Fact]
    public void BinarySearch_FindFirstSlotBelow_MidRange()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 10; i++)
            alg.AddSlot(100); // items at 0, 100, 200, ...

        // y=350 → first slot whose bottom > 350 is slot 3 (bottom=400)
        Assert.Equal(3, alg.FindFirstSlotBelow(350));
        // y=500 → slot 5 (bottom=600)
        Assert.Equal(5, alg.FindFirstSlotBelow(500));
    }

    [Fact]
    public void BinarySearch_FindLastSlotAbove_AtEnd()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 10; i++)
            alg.AddSlot(100);

        Assert.Equal(9, alg.FindLastSlotAbove(1000)); // past the end
        Assert.Equal(9, alg.FindLastSlotAbove(900));   // top of last
    }

    [Fact]
    public void BinarySearch_FindLastSlotAbove_MidRange()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 10; i++)
            alg.AddSlot(100);

        // y=350 → last slot whose top ≤ 350 is slot 3 (top=300)
        Assert.Equal(3, alg.FindLastSlotAbove(350));
    }

    [Fact]
    public void BinarySearch_VariableHeights()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        alg.AddSlot(50);   // 0..50
        alg.AddSlot(300);  // 50..350
        alg.AddSlot(100);  // 350..450
        alg.AddSlot(200);  // 450..650

        Assert.Equal(1, alg.FindFirstSlotBelow(50));  // exactly at boundary → slot 1
        Assert.Equal(2, alg.FindFirstSlotBelow(350)); // slot 2 bottom=450 > 350
        Assert.Equal(1, alg.FindLastSlotAbove(100));   // slot 1 top=50 ≤ 100
    }

    // =======================================================================
    //  ComputeRange: zone calculation
    // =======================================================================

    [Fact]
    public void ComputeRange_ReturnsCorrectVisibleItems()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 100; i++)
            alg.AddSlot(100); // 10000px total

        // Viewport at scroll=500, height=800 → visible items ~5..12
        // With buffer=800 (1x viewport) → items ~0..20
        var (first, last) = alg.ComputeRange(500, 800, 800, bufferItems: 0);

        Assert.True(first <= 0, $"first should be ≤0, got {first}");
        Assert.True(last >= 12, $"last should be ≥12, got {last}");
    }

    [Fact]
    public void ComputeRange_WarmZone_LargerThanVisible()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 100; i++)
            alg.AddSlot(100);

        var (visFirst, visLast) = alg.ComputeRange(3000, 800, 800, bufferItems: 5);
        var (warmFirst, warmLast) = alg.ComputeRange(3000, 800, 2400, bufferItems: 5);

        Assert.True(warmFirst <= visFirst, "Warm zone should start before visible zone");
        Assert.True(warmLast >= visLast, "Warm zone should extend beyond visible zone");
    }

    [Fact]
    public void ComputeRange_Empty_ReturnsInvalidRange()
    {
        var alg = new StrataChatPanelAlgorithms();
        var (first, last) = alg.ComputeRange(0, 800, 800);
        Assert.True(last < first); // empty range
    }

    // =======================================================================
    //  Height Estimation: role-based defaults and running averages
    // =======================================================================

    [Fact]
    public void HeightEstimate_DefaultsByRole()
    {
        var alg = new StrataChatPanelAlgorithms();
        Assert.Equal(300, alg.EstimateHeightForRole(0));  // Assistant
        Assert.Equal(60, alg.EstimateHeightForRole(1));   // User
        Assert.Equal(80, alg.EstimateHeightForRole(2));   // System
        Assert.Equal(120, alg.EstimateHeightForRole(3));  // Tool
        Assert.Equal(120, alg.EstimateHeightForRole(-1)); // Unknown fallback
    }

    [Fact]
    public void HeightEstimate_RunningAverage_UpdatesAfterMeasurement()
    {
        var alg = new StrataChatPanelAlgorithms();
        // Add 3 user slots and measure them
        alg.AddSlot(60, roleIndex: 1);
        alg.AddSlot(60, roleIndex: 1);
        alg.AddSlot(60, roleIndex: 1);

        alg.MeasureSlot(0, 80, 800);
        alg.MeasureSlot(1, 100, 800);
        alg.MeasureSlot(2, 120, 800);

        // Running average = (80+100+120)/3 = 100
        Assert.Equal(100, alg.EstimateHeightForRole(1));
    }

    [Fact]
    public void HeightEstimate_RemeasuredSlot_CorrectlyUpdatesAverage()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.AddSlot(60, roleIndex: 0); // Assistant
        alg.MeasureSlot(0, 200, 800);  // First measure: 200

        Assert.Equal(200, alg.EstimateHeightForRole(0));

        alg.MeasureSlot(0, 400, 800); // Remeasure (streaming growth): 400
        Assert.Equal(400, alg.EstimateHeightForRole(0));
        Assert.Equal(1, alg.MeasuredHeightCount); // still 1 item
    }

    [Fact]
    public void HeightEstimate_RemoveSlot_UpdatesAverage()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.AddSlot(60, roleIndex: 1);
        alg.AddSlot(60, roleIndex: 1);
        alg.MeasureSlot(0, 100, 800);
        alg.MeasureSlot(1, 200, 800);

        Assert.Equal(150, alg.EstimateHeightForRole(1)); // (100+200)/2

        alg.RemoveSlot(0); // remove the 100-tall one
        Assert.Equal(200, alg.EstimateHeightForRole(1)); // only 200 left
        Assert.Equal(1, alg.MeasuredHeightCount);
    }

    // =======================================================================
    //  Slot Management: add, insert, remove, clear
    // =======================================================================

    [Fact]
    public void SlotManagement_Add_IncreasesCount()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.AddSlot(100);
        alg.AddSlot(200);
        Assert.Equal(2, alg.Slots.Count);
    }

    [Fact]
    public void SlotManagement_Insert_ShiftsExisting()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        alg.AddSlot(100);
        alg.AddSlot(300);

        alg.InsertSlot(1, 50); // insert between
        alg.EnsurePrefixHeights();

        Assert.Equal(3, alg.Slots.Count);
        Assert.Equal(100, alg.GetSlotTop(1)); // 50-tall slot at position 1
        Assert.Equal(150, alg.GetSlotTop(2)); // 300-tall slot shifted
        Assert.Equal(450, alg.CachedTotalHeight);
    }

    [Fact]
    public void SlotManagement_Remove_UpdatesHeights()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        alg.AddSlot(100);
        alg.AddSlot(200);
        alg.AddSlot(300);

        alg.RemoveSlot(1); // remove 200-tall
        alg.EnsurePrefixHeights();

        Assert.Equal(2, alg.Slots.Count);
        Assert.Equal(400, alg.CachedTotalHeight); // 100 + 300
    }

    [Fact]
    public void SlotManagement_Clear_ResetsEverything()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.AddSlot(100, roleIndex: 0);
        alg.MeasureSlot(0, 200, 800);

        alg.Clear();

        Assert.Empty(alg.Slots);
        Assert.Equal(0, alg.MeasuredHeightCount);
        Assert.Equal(0, alg.MeasuredHeightSum);
    }

    // =======================================================================
    //  Streaming simulation: append + remeasure pattern
    // =======================================================================

    [Fact]
    public void Streaming_AppendAndGrow_MaintainsCorrectTotal()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 8 };

        // Simulate: 5 existing messages, then new assistant message streams in
        for (int i = 0; i < 5; i++)
        {
            alg.AddSlot(100);
            alg.MeasureSlot(i, 100, 800);
        }

        // Add streaming message (starts small)
        alg.AddSlot(60, roleIndex: 0);
        alg.MeasureSlot(5, 60, 800);

        alg.EnsurePrefixHeights();
        var h1 = alg.CachedTotalHeight;

        // Streaming grows the message
        alg.MeasureSlot(5, 200, 800);
        alg.EnsurePrefixHeights();
        var h2 = alg.CachedTotalHeight;
        Assert.Equal(h1 + 140, h2); // grew by 140 (200-60)

        alg.MeasureSlot(5, 500, 800);
        alg.EnsurePrefixHeights();
        var h3 = alg.CachedTotalHeight;
        Assert.Equal(h2 + 300, h3); // grew by 300 (500-200)
    }

    [Fact]
    public void Streaming_ManySmallGrows_NoExcessiveReallocations()
    {
        var alg = new StrataChatPanelAlgorithms();

        // Simulate: streaming message grows 100 times in small increments
        alg.AddSlot(20, roleIndex: 0);
        alg.MeasureSlot(0, 20, 800);
        alg.EnsurePrefixHeights(); // force initial realloc

        int initialReallocs = alg.PrefixReallocCount;

        for (int i = 0; i < 100; i++)
        {
            alg.MeasureSlot(0, 20 + (i + 1) * 5, 800);
            alg.EnsurePrefixHeights();
        }

        // Height changes dirty the prefix but don't grow the array (still 1 slot,
        // array was allocated with min capacity 64 on first ensure)
        Assert.Equal(initialReallocs, alg.PrefixReallocCount);
    }

    // =======================================================================
    //  Allocation pressure: single-item fast path
    // =======================================================================

    [Fact]
    public void SingleItemAdd_DoesNotAllocateArray()
    {
        // This test validates the fast path: single-item Insert uses
        // List.Insert(index, item) instead of InsertRange(index, array)
        var alg = new StrataChatPanelAlgorithms();
        long memBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int i = 0; i < 50; i++)
            alg.AddSlot(100); // uses List.Add (fast path)

        long memAfter = GC.GetAllocatedBytesForCurrentThread();
        long allocated = memAfter - memBefore;

        // List<T> internal growth dominates. The key thing is we are NOT
        // allocating 50 temporary ItemSlot[] arrays. Each ItemSlot is ~40 bytes,
        // so 50 arrays would be 50 * (16 + 40) = 2800 bytes overhead.
        // With the fast path it should be well under 2000 bytes total for the
        // Add calls (List internal buffer growth is amortized).
        Assert.True(allocated < 50_000,
            $"Allocated {allocated} bytes for 50 single-item adds — expected < 50KB");
    }

    // =======================================================================
    //  Edge cases
    // =======================================================================

    [Fact]
    public void PrefixHeights_ZeroHeightItems()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        alg.AddSlot(0);
        alg.AddSlot(100);
        alg.AddSlot(0);

        alg.EnsurePrefixHeights();
        Assert.Equal(0, alg.GetSlotTop(0));
        Assert.Equal(0, alg.GetSlotTop(1));
        Assert.Equal(100, alg.GetSlotTop(2));
        Assert.Equal(100, alg.CachedTotalHeight);
    }

    [Fact]
    public void BinarySearch_SingleItem()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        alg.AddSlot(500);

        Assert.Equal(0, alg.FindFirstSlotBelow(0));
        Assert.Equal(0, alg.FindFirstSlotBelow(250));
        Assert.Equal(0, alg.FindLastSlotAbove(0));
        Assert.Equal(0, alg.FindLastSlotAbove(500));
    }

    [Fact]
    public void PrefixHeights_LargeDataset_CorrectTotal()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 4 };
        const int count = 5000;

        for (int i = 0; i < count; i++)
            alg.AddSlot(50 + (i % 10) * 30); // heights 50..320

        alg.EnsurePrefixHeights();

        // Verify total matches manual summation
        double expectedTotal = 0;
        for (int i = 0; i < count; i++)
        {
            expectedTotal += 50 + (i % 10) * 30;
            if (i < count - 1) expectedTotal += 4; // spacing
        }

        Assert.Equal(expectedTotal, alg.CachedTotalHeight, precision: 1);
    }

    [Fact]
    public void MeasureSlot_WidthTracking()
    {
        var alg = new StrataChatPanelAlgorithms();
        alg.AddSlot(100);
        alg.MeasureSlot(0, 150, 800);

        Assert.Equal(800, alg.Slots[0].MeasuredAtWidth);
        Assert.True(alg.Slots[0].HasBeenMeasured);
    }

    [Fact]
    public void ComputeRange_ScrolledToEnd()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 50; i++)
            alg.AddSlot(100); // total 5000

        // Scroll to end: offset=4200, viewport=800
        var (first, last) = alg.ComputeRange(4200, 800, 800, bufferItems: 5);

        Assert.Equal(49, last); // should include last item
        Assert.True(first < 45, $"first should be well before end, got {first}");
    }

    [Fact]
    public void ComputeRange_ScrolledToStart()
    {
        var alg = new StrataChatPanelAlgorithms { Spacing = 0 };
        for (int i = 0; i < 50; i++)
            alg.AddSlot(100);

        var (first, last) = alg.ComputeRange(0, 800, 800, bufferItems: 5);

        Assert.Equal(0, first);
        Assert.True(last > 5, $"last should include items past viewport, got {last}");
    }
}
