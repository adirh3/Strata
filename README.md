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
<PackageReference Include="StrataUI.Theme" Version="0.3.40" />
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

## Model picker provider groups

Rich model items implementing `IStrataModelOption` may supply `ProviderName` to
group rows by the host's provider or account. `ModelId` stays the selection value,
and pinned items still appear first. Leaving `ProviderName` unset preserves the
existing model-id grouping. Use `ModelItemTemplate` for clean model names and
badges without encoding provider information in the model identifier.

## Message sharing

Set `StrataChatMessage.CanShare="True"` to offer an action-strip and context-menu
share action. Localize `ShareLabel` in the host and handle the bubbling
`ShareRequested` event to choose what sharing means for your application.
The action is opt-in, keyboard accessible, and unavailable while the message is
streaming or being edited. It never copies, sends or executes content itself.

## Message action strip

A finished assistant `StrataChatMessage` ends with a quiet footer: icon-only actions first, then the
author/model caption and time, wrapping on narrow widths without resizing the answer. Copy, Reply
(`CanReply`), Share (`CanShare`), Edit (`IsEditable`), Retry (`CanRegenerate`), and the overflow menu share
one outline-icon style, with each label provided as a tooltip and accessible name. Their hover and focus chrome
stays inside the message, which clips to its bounds; the footer appears once streaming ends and stays steady
while the shell scrolls.
User-message side actions retain their labelled hover, keyboard-focus, and touch-tap reveal behavior.
The overflow menu reuses the message context menu, including formatted copy and optional
branching (`CanFork`). Copy briefly shows a confirmation checkmark.

Set `TimestampToolTip` to the full date/time while keeping `Timestamp` short. Hosts can
localize `CopyLabel`, `CopySelectionLabel`, `CopyMarkdownLabel`, `CopyRichTextLabel`,
`CopyTurnLabel`, `CopiedLabel`, and `MoreActionsLabel`, alongside the existing reply/fork labels.
Disable Edit and Retry when the host does not implement those actions.

## Native interactive responses

`StrataActionReceipt` presents the user's action as a compact, right-aligned bubble in the user
message color: an action icon (`Action`: use, compare, submit, remaining, assumptions, discuss),
title, and summary. Inputs are a frozen `Detail` string, realized only when the disclosure opens;
"Key: value" paragraphs render as an aligned table and other paragraphs as titled prose. A quiet
meta row beneath the bubble shows lifecycle status (hidden once `Status` is `ReplyReady`) and a
**Show original** link. `SourceRequested` is a navigation event, never an agent/tool invocation.
`TouchMode` provides touch-sized actions.

For a pending input tool, set `StrataInteractiveBlock.InputStatus` to `Waiting`, `Submitted`,
`Canceled`, or `Expired` and acknowledge `Requested` through `Completion`. Waiting forms and
shortlists use the same controls with an accent stratum line and a **Needs your input** pill, but
submit to the existing task; `cancel` ends the request. Submitted input collapses to a one-line
summary with a **Sent** pill and an inputs disclosure; expired input keeps a summary of its draft
without dispatching new work. Ordinary answer widgets leave `InputStatus` null.

`StrataMarkdown` recognizes closed JSON fences named `form`, `checklist`, `results`, `calculator`,
and `quiz`. It creates `StrataInteractiveBlock` controls using validated native
definitions. Partial streaming fences stay inactive; invalid completed definitions report an
error with their original source. Definitions never execute scripts, arbitrary formulas, or tool
commands. Stable `id` values must be unique within an answer.

Hosts bind `InteractionStates` (`IReadOnlyDictionary<string,string>`), `InteractionLanguage`,
`InteractionsHosted`, and `CanSubmitInteractions`. Set `InteractionTouchMode` for 48-DIP controls
and bottom-sheet date/selection pickers; `StrataInteractiveBlock.ClosePicker()` closes an open sheet
for system back navigation. `StrataInteractiveBlock.Requested` bubbles both local state edits
(`Action == null`; typed text and numbers once editing ends, other choices immediately) and
explicit submissions. Persist state with the owning message,
not only a render cache; acknowledge requests with `Completion` (`Task<InteractiveActionResult>`).
Use `InteractiveSubmission.Build` to produce a validated user request for the existing agent flow.
Buttons are not authority to run commands or bypass permission gates.

```xml
<sc:StrataMarkdown Markdown="{Binding Answer}"
                  InteractionStates="{Binding InteractionStates}"
                  InteractionsHosted="True"
                  CanSubmitInteractions="{Binding IsReady}"
                  InteractionLanguage="en"
                  IsInline="True" />
```

Standalone `StrataInteractiveBlock` exposes `Definition`, `StateJson`, `Language`, `TouchMode`,
`IsHosted`, and `CanSubmit`. The AI Controls demo shows every type, the input-request states, and
an action receipt. Hosts with native overlays can observe `PickerPresentationChangedEvent` for the
picker lifetime. `InteractionPickerHost` / `PickerHost` lets a host keep sheets inside its local
device/modal layer instead of using the top-level overlay.

Every block shares one card language built from Strata tokens: an icon tile and title, optional
description, a meta pill (progress ring, question position, or request state), the body, and one
action row with a short status beside it. Forms render short option sets as chips, numbers with
an inline unit or currency symbol, and dates as an input-styled calendar button. Checklists use
round indicators, optional per-item `meta`, and `numbered` step guides that highlight the next
step. Choices are selectable cards. Quizzes use lettered options with correct/incorrect states.

The bounded calculator templates are `bill-split`, `savings`, `servings`, `budget`, and `loan`.
Each leads with a headline metric; budgets and loans add a proportional allocation bar, savings a
growth chart, and servings an ingredient table with kitchen fractions. `InteractiveFormat`
presents money from the definition's currency code without converting it. They update locally
and use the same numeric contract on every platform. Forms, checklists, result selection, quiz
feedback, and calculator edits retain their state during streaming append and history
reattachment. Hosts own persisted state and action errors.

## Question input state

`StrataQuestionCard.HasInput` is a read-only property indicating that the card contains
free text or selected options. Hosts can bind it `OneWayToSource` to protect unsubmitted
answers when navigating or applying an update. It reports input independently of
`IsAnswered`; hosts should ignore confirmed answers when checking for unsent work.

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

## Reasoning cards

`StrataThink` caps its expanded content viewport at 240 device-independent pixels.
Short reasoning stays content-sized; longer or growing reasoning gets an automatic
vertical scrollbar without scrolling the header away. `MaxContentHeight` can adjust
the limit, or be set to `Infinity` for tool/source hosts that need an unbounded layout.
The limit applies both while reasoning is active and after it finishes.

## Running activity previews

`StrataThink.PreviewContent` displays lightweight activity rows below the progress header
while `IsActive` is true and the card is collapsed. Clicking the header or pressing Enter/Space
replaces the preview with the full `Content`. Finishing returns a collapsed card to its compact
pill; reasoning controls without a preview retain their compact pill presentation. Preview and detail content are only
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

For content that can contend with loading or streaming work, use
`SlideFadeEntrance.PlayComposited(host)`. Its fade and translation run on the render thread
without changing the logical effective viewport or scroll anchors. The queued start joins
the next composition commit after binding/layout. `StopComposited(host)` cancels it;
hiding, detaching and completion also release the animation. Existing `Play` behavior is unchanged.

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
