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
<PackageReference Include="StrataUI.Theme" Version="0.3.69" />
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

## Popup motion

Strata popups share one render-thread scale/fade lifecycle: 220 ms to open by default and
150 ms to close, without overshoot or entrance replays after layout/style changes.
Combo boxes expand from their placement target's center and height; menus, flyouts,
submenus and tooltips use the nearest placement anchor (the pointer for context menus).
Origins are resolved after layout, including inherited transforms and display scaling.
An entrance stays pending until asynchronously populated content has a non-zero layout.
Closing immediately disables popup input, finishes the exit before releasing the host,
and a quick reopen continues from the current animated state instead of snapping.
If the placement target is hidden or detached, the host is released immediately instead
of continuing its exit animation against an unavailable anchor. Tooltips retain
their entrance animation but dismiss immediately: Avalonia clears their placement bindings
and theme context on close, so retaining the host for an exit would make the tooltip jump
and recolor before disappearing.

`PopupAnimationHelper.EnableOpenAnimation` opts into expansion and takes precedence
over `EnableOverlayAnimation`, which the theme enables for other popups. Existing
`OverlayAnimationHelper.AnimateOpen` presenter styles use the same lifecycle, not a
second animation. To opt out, set both popup properties and the presenter's
`AnimateOpen` property to `False`.

`PopupAnimationHelper.OpenAnimationDuration` optionally changes one popup's
opening time, without changing its 150 ms exit or other popups. The model picker
uses a 120 ms near-full-size scale/fade instead of expanding from its button's height.

## Chat text selection and copying

Chat shells share one selection across their transcript, including paragraphs, lists,
tables, code and adjacent messages. Messages and standalone markdown also provide a
selection scope on their own. Set `StrataTextSelection.IsSelectionScope="True"` on a
custom transcript host to join its text blocks. Mouse dragging, edge scrolling,
Shift-click extension and Ctrl+C / Cmd+C work across the scope. Ctrl+A / Cmd+A selects
the focused message. Right-click and menu focus preserve the highlight; Escape clears it.
Selection respects clipped inset content, and edge scrolling uses its own scroll area
when selecting inside a table or reasoning panel. Edge scrolling starts when the pointer
moves to drag a selection, not on a stationary click or Shift-click.
Editable fields, buttons and touch scrolling keep their own interactions.

Markdown's renderer-only helper styles are scoped to its content tree, including derived
Markdown controls, avoiding inactive style subscriptions on unrelated application controls.
Full-screen image-viewer helpers remain available in their separate overlay; colors, themes
and text rendering are unchanged.

Full-screen images and Mermaid diagrams support pinch-to-zoom and dragging on touch screens.
Images offer double-tap zoom/fit and a zoom toolbar; diagrams retain double-tap-to-fit.
Viewers respect system-bar safe areas and close on Android Back as well as Escape, before
the application's underlying navigation. Hosts with native input overlays can consult
`StrataFullScreenOverlay.IsPresented` to keep those editors behind a viewer.

Selection controllers are created on first use and shared by the outermost scope;
nested messages and markdown do not allocate duplicate controllers. Keyboard selection
does not create a scroll timer, and the transcript skips per-turn selection checks when
no selection is active.

Code blocks keep their complete text layout and scroll extent, but record only nearby lines for
unselected text. A viewport of prepared lines on either side avoids rebuilding drawing commands
on every small scroll. Selection and detached image/export rendering retain the complete-text path;
no bitmap caches or extra GPU textures are used.

Fenced Text and Markdown blocks wrap long lines to the available width, including long unbroken
values. Text aliases (`text`, `txt`, `plain`, `plaintext`, `plain-text`) and Markdown aliases
(`markdown`, `md`) are case-insensitive; unlabelled fences are treated as plain text.
Other languages preserve source layout with horizontal scrolling. There is no wrap button.
Wrapping is visual only: selection and Copy preserve the original source, including during streaming.
Standalone `StrataCodeBlock.TextWrapping` defaults to `NoWrap` for existing scroll hosts.

Scrollbar thumbs keep their visual inset inside the template, not on the thumb's layout bounds.
This preserves Avalonia's drag-position correction so small mouse movements continue scrolling
reliably in both directions, including ordinary and nested scroll viewers.

The message's copy button copies the whole message. Its context menu has one `Copy as`
group for Plain text, Markdown and Rich text, each copying the selection when there is
one or the message otherwise. There are no separate selection-copy or whole-turn-copy
entries. The footer button and Ctrl+C / Cmd+C supply HTML and readable text so the paste target chooses.
Hosts localize `CopyAsLabel`, `CopyPlainTextLabel` and `SelectAllLabel` alongside the
existing labels.
A `CopyRequested` handler can set `ClipboardWriteTask`; Strata awaits it instead of
writing to the clipboard again, and shows copied feedback only after success.

Formatted copying converts cards and comparisons to headings and content, charts
to data tables, confidence to a percentage and explanation, sources to links, and
interactions to ordinary forms, lists and tables with their current values.
Unanswered quizzes do not reveal their answer key. Markdown retains standard Mermaid
fences; rich copying includes a rendered diagram image when its visual is available,
otherwise readable Mermaid source. Invalid definitions are retained as code and logged.

## Tool progress

Set `StrataAiToolCall.Progress` to the latest complete Markdown update from a running tool.
Its expanded details render headings, lists, links and code, and refresh when `Progress` changes.
The progress is hidden when the call completes, fails or stops. `StrataToolDetails.Progress`
provides the same Markdown surface for custom hosts; parameters and plain-text results/errors
keep their existing presentation.

## Model picker

Rich model items implementing `IStrataModelOption` may supply `ProviderName` to
group rows by the host's provider or account. `ModelId` stays the selection value,
and pinned items still appear first. Leaving `ProviderName` unset preserves the
existing model-id grouping. Use `ModelItemTemplate` for clean model names and
badges without encoding provider information in the model identifier.

The popup focuses search when opened. Search matches every query word against
the model ID, optional `IStrataModelOption.DisplayName`, account and model provider;
case, spaces and punctuation do not need to match the ID exactly. The pin toggle
beside search narrows the results to pinned models without changing the selected
model. One highlight follows both the pointer and Up/Down (skipping group
headings) and is scrolled into view with its heading; Enter chooses the
highlighted model, Ctrl+P (⌘P on macOS) pins or unpins it when `ModelPinCommand`
is set, and Escape closes the popup and restores focus to its button. A focused
button (the pin filter or a segment) keeps its own keys. The pin buttons' tooltips
and accelerator keys show the shortcut. Choosing a model leaves the
reasoning/context controls available. Reopening starts with the full catalog
again, scrolled to and checking the current model.

The layout follows a command-palette shape: a borderless search header, then
single-line rows grouped under small uppercase headings, then a settings footer.
A check marks the current model; group headings, model names and the search text
share one leading edge. A row's pin button appears with the pointer, and pinned
rows use a filled accent pin. Hosts can align right-hand metadata with the
`model-meta` (and `model-meta emphasis`) text classes and show a short status beside
the name with `model-status` / `model-status warning`. Row and footer styles are
scoped to the list and footer, and each row is kept to a minimal control tree,
because a virtualized list restyles every control of a row when it recycles it.
The footer appears only when the current model offers reasoning or context
choices. It has no text labels or icons, so it never sets the popup's width: each
choice is a segmented control spanning the same width as the search field's
Strata line. Segments share spare width evenly and shrink proportionally
(trimming labels) only when space runs out. `ReasoningLabel` and `ContextLabel`
are each control's tooltip and every segment's accessible help text, and the
control's accessible name reads the label with its current value (for example
"Reasoning: High").

Hosts can show a short note about the current model along the bottom of the open
picker with `SelectedModelNote` (for example, the plan it uses), and a link beside
it with `SelectedModelNoteActionLabel` and `SelectedModelNoteCommand`. The note row
(`PART_ModelPickerNote`) is hidden while the note is empty, and its link while the
command is null. `StrataChatComposer` forwards them as `ModelNote`,
`ModelNoteActionLabel` and `ModelNoteCommand`.

The search field has no outline; while it has focus, the composer's Strata line
grows along the header divider. The popup stays in the window's overlay layer:
`StrataBackdropBlur` samples only the pixels already drawn beneath the picker,
with a quiet theme-colored glass tint above it. It does not repaint the whole
window through a `VisualBrush`, and does not retain a window-content reference.
Model rows, search and reasoning/context controls stay sharp. The frost follows
popup placement and size; on non-Skia renderers the tint remains the fallback.
No native popup window is needed.

Rows are virtualized and recycled, unchanged catalogs retain their item source,
and batched catalog/pin updates produce one refresh rather than rebuilding every
row. A newly realized row builds its content once, including with non-recycling
host templates. The host's catalog-refresh command runs after the opening click
handler, once search has focus; a closed or superseded entrance skips that callback.
Reasoning/context segments are retained when their choices have not changed.
Localize `SearchWatermark`, `NoResultsText`, `ModelsLabel` (the list's accessible
name), `PinnedLabel`, `ReasoningLabel` and `ContextLabel` in the host. The
`PART_ModelPickerList` template part is now a `ListBox` with a finite viewport,
not a `StackPanel` inside an outer `ScrollViewer`.

## Bottom sheets

`StrataBottomSheet` keeps its content mounted through a 220 ms entrance and 160 ms exit.
Use `Description` for supporting header text and `ShowCloseButton` for an explicit close
action. Localize `CloseLabel` in the host. The close button, scrim, grip and Escape respect
`IsDismissable`; the close button does not capture a drag. Hosts still own safe-area/keyboard
spacing through `SheetMargin` and can style the `PART_Grip`, `PART_HeaderContent`,
`PART_Description` and `PART_CloseButton` (class `sheet-close`) template parts.

Glass sheets are opt-in, like the glass composer. `PART_Backdrop` is a hidden
`StrataBackdropBlur` beneath the sheet surface; it shares the surface's corner radius and moves
with the slide and drag. Styling it visible frosts only the page beneath the sheet—the page
around it stays sharp behind the scrim. Give the sheet surface a translucent background, as
the tint and rim are painted above the frost. On renderers without Skia the tint is the fallback.

Picker rows use `Button.sheet-item`, with the class `selected` and a trailing
`PathIcon.sheet-check` marking the current choice.

```xml
<sc:StrataBottomSheet Classes="glass"
                     Title="Projects"
                     ShowCloseButton="True"
                     SheetMargin="12"
                     IsOpen="{Binding IsProjectPickerOpen}">
  <!-- Touch-friendly choices -->
</sc:StrataBottomSheet>
```

```xml
<Style Selector="sc|StrataBottomSheet.glass /template/ sc|StrataBackdropBlur#PART_Backdrop">
  <Setter Property="IsVisible" Value="True" />
  <Setter Property="BlurRadius" Value="24" />
</Style>
<Style Selector="sc|StrataBottomSheet.glass /template/ Border#PART_Sheet">
  <Setter Property="Background" Value="#C71B1E29" />
</Style>
```

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

The floating Reply pill over selected text fades out in place when the selection is dismissed.
Its placement anchor is released only after the popup host closes; selecting another excerpt
during the exit keeps the new selection's position and quote.

Set `TimestampToolTip` to the full date/time while keeping `Timestamp` short. Hosts can
localize `CopyLabel`, `CopyAsLabel`, `CopyPlainTextLabel`, `CopyMarkdownLabel`, `CopyRichTextLabel`,
`SelectAllLabel`, `CopiedLabel`, and `MoreActionsLabel`, alongside the existing reply/fork labels.
Disable Edit and Retry when the host does not implement those actions.

Set `IsProgressUpdate="True"` on an assistant message that is narration written while the
assistant works (`:progress-update`). An update never carries the answer's footer. Folded into a
`StrataTurnSummary` with the work it narrates, it reads quieter than the answer, in secondary text;
outside one, such as the update beside the step in progress, it keeps the answer's voice. The summary
marks what it folds with the inherited `StrataTurnSummary.IsWithinSummary`. Only the colour changes, so a
message that turns out to be an update when it finishes does not reflow. Folded, its content takes the
quieter colour by inheritance, `StrataMarkdown` included: the bubble sets the inherited
`StrataChatMessage.IsWithinQuietUpdate`, which content that sets its own text colour can read too. When a
reader opens a summary, its content is revealed from the top while it fades in, and closing folds it away
the same way. The content keeps its natural layout throughout, so a scrolling area inside never shows a
scroll bar mid-motion, and the motion starts once the content has been laid out, so content that takes a
moment to build does not use it up. A reader's own toggle also stops an ancestor `StrataChatShell` from
following its tail, so the row stays under the pointer. Changes made in code, such as opening the row to
show a search hit, apply at once so positions inside are final, as does every change when the control has
the `motion-disabled` class. Hosts that want a bounded area for long content bound their own content.

## Markdown images

Loaded local and online markdown images have a keyboard-accessible expand button
in their top-right corner. It opens the same full-screen overlay as Mermaid diagrams,
fits the image without cropping, and loads a screen-sized decode on demand while
keeping inline previews limited to 640 by 480 pixels (240 by 180 within text).
Escape or the close button restores the previous window state and keyboard focus.
Removing the image closes its viewer and releases its larger bitmap. If the larger
image cannot be loaded, the viewer keeps the inline preview and displays an error.

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

## Transcript glass

The transcript controls share the glass of the chat header's pills and follow-up chips, with line
icons and colour reserved for state. The tokens are `Brush.GlassFill`/`GlassFillHover`,
`Brush.GlassRim`/`GlassRimHover`, `Brush.GlassDivider`, `Brush.GlassRowHover`, `Brush.GlassWell`
(recessed details and console output), `Brush.GlassAccentTint` (a chosen answer), and
`Shadow.GlassChip`/`Shadow.GlassCard`; high contrast uses solid fills without shadows. Their fill
and rim transitions use `PremultipliedBrushTransition`: a plain `BrushTransition` from
`Transparent` (white at zero alpha) flashes light grey on the way in and out.

- `StrataThink` is a chip (`Icon` from `StrataIcons`, `Label`, `Meta`) that grows into a card when
  opened or while it previews live work, with the Strata line as its progress. A collapsed chip at
  work carries a feathered Strata line beneath it. `HasError` turns the icon the danger colour once
  the work has settled.
- `StrataAiToolCall` and `StrataTerminalPreview` are rows inside a card: icon, name, detail (a
  command in the console typeface) and a short outcome, all 31px tall. Opening a row shows its
  details or output in a well. `StrataToolDetails` lays out a step's details: parameters written as
  `**Key:** value` lines become an aligned list of fields (anything else stays markdown), and the
  result follows below a divider, as the error when the step failed. It builds its content when first
  measured, so details that stay folded away cost nothing.
- `StrataElapsedText` counts up from `Since` ("8s", "1m 04s") once a second while attached; live
  rows use it for how long each step has been running.
- `StrataTurnSummary` is a quiet fold line whose content hangs from a thin rail; it shows an alert
  icon when something it folds failed.
- `StrataQuestionCard` is a glass card with full-width option rows and an optional free-text field;
  once answered it folds to one line with the answer. `TouchMode` makes every option, the reply
  field and its buttons 48px targets at the body size (`:touch`).
- `StrataAgentRun.inline` and `StrataTypingIndicator` wear the chip glass. An inline run is one
  31px row (avatar, task, agent) that states its outcome as quiet text with a line icon, with
  `HeaderAction` after it.

## Composer focus and entrance motion

`StrataChatComposer` keeps the thin Stratum gradient under its input, without a gradient border
around the composer. On focus, the line draws from left to right over 400 ms; a contrasting,
feathered highlight then travels for two seconds and stops, leaving the static line until focus
leaves. Refocusing replays the sequence; typing and resizing do not extend it. Motion does not
change layout. The sweep and its pending timers stop on blur, hiding, or detachment. Apply
`Classes="motion-disabled"` to show the static focus line immediately without either animation.
The `glass` composer draws a finer line (1.5 px) feathered at both ends, and no ring: focus keeps the
glass as clear while its rim brightens, its shadow deepens and its top edge catches the light. At rest the
whole composer sits back to 98% of its size, about its bottom centre; while the user writes (`:engaged`:
focus in the composer, or in one of its own menus and lists, which open in the overlay layer) it comes
forward to full size, glass, text and tools together, in 450 ms on a curve that starts gently and lands
softly, and settles back in 350 ms. The scale runs on the composer's composition visual on the render thread, so it never changes
layout, hit testing or popup placement, and the user always writes at full size, where text is crisp and
lists line up with the controls that opened them. A change of mind turns the motion round from wherever it
is. `motion-disabled` and `mobile` keep the composer at full size. Desktop composers give the
text room, 18 px around it (12 px under chips) with 10 px around the toolbar; `Classes="mobile"` keeps
its own touch metrics.

`StrataTheme.Animation.SlideFadeEntrance.Play(host)` gives an attached, visible entrance host
a short rise and fade without animating its layout. Call it only for newly presented content,
not recycled history. The helper owns the host's opacity, render transform and transitions;
use a dedicated host when content already animates those properties.

For content that can contend with loading or streaming work, use
`SlideFadeEntrance.PlayComposited(host)`. Its fade and translation run on the render thread
without changing the logical effective viewport or scroll anchors. The queued start joins
the next composition commit after binding/layout. `StopComposited(host)` cancels it;
hiding, detaching and completion also release the animation. Existing `Play` behavior is unchanged.

## Composer toolbar: +, skills and connectors

The toolbar reads left to right as what a message brings in, then the model and the one action:

- **+** (`PART_AddButton`) opens a glass menu of everything a message can bring in: files
  (`AttachCommand`), the three most recent `AvailableChats` not already chosen, **All chats**, then
  Skills, Agents, Connectors and Project, each shown only when the host offers it. **All chats** and
  **Agents** open their own searchable lists at the + button (`PART_ChatPicker`, `PART_AgentPicker`,
  whose pills are hidden). The agent list offers `AvailableAgents`, leads with the agent answering
  now, and applies a choice like `@` (`AgentName`/`AgentValue`/`AgentGlyph`); choosing the current
  agent again removes it through `AgentRemovedCommand`, like its chip's remove button. Rows that
  have a typing shortcut show its key (`#`, `/`, `@`, `$`); ↑/↓/Home/End walk the rows and Esc closes.
  Every label is a property (`AddFilesLabel`, `AddConnectorsCountFormat`, `AddMenuHint`, …) so hosts
  can localize them. The + button hides when there is nothing to add and on `Classes="mobile"`.
- **Skills** and **Connectors** keep the two most used lists one click away. Both are quiet text
  capsules with line icons; `StrataConnectorPicker.Label` names the connector capsule beside its
  neutral count, and below 600 px wide both shrink to their icons.
- The model picker takes the `quiet` class in the composer: plain text and a line chevron beside
  the send button.
- The send button sends, steers and stops as before. With `CanVoice` and nothing to send it
  dictates (`VoiceRequested`/`VoiceCommand`), and while `IsRecording` it stops dictation;
  `DictateLabel` and `StopDictatingLabel` name it. Touch composers (`Classes="mobile"`) don't dictate
  from it; their hosts offer dictation of their own. While Lumi works a gradient ring breathes around
  the stop button.

Toolbar glyphs are `StrataLineIcon`s: one round-capped stroke on a 24-unit grid in the inherited
foreground, drawn from `StrataIcons`. Each access to a `StrataIcons` geometry parses a new one, so
none is shared across renderers.

## Composer context: skills and chats

`StrataComposerPicker` is a toolbar pill that opens a searchable glass list for choosing context.
The pill shows its `Label` and how many `SelectedItems` are chosen; the list leads with the chosen
items (in the order fixed when it opens), ranks matches as you type, and takes ↑/↓, Enter and Esc.
`ManageLabel`/`ManageCommand` add a footer link, and `CloseOnSelect` suits a pick-one list. The
picker never edits `SelectedItems` itself: it raises `ItemToggled` and its host decides.

`StrataChatComposer` has two. **Skills** offers `AvailableSkills` and fills `SkillItems` (removals
go through `SkillRemovedCommand`; `SkillsManageCommand` adds the footer link). **Chats** offers
`AvailableChats` and edits `ChatItems` directly, reporting removals with `ChatRemoved`;
`ChatsRequestedCommand` runs just before chats are shown so the host can refresh them on demand.
The Chats list has no pill of its own: the + menu opens it, anchored to the + button.
Typing `#` lists matching chats before files. A picker hides while its host offers no list
(`AvailableSkills` or `AvailableChats` is null) and on `Classes="mobile"`; an empty list opens on an
empty state instead. Below 600 px wide the Skills pill shrinks to its icon.

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

Set `ExtendsTranscriptBehindChrome` to run the transcript edge to edge underneath the header
and composer, which then float above it. The scroll content keeps measured margins so it rests
clear of both; a growing header preserves a reader's position, and a growing composer re-pins a
following tail. The scroll-to-bottom button floats above the composer, and both hosts take taps
on their empty space instead of passing them to the transcript beneath. Style the chrome with
`:content-under-header` and `:content-under-composer`, which are set only while scrolled content
is actually behind each host. The default (`false`) keeps the docked desktop layout unchanged.
`ExtendsTranscriptBehindHeader` does the same for the header alone (`:header-overlay`): the
transcript runs under a floating header while the composer stays docked in its own row, for hosts
whose composer keeps its own edge. `ExtendsTranscriptBehindChrome` takes precedence. In either
mode, content brought into view and keyboard pages land clear of the floating chrome, the vertical
scrollbar keeps clear of it too, and hosts that scroll to content by offset can read
`TopChromeInset`.

`StrataPresence.SplitToIsland(point, followFieldHeight: false)` anchors a companion light
to both coordinates of a floating surface, such as a translucent composer. The existing
one-argument overload keeps desktop side islands level with the main presence field.
Changing `Intensity` also eases an already-anchored companion light to the new brightness
without replaying its split or moving its anchor.

`LifecycleOpacityPulse.IsPaused` is an inherited visual-subtree pause for application
backgrounding. Activity pulses, progress sweeps, typing dots, streaming rails, and opt-in
working presence stop while paused and resume from their existing activity state. Hiding or
detaching an activity indicator also stops its loop; hosts do not need to clear its busy state.

## Backdrop blur

`StrataBackdropBlur` frosts whatever was drawn beneath its bounds earlier in the frame: a blurred,
optionally saturated copy clipped to `CornerRadius`. It draws no tint or content, so place it
directly under a translucent surface that supplies both. `Fade` (`Top`/`Bottom`) with `FadeLength`
dissolves the frost toward one edge for scroll-edge bands. `SamplesBeyondEdges` also takes in what
lies just around the glass (three blur radii on every side), so content nearing the rim blurs into
it as real glass does instead of the clamped edge stretching it; it suits small floating glass such
as a navigation pill, whose host must not clip it. The glass samples only pixels redrawn
in the current frame and keeps its unblurred backdrop, so a partial redraw (a caret, button
feedback, an animation beneath) patches just that region and never samples the glass's own
foreground. Once partial redraws settle, all glass is repainted together, which updates the frost
just outside a changed region without letting overlapping glasses keep each other rendering.
It requires the Skia renderer; elsewhere it draws nothing and the
translucent surface remains the fallback. Its GPU textures are released only while their context is
current, by the next frost to render: Avalonia disposes a replaced draw operation before making the
context current, and a texture freed then would never be deleted by the driver.

Floating controls can be glass of their own. `StrataGlassButton` is an opt-in button theme
(`Theme="{DynamicResource StrataGlassButton}"`) whose frost, `PART_Backdrop`, is painted before
the surface's tint, rim and shadow, with matching bounds, corner radius and press transform.
The filter never resamples the foreground chrome. Give the button a translucent `Background`.
`StrataChatComposer` exposes a hidden `PART_Backdrop` behind its
surface, the chat shell's scroll-to-bottom button a hidden `PART_ScrollToBottomBackdrop`, and
`StrataTypingIndicator` a hidden `PART_Backdrop` behind its pill (`PART_Surface`), for hosts that
float it over moving content; hosts enable any of them by style. `Classes="glass"` turns the composer's on (14 px, saturation 1.3), under a
translucent surface: the theme's overlay surface at two thirds with a hairline rim, so it stays a shade
above whatever it floats on. The composer does not clip to its bounds, so the card's shadow falls softly past its
rounded corners. Hosts can keep the frost off until content actually runs beneath the composer, for
example by following `StrataChatShell.IsContentUnderComposer`. Plain buttons and the default
composer draw no frost.

## Responsive navigation and sheets

`StrataNavigationDrawer` uses one measured panel for touch tracking, button toggles and interrupted
animations. `IsModal` defaults to `true`; set it to `false` for a docked layout that should not dim
or block the conversation. A docked host can follow `Progress` to move and resize its conversation
alongside the pane, keeping the released area filled during gestures and button animation.
`CanOpenFromAnywhere` enables chat-area
swipes while preserving vertical scrolling, editable text and consumable horizontal scrolling.
Scrim taps dismiss on release so they do not interrupt a closing swipe. `CompleteMotion()` lands a
settle in progress at its open or closed state at once, for changes the user should not watch slide
(such as switching top-level places); a drag in progress is left to the finger.

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
