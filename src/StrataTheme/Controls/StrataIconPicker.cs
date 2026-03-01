using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StrataTheme.Controls;

/// <summary>
/// Emoji icon picker with categorized browsing and live search.
/// Displays a grid of emoji icons organized by category, with a search box to filter.
/// The selected emoji is exposed via <see cref="SelectedIcon"/>.
/// </summary>
/// <remarks>
/// <para><b>XAML usage:</b></para>
/// <code>
/// &lt;controls:StrataIconPicker SelectedIcon="🎯" Columns="8" /&gt;
/// </code>
/// <para><b>Template parts:</b> PART_SearchBox (TextBox), PART_CategoryList (ItemsControl),
/// PART_IconGrid (ItemsControl), PART_Preview (TextBlock).</para>
/// <para><b>Pseudo-classes:</b> :has-selection, :searching.</para>
/// </remarks>
public class StrataIconPicker : TemplatedControl
{
    private TextBox? _searchBox;
    private ItemsControl? _categoryList;
    private ItemsControl? _iconGrid;

    /// <summary>The currently selected emoji icon.</summary>
    public static readonly StyledProperty<string?> SelectedIconProperty =
        AvaloniaProperty.Register<StrataIconPicker, string?>(nameof(SelectedIcon));

    /// <summary>Number of columns in the icon grid.</summary>
    public static readonly StyledProperty<int> ColumnsProperty =
        AvaloniaProperty.Register<StrataIconPicker, int>(nameof(Columns), 8);

    /// <summary>The currently active category filter.</summary>
    public static readonly StyledProperty<string> ActiveCategoryProperty =
        AvaloniaProperty.Register<StrataIconPicker, string>(nameof(ActiveCategory), "Smileys");

    /// <summary>The current search query text.</summary>
    public static readonly StyledProperty<string?> SearchQueryProperty =
        AvaloniaProperty.Register<StrataIconPicker, string?>(nameof(SearchQuery));

    /// <summary>The list of visible icons based on category/search.</summary>
    public static readonly DirectProperty<StrataIconPicker, IReadOnlyList<EmojiItem>> VisibleIconsProperty =
        AvaloniaProperty.RegisterDirect<StrataIconPicker, IReadOnlyList<EmojiItem>>(
            nameof(VisibleIcons), o => o.VisibleIcons);

    /// <summary>The list of category names.</summary>
    public static readonly DirectProperty<StrataIconPicker, IReadOnlyList<string>> CategoriesProperty =
        AvaloniaProperty.RegisterDirect<StrataIconPicker, IReadOnlyList<string>>(
            nameof(Categories), o => o.Categories);

    /// <summary>Raised when a new icon is selected/picked.</summary>
    public static readonly RoutedEvent<RoutedEventArgs> IconSelectedEvent =
        RoutedEvent.Register<StrataIconPicker, RoutedEventArgs>(nameof(IconSelected), RoutingStrategies.Bubble);

    public string? SelectedIcon
    {
        get => GetValue(SelectedIconProperty);
        set => SetValue(SelectedIconProperty, value);
    }

    public int Columns
    {
        get => GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public string ActiveCategory
    {
        get => GetValue(ActiveCategoryProperty);
        set => SetValue(ActiveCategoryProperty, value);
    }

    public string? SearchQuery
    {
        get => GetValue(SearchQueryProperty);
        set => SetValue(SearchQueryProperty, value);
    }

    private IReadOnlyList<EmojiItem> _visibleIcons = Array.Empty<EmojiItem>();
    public IReadOnlyList<EmojiItem> VisibleIcons
    {
        get => _visibleIcons;
        private set => SetAndRaise(VisibleIconsProperty, ref _visibleIcons, value);
    }

    private IReadOnlyList<string> _categories = Array.Empty<string>();
    public IReadOnlyList<string> Categories
    {
        get => _categories;
        private set => SetAndRaise(CategoriesProperty, ref _categories, value);
    }

    public event EventHandler<RoutedEventArgs>? IconSelected
    {
        add => AddHandler(IconSelectedEvent, value);
        remove => RemoveHandler(IconSelectedEvent, value);
    }

    static StrataIconPicker()
    {
        SelectedIconProperty.Changed.AddClassHandler<StrataIconPicker>((picker, _) => picker.UpdateHasSelection());
        ActiveCategoryProperty.Changed.AddClassHandler<StrataIconPicker>((picker, _) => picker.RefreshVisibleIcons());
        SearchQueryProperty.Changed.AddClassHandler<StrataIconPicker>((picker, _) =>
        {
            picker.PseudoClasses.Set(":searching", !string.IsNullOrWhiteSpace(picker.SearchQuery));
            picker.RefreshVisibleIcons();
        });
    }

    public StrataIconPicker()
    {
        Categories = EmojiCatalog.CategoryNames;
        RefreshVisibleIcons();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _searchBox = e.NameScope.Find<TextBox>("PART_SearchBox");
        _categoryList = e.NameScope.Find<ItemsControl>("PART_CategoryList");
        _iconGrid = e.NameScope.Find<ItemsControl>("PART_IconGrid");

        if (_searchBox is not null)
            _searchBox.TextChanged += OnSearchTextChanged;

        if (_categoryList is not null)
            _categoryList.AddHandler(Button.ClickEvent, OnCategoryClick);

        if (_iconGrid is not null)
            _iconGrid.AddHandler(Button.ClickEvent, OnEmojiClick);
    }

    private void OnCategoryClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button btn && btn.Tag is string category)
            SetCategory(category);
    }

    private void OnEmojiClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Button btn && btn.Tag is string emoji)
            SelectIcon(emoji);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape && !string.IsNullOrEmpty(SearchQuery))
        {
            SearchQuery = null;
            if (_searchBox is not null)
                _searchBox.Text = null;
            e.Handled = true;
        }
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        SearchQuery = _searchBox?.Text;
    }

    internal void SelectIcon(string emoji)
    {
        SelectedIcon = emoji;
        RaiseEvent(new RoutedEventArgs(IconSelectedEvent, this));
    }

    internal void SetCategory(string category)
    {
        ActiveCategory = category;
        SearchQuery = null;
        if (_searchBox is not null)
            _searchBox.Text = null;
    }

    private void RefreshVisibleIcons()
    {
        var query = SearchQuery?.Trim();
        if (!string.IsNullOrEmpty(query))
        {
            VisibleIcons = EmojiCatalog.Search(query);
        }
        else
        {
            VisibleIcons = EmojiCatalog.GetByCategory(ActiveCategory);
        }
    }

    private void UpdateHasSelection()
    {
        PseudoClasses.Set(":has-selection", !string.IsNullOrEmpty(SelectedIcon));
    }
}

/// <summary>Represents a single emoji entry with its icon and descriptive keyword.</summary>
public record EmojiItem(string Emoji, string Keyword);

/// <summary>Static catalog of emoji icons organized by category with search capability.</summary>
internal static class EmojiCatalog
{
    private static readonly Dictionary<string, EmojiItem[]> Data = new()
    {
        ["Smileys"] = new EmojiItem[]
        {
            new("😀", "grinning"), new("😁", "beaming"), new("😂", "laughing"),
            new("🤣", "rofl"), new("😃", "smiley"), new("😄", "smile"),
            new("😅", "sweat smile"), new("😆", "squinting"), new("😉", "wink"),
            new("😊", "blush"), new("😋", "yummy"), new("😎", "cool"),
            new("😍", "heart eyes"), new("🥰", "love"), new("😘", "kiss"),
            new("😗", "kissing"), new("🤗", "hugging"), new("🤔", "thinking"),
            new("🤨", "raised brow"), new("😐", "neutral"), new("😑", "blank"),
            new("😶", "silent"), new("🙄", "eye roll"), new("😏", "smirk"),
            new("😣", "persevere"), new("😥", "sad relief"), new("😮", "open mouth"),
            new("🤐", "zipper"), new("😯", "hushed"), new("😪", "sleepy"),
            new("😫", "tired"), new("🥱", "yawning"), new("😴", "sleeping"),
        },
        ["Gestures"] = new EmojiItem[]
        {
            new("👍", "thumbs up"), new("👎", "thumbs down"), new("👏", "clap"),
            new("🙌", "raised hands"), new("🤝", "handshake"), new("🙏", "pray"),
            new("✌️", "peace"), new("🤞", "crossed fingers"), new("🤟", "love you"),
            new("🤘", "rock on"), new("👌", "ok"), new("🤌", "pinched"),
            new("👋", "wave"), new("✋", "hand"), new("🖐️", "spread hand"),
            new("🖖", "vulcan"), new("💪", "muscle"), new("🦾", "robot arm"),
            new("👈", "left"), new("👉", "right"), new("👆", "up"),
            new("👇", "down"), new("☝️", "index up"), new("✊", "fist"),
        },
        ["Hearts"] = new EmojiItem[]
        {
            new("❤️", "red heart"), new("🧡", "orange heart"), new("💛", "yellow heart"),
            new("💚", "green heart"), new("💙", "blue heart"), new("💜", "purple heart"),
            new("🖤", "black heart"), new("🤍", "white heart"), new("🤎", "brown heart"),
            new("💔", "broken heart"), new("💕", "two hearts"), new("💖", "sparkling heart"),
            new("💗", "growing heart"), new("💘", "arrow heart"), new("💝", "gift heart"),
            new("💞", "revolving hearts"), new("🫶", "heart hands"),
        },
        ["Animals"] = new EmojiItem[]
        {
            new("🐶", "dog"), new("🐱", "cat"), new("🐭", "mouse"),
            new("🐹", "hamster"), new("🐰", "rabbit"), new("🦊", "fox"),
            new("🐻", "bear"), new("🐼", "panda"), new("🐨", "koala"),
            new("🐯", "tiger"), new("🦁", "lion"), new("🐮", "cow"),
            new("🐷", "pig"), new("🐸", "frog"), new("🐵", "monkey"),
            new("🐔", "chicken"), new("🐧", "penguin"), new("🐦", "bird"),
            new("🦅", "eagle"), new("🦉", "owl"), new("🐝", "bee"),
            new("🦋", "butterfly"), new("🐛", "bug"), new("🐌", "snail"),
        },
        ["Food"] = new EmojiItem[]
        {
            new("🍎", "apple"), new("🍐", "pear"), new("🍊", "orange"),
            new("🍋", "lemon"), new("🍌", "banana"), new("🍉", "watermelon"),
            new("🍇", "grapes"), new("🍓", "strawberry"), new("🫐", "blueberries"),
            new("🍑", "peach"), new("🥝", "kiwi"), new("🍅", "tomato"),
            new("🌽", "corn"), new("🍕", "pizza"), new("🍔", "burger"),
            new("🍟", "fries"), new("🌭", "hotdog"), new("🍩", "donut"),
            new("🍪", "cookie"), new("🎂", "cake"), new("🍰", "pie"),
            new("☕", "coffee"), new("🍵", "tea"), new("🧃", "juice"),
        },
        ["Travel"] = new EmojiItem[]
        {
            new("🚗", "car"), new("🚕", "taxi"), new("🚌", "bus"),
            new("🚎", "trolley"), new("🏎️", "race car"), new("🚓", "police car"),
            new("🚑", "ambulance"), new("🚒", "fire truck"), new("✈️", "airplane"),
            new("🚀", "rocket"), new("🛸", "ufo"), new("🚁", "helicopter"),
            new("⛵", "sailboat"), new("🚂", "train"), new("🏠", "house"),
            new("🏢", "office"), new("🏥", "hospital"), new("🏫", "school"),
            new("⛪", "church"), new("🗽", "liberty"), new("🗼", "tower"),
            new("🌍", "earth"), new("🌙", "moon"), new("⭐", "star"),
        },
        ["Objects"] = new EmojiItem[]
        {
            new("💡", "idea"), new("🔑", "key"), new("🔒", "lock"),
            new("🔓", "unlock"), new("📱", "phone"), new("💻", "laptop"),
            new("🖥️", "desktop"), new("🖨️", "printer"), new("📷", "camera"),
            new("🎥", "video"), new("📞", "telephone"), new("📧", "email"),
            new("📎", "paperclip"), new("📌", "pin"), new("📁", "folder"),
            new("📂", "open folder"), new("📝", "memo"), new("📖", "book"),
            new("🔔", "bell"), new("🏷️", "label"), new("⚙️", "gear"),
            new("🔧", "wrench"), new("🔨", "hammer"), new("🛠️", "tools"),
        },
        ["Symbols"] = new EmojiItem[]
        {
            new("✅", "check"), new("❌", "cross"), new("⚠️", "warning"),
            new("❓", "question"), new("❗", "exclamation"), new("💯", "hundred"),
            new("🔴", "red circle"), new("🟢", "green circle"), new("🔵", "blue circle"),
            new("🟡", "yellow circle"), new("🟠", "orange circle"), new("🟣", "purple circle"),
            new("⬛", "black square"), new("⬜", "white square"), new("🔶", "diamond"),
            new("🏁", "flag"), new("🎯", "target"), new("♻️", "recycle"),
            new("⚡", "lightning"), new("🔥", "fire"), new("💧", "water"),
            new("🌈", "rainbow"), new("☀️", "sun"), new("🌟", "glowing star"),
        },
        ["Flags"] = new EmojiItem[]
        {
            new("🏳️", "white flag"), new("🏴", "black flag"), new("🚩", "red flag"),
            new("🎌", "crossed flags"), new("🏁", "checkered flag"),
            new("🏳️‍🌈", "rainbow flag"), new("🏴‍☠️", "pirate flag"),
        },
    };

    public static IReadOnlyList<string> CategoryNames { get; } = Data.Keys.ToArray();

    public static IReadOnlyList<EmojiItem> GetByCategory(string category)
    {
        return Data.TryGetValue(category, out var items) ? items : Array.Empty<EmojiItem>();
    }

    public static IReadOnlyList<EmojiItem> Search(string query)
    {
        var lower = query.ToLowerInvariant();
        var results = new List<EmojiItem>();
        foreach (var kvp in Data)
        {
            foreach (var item in kvp.Value)
            {
                if (item.Keyword.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                    item.Emoji.Contains(query, StringComparison.Ordinal))
                {
                    results.Add(item);
                }
            }
        }
        return results;
    }
}
