using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

namespace StrataTheme.Controls;

/// <summary>
/// Container for a collection of <see cref="StrataFileAttachment"/> chips arranged
/// in a wrapping flow layout. Includes an optional "Add" button that raises
/// <see cref="AddRequested"/>.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataAttachmentList ShowAddButton="True"&gt;
///   &lt;controls:StrataFileAttachment FileName="report.pdf" FileSize="2.4 MB" Status="Completed" /&gt;
///   &lt;controls:StrataFileAttachment FileName="data.csv" FileSize="128 KB" Status="Uploading" /&gt;
/// &lt;/controls:StrataAttachmentList&gt;
/// </code>
/// <para><b>Template parts:</b> PART_ItemsHost (WrapPanel), PART_AddButton (Button).</para>
/// </remarks>
public class StrataAttachmentList : ItemsControl
{
    public static readonly StyledProperty<bool> ShowAddButtonProperty =
        AvaloniaProperty.Register<StrataAttachmentList, bool>(nameof(ShowAddButton), true);

    public static readonly StyledProperty<string> AddButtonTextProperty =
        AvaloniaProperty.Register<StrataAttachmentList, string>(nameof(AddButtonText), "+ Attach");

    public static readonly RoutedEvent<RoutedEventArgs> AddRequestedEvent =
        RoutedEvent.Register<StrataAttachmentList, RoutedEventArgs>(nameof(AddRequested), RoutingStrategies.Bubble);

    public bool ShowAddButton
    {
        get => GetValue(ShowAddButtonProperty);
        set => SetValue(ShowAddButtonProperty, value);
    }

    public string AddButtonText
    {
        get => GetValue(AddButtonTextProperty);
        set => SetValue(AddButtonTextProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? AddRequested
    {
        add => AddHandler(AddRequestedEvent, value);
        remove => RemoveHandler(AddRequestedEvent, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var addBtn = e.NameScope.Find<Button>("PART_AddButton");
        if (addBtn is not null)
            addBtn.Click += (_, _) => RaiseEvent(new RoutedEventArgs(AddRequestedEvent));
    }
}
