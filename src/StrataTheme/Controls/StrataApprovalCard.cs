using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace StrataTheme.Controls;

/// <summary>Where an approval request stands.</summary>
public enum StrataApprovalStatus
{
    /// <summary>Waiting for a person.</summary>
    Pending,
    /// <summary>Allowed: by the person, or for them by a rule or reviewer.</summary>
    Approved,
    /// <summary>Denied.</summary>
    Denied,
    /// <summary>Set aside without an answer, for instance because nobody was there.</summary>
    Skipped,
    /// <summary>No longer needed: the work stopped or another surface answered.</summary>
    Expired
}

/// <summary>
/// An inline request for permission: what an assistant wants to do, why, and the choices to allow or
/// deny it. Once answered it folds into a single receipt line, so the transcript keeps a record of
/// every decision without the weight of the card.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataApprovalCard Title="Run a command" Intention="Push the branch"
///                                Subject="git push origin main" IsSubjectCode="True"
///                                StatusText="Needs you" Status="Pending"&gt;
///   &lt;controls:StrataApprovalCard.Actions&gt;…buttons…&lt;/controls:StrataApprovalCard.Actions&gt;
/// &lt;/controls:StrataApprovalCard&gt;
/// </code>
/// <para><b>Template parts:</b> PART_Card (Border), PART_Receipt (Border), PART_DetailsToggle (ToggleButton).</para>
/// <para><b>Pseudo-classes:</b> :pending, :approved, :denied, :skipped, :expired, :decided,
/// :has-note, :has-warning, :has-details, :details-open, :code, :escalation.</para>
/// </remarks>
public class StrataApprovalCard : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> IconProperty =
        AvaloniaProperty.Register<StrataApprovalCard, Geometry?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(Title));

    public static readonly StyledProperty<string?> StatusTextProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(StatusText));

    public static readonly StyledProperty<string?> IntentionProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(Intention));

    public static readonly StyledProperty<string?> SubjectProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(Subject));

    public static readonly StyledProperty<bool> IsSubjectCodeProperty =
        AvaloniaProperty.Register<StrataApprovalCard, bool>(nameof(IsSubjectCode));

    public static readonly StyledProperty<string?> NoteLabelProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(NoteLabel));

    public static readonly StyledProperty<string?> NoteProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(Note));

    public static readonly StyledProperty<string?> WarningProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(Warning));

    public static readonly StyledProperty<bool> IsEscalationProperty =
        AvaloniaProperty.Register<StrataApprovalCard, bool>(nameof(IsEscalation));

    public static readonly StyledProperty<object?> DetailsProperty =
        AvaloniaProperty.Register<StrataApprovalCard, object?>(nameof(Details));

    public static readonly StyledProperty<string?> DetailsLabelProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(DetailsLabel), "Details");

    public static readonly StyledProperty<bool> IsDetailsExpandedProperty =
        AvaloniaProperty.Register<StrataApprovalCard, bool>(nameof(IsDetailsExpanded));

    public static readonly StyledProperty<bool> ShowDetailsProperty =
        AvaloniaProperty.Register<StrataApprovalCard, bool>(nameof(ShowDetails), true);

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<StrataApprovalCard, object?>(nameof(Actions));

    public static readonly StyledProperty<string?> ErrorProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(Error));

    public static readonly StyledProperty<StrataApprovalStatus> StatusProperty =
        AvaloniaProperty.Register<StrataApprovalCard, StrataApprovalStatus>(nameof(Status));

    public static readonly StyledProperty<string?> ReceiptProperty =
        AvaloniaProperty.Register<StrataApprovalCard, string?>(nameof(Receipt));

    static StrataApprovalCard()
    {
        StatusProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        NoteProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        WarningProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        DetailsProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        IsDetailsExpandedProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        ShowDetailsProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        IsSubjectCodeProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        IsEscalationProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        SubjectProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        IntentionProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
        ErrorProperty.Changed.AddClassHandler<StrataApprovalCard>((card, _) => card.UpdatePseudoClasses());
    }

    public StrataApprovalCard() => UpdatePseudoClasses();

    /// <summary>Line icon for the kind of action.</summary>
    public Geometry? Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }

    /// <summary>What the assistant wants to do, in a few words ("Run a command").</summary>
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    /// <summary>The pill beside the title while the request waits ("Needs you").</summary>
    public string? StatusText { get => GetValue(StatusTextProperty); set => SetValue(StatusTextProperty, value); }

    /// <summary>Why, in the assistant's words.</summary>
    public string? Intention { get => GetValue(IntentionProperty); set => SetValue(IntentionProperty, value); }

    /// <summary>The exact thing to allow: a command, a file, a tool.</summary>
    public string? Subject { get => GetValue(SubjectProperty); set => SetValue(SubjectProperty, value); }

    /// <summary>Shows <see cref="Subject"/> as code, for commands.</summary>
    public bool IsSubjectCode { get => GetValue(IsSubjectCodeProperty); set => SetValue(IsSubjectCodeProperty, value); }

    /// <summary>Who wrote <see cref="Note"/> ("Reviewer").</summary>
    public string? NoteLabel { get => GetValue(NoteLabelProperty); set => SetValue(NoteLabelProperty, value); }

    /// <summary>A second opinion on the request, such as an automatic reviewer's reason.</summary>
    public string? Note { get => GetValue(NoteProperty); set => SetValue(NoteProperty, value); }

    /// <summary>A risk the person should weigh before allowing it.</summary>
    public string? Warning { get => GetValue(WarningProperty); set => SetValue(WarningProperty, value); }

    /// <summary>Marks a request to step outside a sandbox or other boundary.</summary>
    public bool IsEscalation { get => GetValue(IsEscalationProperty); set => SetValue(IsEscalationProperty, value); }

    /// <summary>Full detail behind a disclosure: arguments, a diff, a file preview.</summary>
    public object? Details { get => GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }

    public string? DetailsLabel { get => GetValue(DetailsLabelProperty); set => SetValue(DetailsLabelProperty, value); }

    public bool IsDetailsExpanded { get => GetValue(IsDetailsExpandedProperty); set => SetValue(IsDetailsExpandedProperty, value); }

    /// <summary>Whether the Details disclosure is offered; it never is without <see cref="Details"/>.</summary>
    public bool ShowDetails { get => GetValue(ShowDetailsProperty); set => SetValue(ShowDetailsProperty, value); }

    /// <summary>The choices, supplied by the host.</summary>
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    /// <summary>Why answering failed, shown above the choices.</summary>
    public string? Error { get => GetValue(ErrorProperty); set => SetValue(ErrorProperty, value); }

    public StrataApprovalStatus Status { get => GetValue(StatusProperty); set => SetValue(StatusProperty, value); }

    /// <summary>The decision, once made ("Allowed by you").</summary>
    public string? Receipt { get => GetValue(ReceiptProperty); set => SetValue(ReceiptProperty, value); }

    private void UpdatePseudoClasses()
    {
        var status = Status;
        PseudoClasses.Set(":pending", status == StrataApprovalStatus.Pending);
        PseudoClasses.Set(":approved", status == StrataApprovalStatus.Approved);
        PseudoClasses.Set(":denied", status == StrataApprovalStatus.Denied);
        PseudoClasses.Set(":skipped", status == StrataApprovalStatus.Skipped);
        PseudoClasses.Set(":expired", status == StrataApprovalStatus.Expired);
        PseudoClasses.Set(":decided", status != StrataApprovalStatus.Pending);
        PseudoClasses.Set(":has-note", !string.IsNullOrWhiteSpace(Note));
        PseudoClasses.Set(":has-warning", !string.IsNullOrWhiteSpace(Warning));
        PseudoClasses.Set(":has-details", ShowDetails && Details is not null and not "");
        PseudoClasses.Set(":details-open", IsDetailsExpanded);
        PseudoClasses.Set(":has-subject", !string.IsNullOrWhiteSpace(Subject));
        PseudoClasses.Set(":has-intention", !string.IsNullOrWhiteSpace(Intention));
        PseudoClasses.Set(":has-error", !string.IsNullOrWhiteSpace(Error));
        PseudoClasses.Set(":code", IsSubjectCode);
        PseudoClasses.Set(":escalation", IsEscalation);
    }
}
