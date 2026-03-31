using System;
using System.Collections;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace StrataTheme.Controls;

/// <summary>
/// Compact model picker with grouped providers and per-model reasoning effort controls.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataModelPicker Models="{Binding Models}"
///                             SelectedModel="{Binding SelectedModel, Mode=TwoWay}"
///                             QualityLevels="{Binding QualityLevels}"
///                             SelectedQuality="{Binding SelectedQuality, Mode=TwoWay}" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_ModelPickerButton (Button), PART_ModelPickerPopup (Popup),
/// PART_ModelPickerList (StackPanel), PART_EffortSection (StackPanel).</para>
/// <para><b>Pseudo-classes:</b> :has-models, :has-quality, :model-picker-open.</para>
/// </remarks>
public class StrataModelPicker : TemplatedControl
{
    private Button? _modelPickerButton;
    private Popup? _modelPickerPopup;
    private StackPanel? _modelPickerList;
    private Border? _modelPickerChevronWrap;
    private StackPanel? _effortSection;
    private bool _suppressPickerRebuild;

    public static readonly StyledProperty<IEnumerable?> ModelsProperty =
        AvaloniaProperty.Register<StrataModelPicker, IEnumerable?>(nameof(Models));

    public static readonly StyledProperty<object?> SelectedModelProperty =
        AvaloniaProperty.Register<StrataModelPicker, object?>(nameof(SelectedModel));

    public static readonly StyledProperty<IDataTemplate?> ModelItemTemplateProperty =
        AvaloniaProperty.Register<StrataModelPicker, IDataTemplate?>(nameof(ModelItemTemplate));

    public static readonly StyledProperty<IEnumerable?> QualityLevelsProperty =
        AvaloniaProperty.Register<StrataModelPicker, IEnumerable?>(nameof(QualityLevels));

    public static readonly StyledProperty<object?> SelectedQualityProperty =
        AvaloniaProperty.Register<StrataModelPicker, object?>(nameof(SelectedQuality));

    static StrataModelPicker()
    {
        ModelsProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.EnsureSelectedValues();
            picker.Sync();
            picker.RefreshModelPickerIfOpen();
        });
        QualityLevelsProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) =>
        {
            picker.EnsureSelectedValues();
            picker.Sync();
            picker.RefreshModelPickerEffortIfOpen();
        });
        SelectedModelProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RefreshModelPickerSelectionIfOpen());
        SelectedQualityProperty.Changed.AddClassHandler<StrataModelPicker>((picker, _) => picker.RefreshModelPickerQualityIfOpen());
    }

    public IEnumerable? Models
    {
        get => GetValue(ModelsProperty);
        set => SetValue(ModelsProperty, value);
    }

    public object? SelectedModel
    {
        get => GetValue(SelectedModelProperty);
        set => SetValue(SelectedModelProperty, value);
    }

    public IDataTemplate? ModelItemTemplate
    {
        get => GetValue(ModelItemTemplateProperty);
        set => SetValue(ModelItemTemplateProperty, value);
    }

    public IEnumerable? QualityLevels
    {
        get => GetValue(QualityLevelsProperty);
        set => SetValue(QualityLevelsProperty, value);
    }

    public object? SelectedQuality
    {
        get => GetValue(SelectedQualityProperty);
        set => SetValue(SelectedQualityProperty, value);
    }

    public StrataModelPicker()
    {
        EnsureSelectedValues();
        Sync();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_modelPickerButton is not null)
            _modelPickerButton.Click -= OnModelPickerButtonClick;
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Opened -= OnModelPickerPopupOpened;
            _modelPickerPopup.Closed -= OnModelPickerPopupClosed;
        }

        base.OnApplyTemplate(e);

        _modelPickerButton = e.NameScope.Find<Button>("PART_ModelPickerButton");
        _modelPickerPopup = e.NameScope.Find<Popup>("PART_ModelPickerPopup");
        _modelPickerList = e.NameScope.Find<StackPanel>("PART_ModelPickerList");
        _modelPickerChevronWrap = e.NameScope.Find<Border>("PART_ModelPickerChevronWrap");
        _effortSection = e.NameScope.Find<StackPanel>("PART_EffortSection");

        if (_modelPickerButton is not null)
            _modelPickerButton.Click += OnModelPickerButtonClick;
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Opened += OnModelPickerPopupOpened;
            _modelPickerPopup.Closed += OnModelPickerPopupClosed;
        }

        EnsureSelectedValues();
        Sync();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_modelPickerButton is not null)
            _modelPickerButton.Click -= OnModelPickerButtonClick;
        if (_modelPickerPopup is not null)
        {
            _modelPickerPopup.Opened -= OnModelPickerPopupOpened;
            _modelPickerPopup.Closed -= OnModelPickerPopupClosed;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void OnModelPickerButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        ToggleModelPickerPopup();
    }

    private void OnModelPickerPopupOpened(object? sender, EventArgs e)
    {
        if (_modelPickerPopup is not null)
            ConfigurePopupTranslucency(_modelPickerPopup);
    }

    private void OnModelPickerPopupClosed(object? sender, EventArgs e)
    {
        PseudoClasses.Set(":model-picker-open", false);
        AnimateChevron(false);
    }

    private void ConfigurePopupTranslucency(Popup popup)
    {
        if (popup.Child is Border panel && panel.Background is ISolidColorBrush solid)
        {
            var color = solid.Color;
            panel.Background = new SolidColorBrush(Color.FromArgb(236, color.R, color.G, color.B));
        }
    }

    private void AnimateChevron(bool open)
    {
        if (_modelPickerChevronWrap is null)
            return;

        _modelPickerChevronWrap.RenderTransformOrigin = RelativePoint.Center;
        _modelPickerChevronWrap.RenderTransform = new RotateTransform(open ? 180 : 0);
    }

    private void ToggleModelPickerPopup()
    {
        if (_modelPickerPopup is null)
            return;

        if (_modelPickerPopup.IsOpen)
        {
            _modelPickerPopup.IsOpen = false;
            PseudoClasses.Set(":model-picker-open", false);
            AnimateChevron(false);
            return;
        }

        BuildModelPickerRows();
        _modelPickerPopup.IsOpen = true;
        PseudoClasses.Set(":model-picker-open", true);
        AnimateChevron(true);

        Dispatcher.UIThread.Post(() =>
        {
            if (_modelPickerList is null)
                return;

            foreach (var child in _modelPickerList.Children)
            {
                if (child is Border border && border.Classes.Contains("selected"))
                {
                    border.BringIntoView();
                    break;
                }
            }
        }, DispatcherPriority.Loaded);
    }

    private void RefreshModelPickerIfOpen()
    {
        if (_suppressPickerRebuild)
            return;

        if (_modelPickerPopup is { IsOpen: true })
            BuildModelPickerRows();
    }

    private void RefreshModelPickerSelectionIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true } || _suppressPickerRebuild)
            return;

        UpdateModelPickerSelectionVisuals(SelectedModel);
        RebuildEffortSection();
    }

    private void RefreshModelPickerEffortIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerEffortIfOpen, DispatcherPriority.Background);
            return;
        }

        RebuildEffortSection();
    }

    private void RefreshModelPickerQualityIfOpen()
    {
        if (_modelPickerPopup is not { IsOpen: true })
            return;

        if (_suppressPickerRebuild)
        {
            Dispatcher.UIThread.Post(RefreshModelPickerQualityIfOpen, DispatcherPriority.Background);
            return;
        }

        UpdateEffortActiveState();
    }

    private void BuildModelPickerRows()
    {
        if (_modelPickerList is null)
            return;

        _modelPickerList.Children.Clear();
        if (Models is null)
            return;

        string? lastGroup = null;
        foreach (var model in Models)
        {
            var modelName = model?.ToString() ?? string.Empty;
            var group = GetModelGroup(modelName);
            if (group != lastGroup)
            {
                if (lastGroup is not null)
                {
                    var separator = new Border { Height = 1, Margin = new Thickness(10, 5) };
                    separator.Classes.Add("model-picker-separator");
                    _modelPickerList.Children.Add(separator);
                }

                var header = new TextBlock
                {
                    Text = GetModelGroupLabel(group),
                    FontSize = 10,
                    FontWeight = FontWeight.SemiBold,
                    LetterSpacing = 0.8,
                    Margin = new Thickness(12, lastGroup is null ? 6 : 6, 12, 3)
                };
                header.Classes.Add("model-picker-group-header");
                _modelPickerList.Children.Add(header);
                lastGroup = group;
            }

            _modelPickerList.Children.Add(CreateModelRow(model, modelName, Equals(model, SelectedModel)));
        }

        RebuildEffortSection();
    }

    private void RebuildEffortSection()
    {
        if (_effortSection is null)
            return;

        _effortSection.Children.Clear();
        if (QualityLevels is null || SelectedModel is null)
            return;

        var separator = new Border { Height = 1, Margin = new Thickness(10, 4) };
        separator.Classes.Add("model-picker-separator");
        _effortSection.Children.Add(separator);

        var label = new TextBlock
        {
            Text = "REASONING EFFORT",
            FontSize = 9.5,
            FontWeight = FontWeight.SemiBold,
            LetterSpacing = 0.6,
            Margin = new Thickness(14, 4, 10, 4)
        };
        label.Classes.Add("effort-label");
        _effortSection.Children.Add(label);

        var toggleBorder = new Border
        {
            Margin = new Thickness(8, 0, 8, 4),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(3)
        };
        toggleBorder.Classes.Add("model-effort-toggle");

        var levels = QualityLevels.Cast<object?>().ToList();
        var grid = new Grid
        {
            ColumnDefinitions = ColumnDefinitions.Parse(string.Join(",", Enumerable.Range(0, levels.Count).Select(_ => "*")))
        };

        for (var index = 0; index < levels.Count; index++)
        {
            var level = levels[index];
            var button = new Button
            {
                Content = level?.ToString() ?? string.Empty,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            button.Classes.Add("effort-seg");
            if (Equals(level, SelectedQuality))
                button.Classes.Add("active");

            var capturedLevel = level;
            button.Click += (_, _) =>
            {
                _suppressPickerRebuild = true;
                SelectedQuality = capturedLevel;

                foreach (var child in grid.Children.OfType<Button>())
                {
                    if (Equals(child.Content, capturedLevel?.ToString()))
                    {
                        if (!child.Classes.Contains("active"))
                            child.Classes.Add("active");
                    }
                    else
                    {
                        child.Classes.Remove("active");
                    }
                }

                Dispatcher.UIThread.Post(() => _suppressPickerRebuild = false, DispatcherPriority.Background);
            };

            Grid.SetColumn(button, index);
            grid.Children.Add(button);
        }

        toggleBorder.Child = grid;
        _effortSection.Children.Add(toggleBorder);
    }

    private static string GetModelGroup(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        if (lower.StartsWith("claude", StringComparison.Ordinal))
            return "claude";
        if (lower.StartsWith("gpt", StringComparison.Ordinal))
            return "gpt";
        if (lower.StartsWith("o1", StringComparison.Ordinal)
            || lower.StartsWith("o3", StringComparison.Ordinal)
            || lower.StartsWith("o4", StringComparison.Ordinal))
            return "reasoning";
        if (lower.StartsWith("gemini", StringComparison.Ordinal))
            return "gemini";
        return "other";
    }

    private static string GetModelGroupLabel(string group) => group switch
    {
        "claude" => "ANTHROPIC",
        "gpt" => "OPENAI",
        "reasoning" => "REASONING",
        "gemini" => "GOOGLE",
        _ => "OTHER"
    };

    private static string GetModelTier(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        if (lower.Contains("opus", StringComparison.Ordinal))
            return "premium";
        if (lower.Contains("pro", StringComparison.Ordinal))
            return "premium";
        if (lower.Contains("haiku", StringComparison.Ordinal))
            return "fast";
        if (lower.Contains("mini", StringComparison.Ordinal))
            return "fast";
        if (lower.Contains("codex-max", StringComparison.Ordinal) || lower.Contains("codex max", StringComparison.Ordinal))
            return "max";
        if (lower.Contains("codex", StringComparison.Ordinal))
            return "code";
        if (lower.Contains("1m", StringComparison.Ordinal) || lower.Contains("2m", StringComparison.Ordinal))
            return "extended";
        if (IsReasoningCapable(modelId))
            return "reasoning";
        return string.Empty;
    }

    private static bool IsReasoningCapable(string modelId)
    {
        var lower = modelId.ToLowerInvariant();
        return lower.StartsWith("o1", StringComparison.Ordinal)
               || lower.StartsWith("o3", StringComparison.Ordinal)
               || lower.StartsWith("o4", StringComparison.Ordinal)
               || lower.Contains("think", StringComparison.Ordinal);
    }

    private Border CreateModelRow(object? model, string modelName, bool isSelected)
    {
        var grid = new Grid { ColumnDefinitions = ColumnDefinitions.Parse("20,*,Auto") };

        var dot = new Border
        {
            Width = 6,
            Height = 6,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            IsVisible = isSelected
        };
        dot.Classes.Add("model-picker-dot");
        Grid.SetColumn(dot, 0);
        grid.Children.Add(dot);

        if (ModelItemTemplate is not null)
        {
            var presenter = new ContentPresenter
            {
                Content = model,
                ContentTemplate = ModelItemTemplate,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(presenter, 1);
            grid.Children.Add(presenter);
        }
        else
        {
            var name = new TextBlock { Text = modelName };
            name.Classes.Add("model-name");
            Grid.SetColumn(name, 1);
            grid.Children.Add(name);
        }

        var tier = GetModelTier(modelName);
        if (!string.IsNullOrEmpty(tier))
        {
            var badge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = tier,
                    FontSize = 9.5,
                    FontWeight = FontWeight.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            badge.Classes.Add("model-tier-badge");
            badge.Classes.Add(tier switch
            {
                "premium" or "max" or "extended" => "tier-premium",
                "fast" => "tier-fast",
                "reasoning" => "tier-reasoning",
                _ => "tier-default"
            });
            Grid.SetColumn(badge, 2);
            grid.Children.Add(badge);
        }

        var border = new Border
        {
            Child = grid,
            Padding = new Thickness(8, 7, 10, 7),
            CornerRadius = new CornerRadius(8),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        border.Classes.Add("model-picker-row");
        if (isSelected)
            border.Classes.Add("selected");

        var capturedModel = model;
        border.PointerPressed += (_, args) =>
        {
            if (!args.GetCurrentPoint(border).Properties.IsLeftButtonPressed)
                return;

            args.Handled = true;
            UpdateModelPickerSelection(capturedModel);
        };

        return border;
    }

    private void UpdateModelPickerSelection(object? newModel)
    {
        _suppressPickerRebuild = true;
        SelectedModel = newModel;
        UpdateModelPickerSelectionVisuals(newModel);

        Dispatcher.UIThread.Post(() =>
        {
            _suppressPickerRebuild = false;
            RefreshModelPickerEffortIfOpen();
        }, DispatcherPriority.Background);
    }

    private void UpdateModelPickerSelectionVisuals(object? selectedModel)
    {
        if (_modelPickerList is null)
            return;

        foreach (var child in _modelPickerList.Children)
        {
            if (child is not Border border || !border.Classes.Contains("model-picker-row"))
                continue;
            if (border.Child is not Grid grid || grid.Children.Count == 0 || grid.Children[0] is not Border dot)
                continue;

            var isNowSelected = false;
            for (var index = 1; index < grid.Children.Count; index++)
            {
                switch (grid.Children[index])
                {
                    case ContentPresenter presenter when Equals(presenter.Content, selectedModel):
                        isNowSelected = true;
                        break;
                    case TextBlock textBlock when textBlock.Classes.Contains("model-name")
                                               && Equals(textBlock.Text, selectedModel?.ToString()):
                        isNowSelected = true;
                        break;
                }

                if (isNowSelected)
                    break;
            }

            dot.IsVisible = isNowSelected;
            if (isNowSelected)
            {
                if (!border.Classes.Contains("selected"))
                    border.Classes.Add("selected");
            }
            else
            {
                border.Classes.Remove("selected");
            }
        }
    }

    private void UpdateEffortActiveState()
    {
        if (_effortSection is null)
            return;

        if (QualityLevels is not null && _effortSection.Children.Count == 0)
        {
            RebuildEffortSection();
            return;
        }

        if (QualityLevels is null && _effortSection.Children.Count > 0)
        {
            _effortSection.Children.Clear();
            return;
        }

        foreach (var child in _effortSection.Children)
        {
            if (child is not Border border || !border.Classes.Contains("model-effort-toggle") || border.Child is not Grid grid)
                continue;

            var selectedQuality = SelectedQuality?.ToString();
            foreach (var button in grid.Children.OfType<Button>())
            {
                if (button.Content?.ToString() == selectedQuality)
                {
                    if (!button.Classes.Contains("active"))
                        button.Classes.Add("active");
                }
                else
                {
                    button.Classes.Remove("active");
                }
            }

            break;
        }
    }

    private void EnsureSelectedValues()
    {
        if (Models is not null && SelectedModel is null)
        {
            foreach (var item in Models)
            {
                SelectedModel = item;
                break;
            }
        }

        Sync();
    }

    private void Sync()
    {
        PseudoClasses.Set(":has-models", Models is not null);
        PseudoClasses.Set(":has-quality", QualityLevels is not null);
    }
}
