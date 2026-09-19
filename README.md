# Strata UI Theme for Avalonia

A modern, token-driven Avalonia theme designed for professional desktop applications.  
Strata UI prioritizes readability, consistency, and accessible contrast across Light, Dark, and High Contrast variants.

---

## Quick Start

### 1. Add the project reference

```xml
<ProjectReference Include="path/to/StrataTheme/StrataTheme.csproj" />
```

Or, if published as a NuGet package:

```xml
<PackageReference Include="StrataUI.Theme" Version="0.3.32" />
```

### 2. Apply the theme in `App.axaml`

```xml
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="YourApp.App"
             RequestedThemeVariant="Light">
  <Application.Styles>
    <SimpleTheme />
    <StyleInclude Source="avares://StrataTheme/StrataTheme.axaml" />
  </Application.Styles>
</Application>
```

> **Note:** `SimpleTheme` provides base templates for controls that Strata does not fully re-template. Strata's styles layer on top and override all visual properties.

---

## Shared code diffs

`StrataDiffView` renders unified Git patches, snapshots, or captured edits with change colors,
old/new line numbers and hunk navigation. Desktop uses TextMate grammars; Android and WebAssembly
use native-free lexical highlighting for keywords, strings, numbers, comments and type names.
Set `TouchMode="True"` and `CodeFontSize="14"` for touch-sized navigation and scroll-safe code.
Touch panning clamps each axis independently, so minor finger drift at the top or bottom
does not block horizontal scrolling through long lines.

```xml
<sc:StrataDiffView UnifiedDiffText="{Binding DiffText}" FilePath="{Binding FilePath}"
                   TouchMode="True" CodeFontSize="14" />
```

The parser/models live in `StrataTheme.Diff`; applications do not need their own copy.

## Running activity previews

`StrataThink.PreviewContent` displays lightweight activity rows below the progress header
while `IsActive` is true and the card is collapsed. Clicking the header or pressing Enter/Space
replaces the preview with the full `Content`. Finishing returns a collapsed card to its compact
pill; reasoning controls without a preview are unchanged. Preview and detail content are only
hosted while visible. Set `MaxWidth` to bound the responsive card in a transcript column.

```xml
<sc:StrataThink Label="{Binding Label}" Meta="{Binding Meta}"
                IsActive="{Binding IsActive}" IsExpanded="{Binding IsExpanded}"
                ProgressValue="{Binding ProgressValue}" MaxWidth="560">
  <sc:StrataThink.PreviewContent>
    <ItemsControl ItemsSource="{Binding ActivityPreview}" />
  </sc:StrataThink.PreviewContent>
  <sc:StrataThink.Content>
    <ItemsControl ItemsSource="{Binding AllOperations}" />
  </sc:StrataThink.Content>
</sc:StrataThink>
```

## Composer focus and entrance motion

`StrataChatComposer` keeps the thin Stratum gradient under its input, without a gradient border
around the composer. On focus, the line draws from left to right over 400 ms; a contrasting,
feathered highlight then travels for two seconds and stops, leaving the static line until focus
leaves. Refocusing replays the sequence; typing and resizing do not extend it. Motion does not
change layout. The sweep and its pending timers stop on blur, hiding, or detachment. Apply
`Classes="motion-disabled"` to show the static focus line immediately without either animation.

`StrataTheme.Animation.SlideFadeEntrance.Play(host)` gives an attached, visible entrance host
a short rise and fade without animating its layout. Call it only for newly presented content,
not recycled history. The helper owns the host's opacity, render transform and transitions;
use a dedicated host when content already animates those properties.

## Mobile composer

`StrataChatComposer` with `Classes="mobile"` supports an optional `IsCompact` presentation.
Set it only for an unfocused, empty draft, then clear it on input focus or when draft content
is present. The layout smoothly unfolds from one row into an editor above the secondary tools;
it does not clip or replace the editor. `LeadingContent` supplies a persistent leading action
such as attachment, alongside the always-available Send/Stop action. Existing `ToolbarContent`
remains the secondary tools area. Selected context and attachments stay visible in either state.
Mobile context chips use one horizontally scrollable row so extra skills do not crowd the editor.
Keep multiline drafts expanded when focus leaves. Native hosts should release actual input
focus on keyboard dismissal, not merely change this presentation property.
`IsCompact` defaults to `false`; desktop layout remains expanded.

When native editor content cannot sit underneath an overlay, set `IsEditorContentVisible`
to `false`. The composer keeps the host instance and shows the bound draft in its read-only
Avalonia editor instead; restore the property when native input can be presented again.

The transcript in `StrataChatShell` respects the standard attached
`ScrollViewer.VerticalScrollBarVisibility` property. It defaults to `Auto`; use `Hidden`
for a touch canvas without rails while keeping scrolling enabled.

`StrataPresence.SplitToIsland(point, followFieldHeight: false)` anchors a companion light
to both coordinates of a floating surface, such as a translucent composer. The existing
one-argument overload keeps desktop side islands level with the main presence field.

## Responsive navigation and sheets

`StrataNavigationDrawer` uses one measured panel for touch tracking, button toggles and interrupted
animations. `IsModal` defaults to `true`; set it to `false` for a docked layout that should not dim
or block the conversation. A docked host can follow `Progress` to move and resize its conversation
alongside the pane, keeping the released area filled during gestures and button animation.
`CanOpenFromAnywhere` enables chat-area
swipes while preserving vertical scrolling, editable text and consumable horizontal scrolling.
Scrim taps dismiss on release so they do not interrupt a closing swipe.

`StrataBottomSheet.SheetMargin` insets only the sheet, leaving the scrim edge-to-edge. Include
the host's safe-area and keyboard insets in that margin instead of padding the entire overlay.
The grip and title share a touch-sized drag surface; release either settles back or continues
the exit from the current finger position. `IsPresented` and the bubbling `PresentationChanged`
event keep native overlays behind the sheet until its exit finishes. A zero margin preserves
the edge-attached layout.

## Theme Variants

### Light / Dark Toggle

Set `RequestedThemeVariant` on the `Application`:

```csharp
Application.Current.RequestedThemeVariant = ThemeVariant.Dark;  // or ThemeVariant.Light
```

### High Contrast

```csharp
Application.Current.RequestedThemeVariant = new ThemeVariant("HighContrast", ThemeVariant.Dark);
```

---

## Density Modes

Strata ships two density presets: **Comfortable** (default) and **Compact**.

### Swap density at runtime

Load the desired density dictionary into `Application.Resources`:

```csharp
var uri = isCompact
    ? new Uri("avares://StrataTheme/Tokens/Density.Compact.axaml")
    : new Uri("avares://StrataTheme/Tokens/Density.Comfortable.axaml");

var dict = (ResourceDictionary)AvaloniaXamlLoader.Load(uri);
Application.Current.Resources.MergedDictionaries.Clear();
Application.Current.Resources.MergedDictionaries.Add(dict);
```

Density affects control heights, padding, spacing, and font sizes.

---

## Design Principles

| Principle | Description |
|---|---|
| **Token-driven** | All values come from semantic tokens — no hard-coded values in templates. |
| **Professional readability** | Optimized for data-heavy UIs with clear typographic hierarchy. |
| **Accessible contrast** | Targets WCAG AA+ contrast ratios across all variants. |
| **Distinctive geometry** | Base radius 6 / Interactive 10 / Overlay 14, with accent indicator bars as the Strata signature. |
| **Minimal shadows** | Uses hairline keylines and subtle surface tone shifts instead of drop shadows. |
| **Density-aware** | Two presets (Comfortable / Compact) controlled via token swap. |

---

## Token Reference

### Surface & Background

| Token | Usage |
|---|---|
| `Color.Background` / `Brush.Background` | App background |
| `Color.Surface0` / `Brush.Surface0` | Primary card / panel surface |
| `Color.Surface1` / `Brush.Surface1` | Slightly elevated surface |
| `Color.Surface2` / `Brush.Surface2` | More elevated / header surface |
| `Color.SurfaceOverlay` / `Brush.SurfaceOverlay` | Popup / dialog surface |

### Text

| Token | Usage |
|---|---|
| `Brush.TextPrimary` | Primary body text |
| `Brush.TextSecondary` | Secondary / label text |
| `Brush.TextTertiary` | Hint / caption text |
| `Brush.TextDisabled` | Disabled text |
| `Brush.TextOnAccent` | Text on accent-colored backgrounds |
| `Brush.TextOnDanger` | Text on danger-colored backgrounds |
| `Brush.TextLink` | Hyperlink text |

### Interactive / Accent

| Token | Usage |
|---|---|
| `Brush.AccentDefault` | Primary action button, selected states |
| `Brush.AccentHover` | Pointer-over accent |
| `Brush.AccentPressed` | Pressed accent |
| `Brush.AccentSubtle` | Selected item background |
| `Brush.AccentSubtleHover` | Selected + hovered background |

### Danger

| Token | Usage |
|---|---|
| `Brush.DangerDefault` | Destructive action button |
| `Brush.DangerHover` / `Brush.DangerPressed` | Danger interaction states |
| `Brush.DangerSubtle` | Error background tint |

### Control

| Token | Usage |
|---|---|
| `Brush.ControlDefault` | Button / input rest state |
| `Brush.ControlHover` / `Brush.ControlPressed` | Interaction states |
| `Brush.ControlDisabled` | Disabled control background |

### Subtle (ghost)

| Token | Usage |
|---|---|
| `Brush.SubtleDefault` | Transparent rest state |
| `Brush.SubtleHover` / `Brush.SubtlePressed` | Ghost button hover/press |

### Border

| Token | Usage |
|---|---|
| `Brush.BorderDefault` | Standard control border |
| `Brush.BorderSubtle` | Separator / divider line |
| `Brush.BorderStrong` | Emphasized / hover border |
| `Brush.BorderFocus` | Focus ring |
| `Brush.BorderError` | Validation error border |

### Selection

| Token | Usage |
|---|---|
| `Brush.SelectionBackground` | Text selection background |
| `Brush.SelectionText` | Text selection foreground |

### Typography

| Token | Value (Comfortable) |
|---|---|
| `Font.Family` | Inter / Segoe UI / system sans |
| `Font.FamilyMono` | Cascadia Code / Consolas |
| `Font.SizeCaption` | 11 |
| `Font.SizeBody` | 14 |
| `Font.SizeSubtitle` | 16 |
| `Font.SizeTitle` | 20 |
| `Font.SizeHeadline` | 26 |
| `Font.SizeDisplay` | 34 |

### Geometry

| Token | Value |
|---|---|
| `Radius.Base` | 6 |
| `Radius.Interactive` | 10 |
| `Radius.Overlay` | 14 |
| `Radius.Full` | 9999 (pill) |
| `Stroke.Thin` | 1 |
| `Stroke.Focus` | 2 |

### Spacing & Sizing

| Token | Comfortable | Compact |
|---|---|---|
| `Size.ControlHeightS` | 28 | 24 |
| `Size.ControlHeightM` | 36 | 30 |
| `Size.ControlHeightL` | 44 | 36 |
| `Padding.Control` | 12,6 | 8,4 |
| `Space.S` | 8 | 6 |
| `Space.M` | 12 | 8 |
| `Space.L` | 16 | 12 |

---

## Styled Controls

- **Window** — background, foreground, base font
- **TextBlock / Label** — type-scale classes: `.caption`, `.body`, `.body-strong`, `.subtitle`, `.title`, `.headline`, `.display`, `.secondary`, `.tertiary`, `.mono`
- **Button** — variants: default, `.accent`, `.subtle`, `.danger`
- **TextBox** — with watermark, focus ring, error state
- **CheckBox** — with checkmark, indeterminate dash
- **RadioButton** — with dot indicator
- **ToggleSwitch** — property-styled
- **ComboBox** — with styled dropdown popup and items
- **ListBox / ListBoxItem** — with accent selection indicator bar
- **TabControl / TabItem** — with bottom accent strip (Strata signature)
- **Slider** — custom thumb
- **ProgressBar** — rounded indicator
- **ScrollBar** — minimal thin track
- **Menu / ContextMenu / MenuItem** — rounded popup with hover states
- **ToolTip** — compact, bordered
- **DataGrid** — styled headers, rows, selection, gridlines
- **Expander** — with rotating chevron
- **Dialog overlay** — use `Border` class `.strata-dialog`

### CSS-like classes for Buttons

```xml
<Button Content="Save" Classes="accent" />
<Button Content="Delete" Classes="danger" />
<Button Content="More" Classes="subtle" />
```

### Dialog-style overlay

```xml
<Border Classes="strata-dialog">
  <StackPanel Spacing="12">
    <TextBlock Classes="subtitle" Text="Confirm" />
    <TextBlock Text="Are you sure?" />
    <Button Classes="accent" Content="OK" />
  </StackPanel>
</Border>
```

---

## Project Structure

```
src/StrataTheme/
├── StrataTheme.axaml          ← Main entry (single include)
├── StrataTheme.csproj
├── Tokens/
│   ├── Colors.Light.axaml
│   ├── Colors.Dark.axaml
│   ├── Colors.HighContrast.axaml
│   ├── Typography.axaml
│   ├── Geometry.axaml
│   ├── Density.Comfortable.axaml
│   └── Density.Compact.axaml
└── Controls/
    ├── Button.axaml
    ├── CheckBox.axaml
    ├── ComboBox.axaml
    ├── DataGrid.axaml
    ├── Expander.axaml
    ├── ListBox.axaml
    ├── Menu.axaml
    ├── ProgressBar.axaml
    ├── RadioButton.axaml
    ├── ScrollBar.axaml
    ├── Separator.axaml
    ├── Slider.axaml
    ├── TabControl.axaml
    ├── TextBlock.axaml
    ├── TextBox.axaml
    ├── ToggleSwitch.axaml
    ├── ToolTip.axaml
    └── Window.axaml

demo/StrataDemo/
├── App.axaml / App.axaml.cs
├── MainWindow.axaml / MainWindow.axaml.cs
├── MainViewModel.cs
└── Program.cs
```

---

## Running the Demo

```bash
cd demo/StrataDemo
dotnet run
```

The demo shows all styled controls on one page with left navigation, theme toggle (Light/Dark), and density toggle (Comfortable/Compact).

---

## License

MIT
