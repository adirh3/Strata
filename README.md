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
<PackageReference Include="StrataUI.Theme" Version="0.1.0" />
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
