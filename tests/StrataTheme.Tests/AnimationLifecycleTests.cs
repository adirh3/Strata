using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Animation.Easings;
using Avalonia.Headless;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StrataTheme.Animation;
using StrataTheme.Controls;

namespace StrataTheme.Tests;

[Collection("Avalonia UI")]
public sealed class AnimationLifecycleTests
{
    private readonly AvaloniaFixture _fixture;

    public AnimationLifecycleTests(AvaloniaFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task LifecycleOpacityPulse_FollowsAttachVisibilityStateAndReattach()
    {
        var result = await _fixture.Dispatch(() =>
        {
            var target = CreatePulsingBorder();
            var host = new Border { Child = target };
            var window = new Window { Width = 320, Height = 200, Content = host };

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var runningWhileAttached = LifecycleOpacityPulse.IsRunning(target);

            host.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            var stoppedWhileHidden = !LifecycleOpacityPulse.IsRunning(target);

            host.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var restartedWhenVisible = LifecycleOpacityPulse.IsRunning(target);

            host.Child = null;
            Dispatcher.UIThread.RunJobs();
            var stoppedWhenDetached = !LifecycleOpacityPulse.IsRunning(target);

            LifecycleOpacityPulse.SetIsActive(target, false);
            LifecycleOpacityPulse.SetIsActive(target, true);
            Dispatcher.UIThread.RunJobs();
            var refusedDetachedRestart = !LifecycleOpacityPulse.IsRunning(target);

            host.Child = target;
            Dispatcher.UIThread.RunJobs();
            var restartedWhenReattached = LifecycleOpacityPulse.IsRunning(target);

            LifecycleOpacityPulse.SetIsActive(target, false);
            Dispatcher.UIThread.RunJobs();
            var stoppedWhenInactive = !LifecycleOpacityPulse.IsRunning(target);

            window.Close();
            return (
                runningWhileAttached,
                stoppedWhileHidden,
                restartedWhenVisible,
                stoppedWhenDetached,
                refusedDetachedRestart,
                restartedWhenReattached,
                stoppedWhenInactive);
        });

        Assert.True(result.runningWhileAttached);
        Assert.True(result.stoppedWhileHidden);
        Assert.True(result.restartedWhenVisible);
        Assert.True(result.stoppedWhenDetached);
        Assert.True(result.refusedDetachedRestart);
        Assert.True(result.restartedWhenReattached);
        Assert.True(result.stoppedWhenInactive);
    }

    [Fact]
    public async Task LifecycleOpacityPulse_DetachedStressLeavesNoRetainedVisuals()
    {
        const int visualCount = 256;

        var result = await _fixture.Dispatch(() =>
        {
            var panel = new StackPanel();
            var targets = new List<Border>(visualCount);
            var window = new Window { Width = 480, Height = 720, Content = panel };

            for (var i = 0; i < visualCount; i++)
            {
                var target = CreatePulsingBorder();
                targets.Add(target);
                panel.Children.Add(target);
            }

            window.Show();
            Dispatcher.UIThread.RunJobs();
            var runningCount = targets.Count(LifecycleOpacityPulse.IsRunning);

            panel.Children.Clear();
            Dispatcher.UIThread.RunJobs();
            var stoppedCount = targets.Count(target => !LifecycleOpacityPulse.IsRunning(target));
            var weakTargets = targets.Select(target => new WeakReference(target)).ToArray();

            targets.Clear();
            for (var i = 0; i < 3; i++)
            {
                using var frame = window.CaptureRenderedFrame();
                Dispatcher.UIThread.RunJobs();
            }

            return (runningCount, stoppedCount, weakTargets, window);
        });

        try
        {
            ForceFullGc();
            var retainedCount = result.weakTargets.Count(reference => reference.IsAlive);

            Assert.Equal(visualCount, result.runningCount);
            Assert.Equal(visualCount, result.stoppedCount);
            Assert.Equal(0, retainedCount);
        }
        finally
        {
            await _fixture.Dispatch(() =>
            {
                result.window.Close();
                Dispatcher.UIThread.RunJobs();
            });
        }
    }

    [Fact]
    public void LifecycleOpacityPulse_PreservesStyleAndCompositorEasingModes()
    {
        const double fromOpacity = 1d;
        const double toOpacity = 0.25d;
        var styleEasing = new SineEaseInOut();

        Assert.IsType<LinearEasing>(LifecycleOpacityPulse.CreateSegmentEasing(
            LifecycleOpacityPulseEasing.Linear,
            0d,
            0.5d,
            0d,
            0.5d));
        Assert.Null(LifecycleOpacityPulse.CreateSegmentEasing(
            LifecycleOpacityPulseEasing.CompositorDefault,
            0d,
            0.5d,
            0d,
            0.5d));

        AssertSinePulseMatchesStyle(0.38d);
        AssertSinePulseMatchesStyle(0.5d);
        Assert.True(Math.Abs(
            LifecycleOpacityPulse.MapTimelineCue(
                LifecycleOpacityPulseEasing.SineEaseInOut,
                0.38d) - 0.38d) > 0.01d);
        Assert.Equal(
            0.7803300858899107d,
            EvaluateReplacementOpacity(0.5d, 0.25d),
            precision: 10);

        void AssertSinePulseMatchesStyle(double peakCue)
        {
            foreach (var timelineProgress in new[]
                     {
                         0d, 0.1d, 0.25d, 0.38d, 0.5d, 0.625d, 0.75d, 0.875d, 1d,
                     })
            {
                var globallyEasedProgress = styleEasing.Ease(timelineProgress);
                var expectedOpacity = globallyEasedProgress <= peakCue
                    ? Interpolate(
                        fromOpacity,
                        toOpacity,
                        globallyEasedProgress / peakCue)
                    : Interpolate(
                        toOpacity,
                        fromOpacity,
                        (globallyEasedProgress - peakCue) / (1d - peakCue));

                Assert.Equal(
                    expectedOpacity,
                    EvaluateReplacementOpacity(peakCue, timelineProgress),
                    precision: 10);
            }
        }

        static double EvaluateReplacementOpacity(double peakCue, double timelineProgress)
        {
            const double fromOpacity = 1d;
            const double toOpacity = 0.25d;
            var peakAnimationCue = LifecycleOpacityPulse.MapTimelineCue(
                LifecycleOpacityPulseEasing.SineEaseInOut,
                peakCue);

            if (timelineProgress <= peakAnimationCue)
            {
                return Interpolate(
                    fromOpacity,
                    toOpacity,
                    LifecycleOpacityPulse.EaseTimelineSegment(
                        LifecycleOpacityPulseEasing.SineEaseInOut,
                        0d,
                        peakCue,
                        0d,
                        peakAnimationCue,
                        timelineProgress / peakAnimationCue));
            }

            return Interpolate(
                toOpacity,
                fromOpacity,
                LifecycleOpacityPulse.EaseTimelineSegment(
                    LifecycleOpacityPulseEasing.SineEaseInOut,
                    peakCue,
                    1d,
                    peakAnimationCue,
                    1d,
                    (timelineProgress - peakAnimationCue) / (1d - peakAnimationCue)));
        }
    }

    [Fact]
    public async Task MovingBarAnimations_StopOnDetachRefuseLateRestartAndResumeOnReattach()
    {
        var result = await _fixture.Dispatch(() =>
        {
            var canvas = new StrataCanvas
            {
                IsGenerating = true,
                Template = BuildCanvasTemplate(),
            };
            var stream = new StrataStream
            {
                IsStreaming = true,
                Template = BuildStreamTemplate(),
            };
            var panel = new StackPanel { Children = { canvas, stream } };
            var window = new Window { Width = 480, Height = 320, Content = panel };

            window.Show();
            canvas.ApplyTemplate();
            stream.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            var runningWhileAttached =
                canvas.IsGeneratingAnimationRunningForTest &&
                stream.IsStreamAnimationRunningForTest;

            panel.IsVisible = false;
            Dispatcher.UIThread.RunJobs();
            var stoppedWhileHidden =
                !canvas.IsGeneratingAnimationRunningForTest &&
                !stream.IsStreamAnimationRunningForTest;

            canvas.IsGenerating = false;
            canvas.IsGenerating = true;
            stream.IsStreaming = false;
            stream.IsStreaming = true;
            Dispatcher.UIThread.RunJobs();
            var refusedHiddenRestart =
                !canvas.IsGeneratingAnimationRunningForTest &&
                !stream.IsStreamAnimationRunningForTest;

            panel.IsVisible = true;
            Dispatcher.UIThread.RunJobs();
            var resumedWhenVisible =
                canvas.IsGeneratingAnimationRunningForTest &&
                stream.IsStreamAnimationRunningForTest;

            panel.Children.Clear();
            Dispatcher.UIThread.RunJobs();
            var stoppedWhenDetached =
                !canvas.IsGeneratingAnimationRunningForTest &&
                !stream.IsStreamAnimationRunningForTest;

            canvas.IsGenerating = false;
            canvas.IsGenerating = true;
            stream.IsStreaming = false;
            stream.IsStreaming = true;
            Dispatcher.UIThread.RunJobs();
            var refusedDetachedRestart =
                !canvas.IsGeneratingAnimationRunningForTest &&
                !stream.IsStreamAnimationRunningForTest;

            panel.Children.Add(canvas);
            panel.Children.Add(stream);
            Dispatcher.UIThread.RunJobs();
            var resumedWhenReattached =
                canvas.IsGeneratingAnimationRunningForTest &&
                stream.IsStreamAnimationRunningForTest;

            var oldCanvasBar = canvas.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_ActivityBar");
            var oldStreamBar = stream.GetVisualDescendants()
                .OfType<Border>()
                .Single(control => control.Name == "PART_StreamBar");
            var oldCanvasBarReference = new WeakReference(oldCanvasBar);
            var oldStreamBarReference = new WeakReference(oldStreamBar);
            var canvasStopCountBeforeReplacement = canvas.GeneratingAnimationStopCountForTest;
            var streamStopCountBeforeReplacement = stream.StreamAnimationStopCountForTest;

            canvas.Template = BuildCanvasTemplate();
            stream.Template = BuildStreamTemplate();
            canvas.ApplyTemplate();
            stream.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            var survivedTemplateReplacement =
                canvas.IsGeneratingAnimationRunningForTest &&
                stream.IsStreamAnimationRunningForTest;
            var stoppedOldTemplateAnimations =
                canvas.GeneratingAnimationStopCountForTest > canvasStopCountBeforeReplacement &&
                stream.StreamAnimationStopCountForTest > streamStopCountBeforeReplacement;
            oldCanvasBar = null!;
            oldStreamBar = null!;
            for (var i = 0; i < 3; i++)
            {
                using var frame = window.CaptureRenderedFrame();
                Dispatcher.UIThread.RunJobs();
            }

            return (
                runningWhileAttached,
                stoppedWhileHidden,
                refusedHiddenRestart,
                resumedWhenVisible,
                stoppedWhenDetached,
                refusedDetachedRestart,
                resumedWhenReattached,
                survivedTemplateReplacement,
                stoppedOldTemplateAnimations,
                oldCanvasBarReference,
                oldStreamBarReference,
                canvas,
                stream,
                panel,
                window);
        });

        ForceFullGc();
        var oldCanvasBarCollected = !result.oldCanvasBarReference.IsAlive;
        var oldStreamBarCollected = !result.oldStreamBarReference.IsAlive;
        var stoppedAfterTemplateReplacement = await _fixture.Dispatch(() =>
        {
            result.panel.Children.Clear();
            Dispatcher.UIThread.RunJobs();
            var stopped =
                !result.canvas.IsGeneratingAnimationRunningForTest &&
                !result.stream.IsStreamAnimationRunningForTest;
            result.window.Close();
            Dispatcher.UIThread.RunJobs();
            return stopped;
        });

        Assert.True(result.runningWhileAttached);
        Assert.True(result.stoppedWhileHidden);
        Assert.True(result.refusedHiddenRestart);
        Assert.True(result.resumedWhenVisible);
        Assert.True(result.stoppedWhenDetached);
        Assert.True(result.refusedDetachedRestart);
        Assert.True(result.resumedWhenReattached);
        Assert.True(result.survivedTemplateReplacement);
        Assert.True(result.stoppedOldTemplateAnimations);
        Assert.True(stoppedAfterTemplateReplacement);
        Assert.True(oldCanvasBarCollected);
        Assert.True(oldStreamBarCollected);
    }

    [Fact]
    public async Task CanvasQueuedStart_DoesNotRestartAfterClose()
    {
        var stayedStopped = await _fixture.Dispatch(() =>
        {
            var canvas = new StrataCanvas
            {
                IsGenerating = true,
                Template = BuildCanvasTemplate(),
            };
            var window = new Window { Width = 320, Height = 180, Content = canvas };

            window.Show();
            canvas.ApplyTemplate();
            canvas.IsOpen = false;
            Dispatcher.UIThread.RunJobs();

            var result = !canvas.IsGeneratingAnimationRunningForTest;
            window.Close();
            return result;
        });

        Assert.True(stayedStopped);
    }

    [Fact]
    public async Task MovingBarAnimations_InactiveTemplatesNormalizeNewBars()
    {
        var result = await _fixture.Dispatch(() =>
        {
            var canvas = new StrataCanvas
            {
                IsGenerating = false,
                Template = BuildCanvasTemplate(),
            };
            var stream = new StrataStream
            {
                IsStreaming = false,
                Template = BuildStreamTemplate(),
            };
            var window = new Window
            {
                Width = 480,
                Height = 320,
                Content = new StackPanel { Children = { canvas, stream } },
            };

            window.Show();
            canvas.ApplyTemplate();
            stream.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            var initialBarsHidden =
                GetCompositionOpacity(canvas, "PART_ActivityBar") == 0f &&
                GetCompositionOpacity(stream, "PART_StreamBar") == 0f;

            canvas.Template = BuildCanvasTemplate();
            stream.Template = BuildStreamTemplate();
            canvas.ApplyTemplate();
            stream.ApplyTemplate();
            Dispatcher.UIThread.RunJobs();
            var replacementBarsHidden =
                GetCompositionOpacity(canvas, "PART_ActivityBar") == 0f &&
                GetCompositionOpacity(stream, "PART_StreamBar") == 0f;

            window.Close();
            return (initialBarsHidden, replacementBarsHidden);
        });

        Assert.True(result.initialBarsHidden);
        Assert.True(result.replacementBarsHidden);
    }

    private static double Interpolate(double from, double to, double progress) =>
        from + ((to - from) * progress);

    private static float GetCompositionOpacity(Control control, string partName)
    {
        var part = control.GetVisualDescendants()
            .OfType<Border>()
            .Single(candidate => candidate.Name == partName);
        return ElementComposition.GetElementVisual(part)?.Opacity
               ?? throw new InvalidOperationException($"Template part '{partName}' has no composition visual.");
    }

    private static Border CreatePulsingBorder()
    {
        var target = new Border { Width = 12, Height = 12, Opacity = 0.8 };
        LifecycleOpacityPulse.SetFromOpacity(target, 0.8);
        LifecycleOpacityPulse.SetToOpacity(target, 0.3);
        LifecycleOpacityPulse.SetDuration(target, TimeSpan.FromMilliseconds(250));
        LifecycleOpacityPulse.SetIsActive(target, true);
        return target;
    }

    private static FuncControlTemplate<StrataCanvas> BuildCanvasTemplate() =>
        new((_, scope) =>
        {
            var activityBar = new Border { Name = "PART_ActivityBar", Width = 96, Height = 2 };
            var activityTrack = new Border
            {
                Name = "PART_ActivityTrack",
                Width = 240,
                Height = 2,
                Child = activityBar,
            };
            var root = new Border { Name = "PART_Root", Child = activityTrack };

            scope.Register("PART_Root", root);
            scope.Register("PART_ActivityTrack", activityTrack);
            scope.Register("PART_ActivityBar", activityBar);
            return root;
        });

    private static FuncControlTemplate<StrataStream> BuildStreamTemplate() =>
        new((_, scope) =>
        {
            var streamBar = new Border { Name = "PART_StreamBar", Width = 90, Height = 2 };
            var streamTrack = new Border
            {
                Name = "PART_StreamTrack",
                Width = 240,
                Height = 2,
                Child = streamBar,
            };
            var statusArea = new Border { Name = "PART_StatusArea", Height = 24 };
            var root = new StackPanel { Children = { streamTrack, statusArea } };

            scope.Register("PART_StreamTrack", streamTrack);
            scope.Register("PART_StreamBar", streamBar);
            scope.Register("PART_StatusArea", statusArea);
            return root;
        });

    private static void ForceFullGc()
    {
        for (var i = 0; i < 5; i++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }
}
