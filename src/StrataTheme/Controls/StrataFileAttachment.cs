using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Rendering.Composition;
using Avalonia.Rendering.Composition.Animations;
using Avalonia.Threading;
using System;
using System.IO;

namespace StrataTheme.Controls;

/// <summary>Lifecycle status of a file attachment.</summary>
public enum StrataAttachmentStatus
{
    /// <summary>File is queued but not yet processed.</summary>
    Pending,
    /// <summary>File is currently uploading or processing.</summary>
    Uploading,
    /// <summary>Upload/processing completed successfully.</summary>
    Completed,
    /// <summary>Upload or validation failed.</summary>
    Failed
}

/// <summary>
/// Compact file attachment chip showing icon glyph, file name, size, and status.
/// Supports click-to-open, remove button with event, and animated upload state.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataFileAttachment FileName="report.pdf"
///                                  FileSize="2.4 MB"
///                                  Status="Completed"
///                                  IsRemovable="True" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Root (Border), PART_StatusDot (Border),
/// PART_RemoveButton (Button), PART_Stratum (Border).</para>
/// <para><b>Pseudo-classes:</b> :pending, :uploading, :completed, :failed, :removable.</para>
/// </remarks>
public class StrataFileAttachment : TemplatedControl
{
    private Border? _statusDot;

    public static readonly StyledProperty<string> FileNameProperty =
        AvaloniaProperty.Register<StrataFileAttachment, string>(nameof(FileName), "file.txt");

    public static readonly StyledProperty<string?> FileSizeProperty =
        AvaloniaProperty.Register<StrataFileAttachment, string?>(nameof(FileSize));

    public static readonly StyledProperty<string> IconGlyphProperty =
        AvaloniaProperty.Register<StrataFileAttachment, string>(nameof(IconGlyph), "\U0001F4CE");

    public static readonly StyledProperty<StrataAttachmentStatus> StatusProperty =
        AvaloniaProperty.Register<StrataFileAttachment, StrataAttachmentStatus>(nameof(Status), StrataAttachmentStatus.Completed);

    public static readonly StyledProperty<bool> IsRemovableProperty =
        AvaloniaProperty.Register<StrataFileAttachment, bool>(nameof(IsRemovable), true);

    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<StrataFileAttachment, double>(nameof(Progress), 0);

    public static readonly DirectProperty<StrataFileAttachment, string> StatusTextProperty =
        AvaloniaProperty.RegisterDirect<StrataFileAttachment, string>(nameof(StatusText), o => o.StatusText);

    public static readonly RoutedEvent<RoutedEventArgs> RemoveRequestedEvent =
        RoutedEvent.Register<StrataFileAttachment, RoutedEventArgs>(nameof(RemoveRequested), RoutingStrategies.Bubble);

    public static readonly RoutedEvent<RoutedEventArgs> OpenRequestedEvent =
        RoutedEvent.Register<StrataFileAttachment, RoutedEventArgs>(nameof(OpenRequested), RoutingStrategies.Bubble);

    static StrataFileAttachment()
    {
        StatusProperty.Changed.AddClassHandler<StrataFileAttachment>((c, _) => c.UpdateState());
        IsRemovableProperty.Changed.AddClassHandler<StrataFileAttachment>((c, _) => c.UpdateState());
        FileNameProperty.Changed.AddClassHandler<StrataFileAttachment>((c, _) => c.UpdateIconForExtension());
    }

    public string FileName
    {
        get => GetValue(FileNameProperty);
        set => SetValue(FileNameProperty, value);
    }

    public string? FileSize
    {
        get => GetValue(FileSizeProperty);
        set => SetValue(FileSizeProperty, value);
    }

    public string IconGlyph
    {
        get => GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    public StrataAttachmentStatus Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    public bool IsRemovable
    {
        get => GetValue(IsRemovableProperty);
        set => SetValue(IsRemovableProperty, value);
    }

    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public string StatusText => Status switch
    {
        StrataAttachmentStatus.Pending => "Pending",
        StrataAttachmentStatus.Uploading => $"Uploading {Progress:0}%",
        StrataAttachmentStatus.Completed => "Ready",
        StrataAttachmentStatus.Failed => "Failed",
        _ => ""
    };

    public event EventHandler<RoutedEventArgs>? RemoveRequested
    {
        add => AddHandler(RemoveRequestedEvent, value);
        remove => RemoveHandler(RemoveRequestedEvent, value);
    }

    public event EventHandler<RoutedEventArgs>? OpenRequested
    {
        add => AddHandler(OpenRequestedEvent, value);
        remove => RemoveHandler(OpenRequestedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _statusDot = e.NameScope.Find<Border>("PART_StatusDot");

        var root = e.NameScope.Find<Border>("PART_Root");
        if (root is not null)
        {
            root.PointerPressed += (_, pe) =>
            {
                if (pe.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                {
                    RaiseEvent(new RoutedEventArgs(OpenRequestedEvent));
                    pe.Handled = true;
                }
            };
        }

        var removeBtn = e.NameScope.Find<Button>("PART_RemoveButton");
        if (removeBtn is not null)
            removeBtn.Click += (_, _) => RaiseEvent(new RoutedEventArgs(RemoveRequestedEvent));

        UpdateState();
        UpdateIconForExtension();

        Dispatcher.UIThread.Post(() =>
        {
            if (Status == StrataAttachmentStatus.Uploading)
                StartUploadPulse();
        }, DispatcherPriority.Loaded);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        StopUploadPulse();
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateState()
    {
        RaisePropertyChanged(StatusTextProperty, default!, StatusText);

        PseudoClasses.Set(":pending", Status == StrataAttachmentStatus.Pending);
        PseudoClasses.Set(":uploading", Status == StrataAttachmentStatus.Uploading);
        PseudoClasses.Set(":completed", Status == StrataAttachmentStatus.Completed);
        PseudoClasses.Set(":failed", Status == StrataAttachmentStatus.Failed);
        PseudoClasses.Set(":removable", IsRemovable);

        if (Status == StrataAttachmentStatus.Uploading)
            StartUploadPulse();
        else
            StopUploadPulse();
    }

    private void UpdateIconForExtension()
    {
        // Only auto-set icon if the user hasn't explicitly set one
        var ext = Path.GetExtension(FileName)?.ToLowerInvariant();
        var glyph = ext switch
        {
            ".pdf" => "\U0001F4D1",      // bookmark tab → PDF
            ".doc" or ".docx" => "\U0001F4DD", // memo → doc
            ".xls" or ".xlsx" or ".csv" => "\U0001F4CA", // bar chart → spreadsheet
            ".ppt" or ".pptx" => "\U0001F4CA",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".bmp" or ".webp" => "\U0001F5BC", // framed picture
            ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" => "\U0001F3AC",  // clapper
            ".mp3" or ".wav" or ".ogg" or ".flac" => "\U0001F3B5",           // musical note
            ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "\U0001F4E6",    // package
            ".cs" or ".ts" or ".js" or ".py" or ".java" or ".cpp" or ".rs" => "\U0001F4C4", // page
            ".json" or ".xml" or ".yaml" or ".yml" or ".toml" => "\u2699",    // gear
            ".md" or ".txt" or ".log" => "\U0001F4DD",
            _ => "\U0001F4CE"             // paperclip
        };
        SetValue(IconGlyphProperty, glyph);
    }

    private void StartUploadPulse()
    {
        if (_statusDot is null) return;
        var visual = ElementComposition.GetElementVisual(_statusDot);
        if (visual is null) return;

        var anim = visual.Compositor.CreateScalarKeyFrameAnimation();
        anim.Target = "Opacity";
        anim.InsertKeyFrame(0f, 1f);
        anim.InsertKeyFrame(0.5f, 0.35f);
        anim.InsertKeyFrame(1f, 1f);
        anim.Duration = TimeSpan.FromMilliseconds(900);
        anim.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Opacity", anim);
    }

    private void StopUploadPulse()
    {
        if (_statusDot is null) return;
        var visual = ElementComposition.GetElementVisual(_statusDot);
        if (visual is null) return;

        var reset = visual.Compositor.CreateScalarKeyFrameAnimation();
        reset.Target = "Opacity";
        reset.InsertKeyFrame(0f, 1f);
        reset.Duration = TimeSpan.FromMilliseconds(1);
        reset.IterationBehavior = AnimationIterationBehavior.Count;
        reset.IterationCount = 1;
        visual.StartAnimation("Opacity", reset);
    }
}
