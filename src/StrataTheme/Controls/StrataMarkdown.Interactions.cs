using System;
using System.Collections.Generic;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Logging;

namespace StrataTheme.Controls;

public partial class StrataMarkdown
{
    public static readonly StyledProperty<IReadOnlyDictionary<string, string>?> InteractionStatesProperty =
        AvaloniaProperty.Register<StrataMarkdown, IReadOnlyDictionary<string, string>?>(nameof(InteractionStates));
    public static readonly StyledProperty<bool> CanSubmitInteractionsProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(CanSubmitInteractions));
    public static readonly StyledProperty<bool> InteractionsHostedProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(InteractionsHosted));
    public static readonly StyledProperty<string> InteractionLanguageProperty =
        AvaloniaProperty.Register<StrataMarkdown, string>(nameof(InteractionLanguage), "en");
    public static readonly StyledProperty<bool> InteractionTouchModeProperty =
        AvaloniaProperty.Register<StrataMarkdown, bool>(nameof(InteractionTouchMode));
    public static readonly StyledProperty<Panel?> InteractionPickerHostProperty =
        AvaloniaProperty.Register<StrataMarkdown, Panel?>(nameof(InteractionPickerHost));

    private readonly Dictionary<string, StrataInteractiveBlock> _interactionCache = new();
    private readonly HashSet<string> _interactionKeysUsed = new();
    private readonly HashSet<string> _interactionIdsUsed = new();
    private readonly Dictionary<string, string> _localInteractionStates = new();

    public IReadOnlyDictionary<string, string>? InteractionStates { get => GetValue(InteractionStatesProperty); set => SetValue(InteractionStatesProperty, value); }
    public bool CanSubmitInteractions { get => GetValue(CanSubmitInteractionsProperty); set => SetValue(CanSubmitInteractionsProperty, value); }
    public bool InteractionsHosted { get => GetValue(InteractionsHostedProperty); set => SetValue(InteractionsHostedProperty, value); }
    public string InteractionLanguage { get => GetValue(InteractionLanguageProperty); set => SetValue(InteractionLanguageProperty, value); }
    public bool InteractionTouchMode { get => GetValue(InteractionTouchModeProperty); set => SetValue(InteractionTouchModeProperty, value); }
    public Panel? InteractionPickerHost { get => GetValue(InteractionPickerHostProperty); set => SetValue(InteractionPickerHostProperty, value); }

    private static void InitializeInteractionProperties()
    {
        InteractionStatesProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.RefreshInteractions());
        CanSubmitInteractionsProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.RefreshInteractions());
        InteractionsHostedProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.RefreshInteractions());
        InteractionLanguageProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.RefreshInteractions());
        InteractionTouchModeProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.RefreshInteractions());
        InteractionPickerHostProperty.Changed.AddClassHandler<StrataMarkdown>((control, _) => control.RefreshInteractions());
    }

    private void RefreshInteractions()
    {
        foreach (var control in _interactionCache.Values)
        {
            control.CanSubmit = CanSubmitInteractions;
            control.IsHosted = InteractionsHosted;
            control.Language = InteractionLanguage;
            control.TouchMode = InteractionTouchMode;
            control.PickerHost = InteractionPickerHost;
            if (control.Definition is { } definition && InteractionStates?.TryGetValue(definition.Id, out var state) == true)
                control.StateJson = state;
        }
    }

    private void OnInteractiveRequest(object? sender, InteractiveRequestEventArgs request)
    {
        if (request.Action is null)
            _localInteractionStates[request.Definition.Id] = request.StateJson;
    }

    internal string GetCopyMarkdown() =>
        MarkdownClipboardFormatter.ToPortableMarkdown(Markdown ?? "", GetCopyInteractionStates());

    private IReadOnlyDictionary<string, string> GetCopyInteractionStates()
    {
        var states = new Dictionary<string, string>(_localInteractionStates);
        if (InteractionStates is not null)
        {
            foreach (var state in InteractionStates)
                states[state.Key] = state.Value;
        }
        foreach (var control in _interactionCache.Values)
        {
            if (control.Definition is { } definition && !string.IsNullOrEmpty(control.StateJson))
                states[definition.Id] = control.StateJson;
        }
        return states;
    }

    private static string InteractionCacheKey(MdBlock block) => block.Language.ToLowerInvariant() + ":" + block.Content.Trim();

    private Control CreateInteractiveControl(MdBlock block)
    {
        if (!block.IsClosed)
            return GetBlockPlaceholder("...");
        var key = InteractionCacheKey(block);
        try
        {
            var definition = _interactionCache.TryGetValue(key, out var cached)
                ? cached.Definition! : InteractiveDefinition.Parse(block.Language, block.Content);
            if (!_interactionIdsUsed.Add(definition.Id))
                throw new FormatException("Each interaction in an answer needs a unique id.");

            _interactionKeysUsed.Add(key);
            if (cached is not null)
            {
                DetachFromForeignParent(cached);
                return cached;
            }

            var control = new StrataInteractiveBlock
            {
                Name = "Interaction_" + StrataInteractiveBlock.SafeName(definition.Id),
                Definition = definition,
                StateJson = InteractionStates?.GetValueOrDefault(definition.Id)
                    ?? _localInteractionStates.GetValueOrDefault(definition.Id),
                CanSubmit = CanSubmitInteractions,
                IsHosted = InteractionsHosted,
                Language = InteractionLanguage,
                TouchMode = InteractionTouchMode,
                PickerHost = InteractionPickerHost
            };
            _interactionCache[key] = control;
            return control;
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
            Logger.TryGet(LogEventLevel.Warning, "InteractiveResponse")?.Log(this,
                "Invalid {Kind} response: {Error}", block.Language, error.Message);
            var content = new StackPanel { Spacing = 6 };
            content.Children.Add(new TextBlock
            {
                Text = InteractiveLabels.ForLanguage(InteractionLanguage)["definitionError"],
                TextWrapping = Avalonia.Media.TextWrapping.Wrap
            });
            content.Children.Add(CreateCodeBlockControl(block.Content, "json"));
            return new Border { Child = content };
        }
    }

    private void TrackInteraction(MdBlock block)
    {
        var key = InteractionCacheKey(block);
        _interactionKeysUsed.Add(key);
        if (_interactionCache.TryGetValue(key, out var control) && control.Definition is { } definition)
            _interactionIdsUsed.Add(definition.Id);
    }
}
