using System.Collections;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace StrataTheme.Controls;

// Dictation: a microphone beside send starts it. While IsRecording, the voice bar takes the tools' place (a pulse, the
// time or a short status, a live waveform, the language it listens for and discard), the microphone finishes, and the
// words heard so far show in quiet text where they will land.
public partial class StrataChatComposer
{
    /// <summary>The microphone level while <see cref="IsRecording"/>, from 0 (silence) to 1 (loud), drawn by the voice bar's waveform.</summary>
    public static readonly StyledProperty<double> VoiceLevelProperty =
        AvaloniaProperty.Register<StrataChatComposer, double>(nameof(VoiceLevel));

    /// <summary>
    /// Words heard but not final yet while <see cref="IsRecording"/>, shown in quiet text where they will land: in the
    /// empty prompt, or under the text already there. The newest words stay in view.
    /// </summary>
    public static readonly StyledProperty<string?> VoicePreviewProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(VoicePreview));

    /// <summary>A short note that takes the voice bar's timer place while set, such as "Starting…" or "Finishing…".</summary>
    public static readonly StyledProperty<string?> VoiceStatusProperty =
        AvaloniaProperty.Register<StrataChatComposer, string?>(nameof(VoiceStatus));

    /// <summary>Command run by the voice bar's discard button, and by Escape while <see cref="IsRecording"/>.</summary>
    public static readonly StyledProperty<ICommand?> VoiceCancelCommandProperty =
        AvaloniaProperty.Register<StrataChatComposer, ICommand?>(nameof(VoiceCancelCommand));

    /// <summary>The name of the voice bar's discard button.</summary>
    public static readonly StyledProperty<string> CancelDictationLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(CancelDictationLabel), "Discard dictation");

    /// <summary>
    /// The languages dictation can listen for, offered by the voice bar's language button; each shows as its
    /// <see cref="object.ToString"/>. The button hides while this is null.
    /// </summary>
    public static readonly StyledProperty<IEnumerable?> VoiceLanguagesProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(VoiceLanguages));

    /// <summary>The language dictation listens for, one of <see cref="VoiceLanguages"/>, shown on the voice bar's language button.</summary>
    public static readonly StyledProperty<object?> SelectedVoiceLanguageProperty =
        AvaloniaProperty.Register<StrataChatComposer, object?>(nameof(SelectedVoiceLanguage), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Further languages, listed under <see cref="MoreVoiceLanguagesLabel"/> after <see cref="VoiceLanguages"/>.</summary>
    public static readonly StyledProperty<IEnumerable?> MoreVoiceLanguagesProperty =
        AvaloniaProperty.Register<StrataChatComposer, IEnumerable?>(nameof(MoreVoiceLanguages));

    /// <summary>The name of the voice bar's language button.</summary>
    public static readonly StyledProperty<string> VoiceLanguageLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(VoiceLanguageLabel), "Dictation language");

    /// <summary>The submenu holding <see cref="MoreVoiceLanguages"/>.</summary>
    public static readonly StyledProperty<string> MoreVoiceLanguagesLabelProperty =
        AvaloniaProperty.Register<StrataChatComposer, string>(nameof(MoreVoiceLanguagesLabel), "More languages");

    private static readonly TimeSpan RecordingClockInterval = TimeSpan.FromMilliseconds(250);
    private Button? _voiceButton;
    private Button? _voiceLanguageButton;
    private TextBlock? _voiceLanguageText;
    private TextBlock? _voiceElapsed;
    private TextBlock? _voicePreviewInline;
    private TextBlock? _voicePreviewBelow;
    private DispatcherTimer? _recordingClock;
    private long _recordingStarted;
    private int _shownRecordingSeconds = -1;

    public double VoiceLevel { get => GetValue(VoiceLevelProperty); set => SetValue(VoiceLevelProperty, value); }
    public string? VoicePreview { get => GetValue(VoicePreviewProperty); set => SetValue(VoicePreviewProperty, value); }
    public string? VoiceStatus { get => GetValue(VoiceStatusProperty); set => SetValue(VoiceStatusProperty, value); }
    public ICommand? VoiceCancelCommand { get => GetValue(VoiceCancelCommandProperty); set => SetValue(VoiceCancelCommandProperty, value); }
    public string CancelDictationLabel { get => GetValue(CancelDictationLabelProperty); set => SetValue(CancelDictationLabelProperty, value); }
    public IEnumerable? VoiceLanguages { get => GetValue(VoiceLanguagesProperty); set => SetValue(VoiceLanguagesProperty, value); }
    public object? SelectedVoiceLanguage { get => GetValue(SelectedVoiceLanguageProperty); set => SetValue(SelectedVoiceLanguageProperty, value); }
    public IEnumerable? MoreVoiceLanguages { get => GetValue(MoreVoiceLanguagesProperty); set => SetValue(MoreVoiceLanguagesProperty, value); }
    public string VoiceLanguageLabel { get => GetValue(VoiceLanguageLabelProperty); set => SetValue(VoiceLanguageLabelProperty, value); }
    public string MoreVoiceLanguagesLabel { get => GetValue(MoreVoiceLanguagesLabelProperty); set => SetValue(MoreVoiceLanguagesLabelProperty, value); }

    private static void RegisterVoiceClassHandlers()
    {
        IsRecordingProperty.Changed.AddClassHandler<StrataChatComposer>(static (composer, _) => composer.OnRecordingChanged());
        VoiceStatusProperty.Changed.AddClassHandler<StrataChatComposer>(static (composer, _) => composer.SyncVoice());
        VoicePreviewProperty.Changed.AddClassHandler<StrataChatComposer>(static (composer, _) => composer.SyncVoice());
        VoiceLanguagesProperty.Changed.AddClassHandler<StrataChatComposer>(static (composer, _) => composer.SyncVoice());
        SelectedVoiceLanguageProperty.Changed.AddClassHandler<StrataChatComposer>(static (composer, _) => composer.SyncVoice());
    }

    private void ApplyVoiceTemplate(TemplateAppliedEventArgs e)
    {
        _voiceButton = e.NameScope.Find<Button>("PART_VoiceButton");
        _voiceLanguageButton = e.NameScope.Find<Button>("PART_VoiceLanguageButton");
        _voiceLanguageText = e.NameScope.Find<TextBlock>("PART_VoiceLanguageText");
        _voiceElapsed = e.NameScope.Find<TextBlock>("PART_VoiceElapsed");
        _voicePreviewInline = e.NameScope.Find<TextBlock>("PART_VoicePreviewInline");
        _voicePreviewBelow = e.NameScope.Find<TextBlock>("PART_VoicePreview");
        Wire(e, "PART_VoiceButton", HandleVoiceButton);
        Wire(e, "PART_VoiceLanguageButton", OpenVoiceLanguages);
        Wire(e, "PART_VoiceCancelButton", CancelDictation);
        _shownRecordingSeconds = -1;
        SyncVoice();
        UpdateRecordingClock();
    }

    /// <summary>
    /// Lists <see cref="VoiceLanguages"/> under the language button, the current one checked, and
    /// <see cref="MoreVoiceLanguages"/> in a submenu; choosing one returns to the prompt.
    /// </summary>
    private void OpenVoiceLanguages()
    {
        if (_voiceLanguageButton is null || VoiceLanguages is null)
            return;

        var menu = new MenuFlyout { Placement = PlacementMode.TopEdgeAlignedRight };
        foreach (var language in VoiceLanguages)
            menu.Items.Add(CreateVoiceLanguageItem(language));

        var more = new MenuItem { Header = MoreVoiceLanguagesLabel, Icon = new Panel { Width = 14, Height = 14 } };
        foreach (var language in MoreVoiceLanguages ?? Array.Empty<object>())
            more.Items.Add(CreateVoiceLanguageItem(language));
        if (more.ItemCount > 0)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(more);
        }
        menu.ShowAt(_voiceLanguageButton);
    }

    private MenuItem CreateVoiceLanguageItem(object? language)
    {
        var item = new MenuItem
        {
            Header = language?.ToString(),
            // Every row keeps the check's column, so the names line up.
            Icon = Equals(language, SelectedVoiceLanguage)
                ? new StrataLineIcon { Width = 14, Height = 14, StrokeThickness = 2.4, Data = StrataIcons.Check }
                : new Panel { Width = 14, Height = 14 },
        };
        item.Click += (_, _) =>
        {
            SelectedVoiceLanguage = language;
            FocusInput();
        };
        return item;
    }

    /// <summary>The microphone: starts dictation, and finishes it while listening.</summary>
    private void HandleVoiceButton()
    {
        RaiseEvent(new RoutedEventArgs(VoiceRequestedEvent));
        CommandHelper.Execute(VoiceCommand, VoiceCommandParameter);
    }

    private void SyncVoiceButton()
    {
        if (_voiceButton is null)
            return;
        var label = IsRecording ? StopDictatingLabel : DictateLabel;
        AutomationProperties.SetName(_voiceButton, label);
        ToolTip.SetTip(_voiceButton, label);
    }

    private void OnRecordingChanged()
    {
        if (IsRecording)
        {
            _recordingStarted = Stopwatch.GetTimestamp();
            _shownRecordingSeconds = -1;
        }

        UpdateRecordingClock();
    }

    private void SyncVoice()
    {
        PseudoClasses.Set(":voice-status", !string.IsNullOrEmpty(VoiceStatus));
        PseudoClasses.Set(":has-voice-languages", VoiceLanguages is not null);
        if (_voiceLanguageText is not null)
            _voiceLanguageText.Text = SelectedVoiceLanguage?.ToString();
        var preview = VoicePreview;
        PseudoClasses.Set(":voice-preview", !string.IsNullOrWhiteSpace(preview));

        // Heard words read in their own direction, like the prompt itself.
        var rightToLeft = StrataTextDirectionDetector.Detect(preview) == FlowDirection.RightToLeft;
        foreach (var block in new[] { _voicePreviewInline, _voicePreviewBelow })
        {
            if (block is null)
                continue;
            block.FlowDirection = rightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            block.TextAlignment = rightToLeft ? TextAlignment.Right : TextAlignment.Left;
        }
    }

    private void CancelDictation() => CommandHelper.Execute(VoiceCancelCommand, null);

    /// <summary>Runs the recording clock only while recording in a live template, so it never ticks in the background.</summary>
    private void UpdateRecordingClock()
    {
        if (IsRecording && _voiceElapsed is not null && this.IsAttachedToVisualTree())
        {
            ShowRecordingTime();
            if (_recordingClock is null)
            {
                _recordingClock = new DispatcherTimer(RecordingClockInterval, DispatcherPriority.Normal, (_, _) => ShowRecordingTime());
                _recordingClock.Start();
            }
            return;
        }

        StopRecordingClock();
    }

    private void StopRecordingClock()
    {
        _recordingClock?.Stop();
        _recordingClock = null;
    }

    private void ShowRecordingTime()
    {
        if (_voiceElapsed is null)
            return;

        var seconds = (int)Stopwatch.GetElapsedTime(_recordingStarted).TotalSeconds;
        if (seconds == _shownRecordingSeconds)
            return;

        _shownRecordingSeconds = seconds;
        _voiceElapsed.Text = $"{seconds / 60}:{seconds % 60:00}";
    }
}
