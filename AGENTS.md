# AGENTS.md

Repository operating manual for coding agents in Strata.

## 1) Mission

- Maintain a production-grade Avalonia theme library (`StrataTheme`) and a reliable demo app (`StrataDemo`).
- Prefer minimal, surgical changes with strong visual consistency.
- Keep agent output immediately runnable: compile, demo-run when UI changes, package when version changes.

## 2) First-Contact Startup (empty-state workflow)

When starting from zero context, do this in order:

1. Read `README.md` for package usage and token vocabulary.
2. Read `src/StrataTheme/StrataTheme.axaml` to understand style include graph.
3. Inspect related control pair in `src/StrataTheme/Controls/` (`.cs` + `.axaml`).
4. Inspect demo usage in `demo/StrataDemo/MainWindow.axaml`.
5. Build (`dotnet build`) before editing if repo state is unknown.

## 3) Repo Map

- `src/StrataTheme`
	- `Controls/*.cs`: behavior, state, pseudo-classes, routed events.
	- `Controls/*.axaml`: templates, visual states, transitions.
	- `Tokens/*.axaml`: semantic design tokens and theme variants.
	- `StrataTheme.axaml`: central include list (must include new control styles).
	- `StrataTheme.csproj`: package metadata and version.
- `demo/StrataDemo`
	- `MainWindow.axaml`: showcase pages and usage examples.
	- `MainWindow.axaml.cs`: interaction wiring/demo logic.
	- `MainViewModel.cs`: demo state and localization bindings.
	- `Localization/Strings.cs`: user-facing localized strings + RTL flow support.

## 4) Hard Constraints

- Do not use WPF-only attributes such as `TemplatePartAttribute` or `PseudoClassesAttribute`.
- Document template parts and pseudo-classes in XML docs (`<remarks>`), not via unsupported attributes.
- Avoid hard-coded colors/metrics when token exists (`Brush.*`, `Radius.*`, `Stroke.*`, `Font.*`, `Size.*`, `Space.*`).
- Keep controls keyboard-accessible (`Enter`, `Space`, `Escape` as relevant).
- Keep public APIs stable unless user explicitly requests breaking changes.
- Avoid unrelated refactors.

## 5) Control Authoring Playbook

For a new custom control, complete all steps:

1. Add behavior file: `src/StrataTheme/Controls/<ControlName>.cs`.
2. Add template/style file: `src/StrataTheme/Controls/<ControlName>.axaml`.
3. Register style include in `src/StrataTheme/StrataTheme.axaml`.
4. Add demo usage block in `demo/StrataDemo/MainWindow.axaml`.
5. Add XML docs for public class/properties/events (including template parts and pseudo-classes in remarks).
6. Validate with build and demo run.

Expected behavior standards:

- Use `StyledProperty` / `DirectProperty` / routed events for state and actions.
- Set pseudo-classes in code-behind (`PseudoClasses.Set(...)`).
- Add focus-visible treatment in AXAML.
- Use subtle transitions; avoid heavy keyframe usage when transitions suffice.

## 6) Localization + RTL Playbook (demo)

- All user-facing shell/demo text should come from localization source (`demo/StrataDemo/Localization/Strings.cs`).
- Keep bindings compatible with live language switching.
- For layout-sensitive additions, verify RTL behavior (alignment, spacing, icon/text order where applicable).
- If adding new user-facing text, add both EN + HE keys.

## 7) Animation & Avalonia Gotchas

- Prefer `TransformOperationsTransition` for transform animation.
- Do not rely on unsupported keyframe animation targets (e.g., some `RenderTransform` keyframe scenarios can fail at runtime).
- If build fails with locked `StrataDemo.exe`, stop running demo process and rebuild.

## 8) Validation Checklist

Run based on scope:

- Always: `dotnet build`
- Any UI/theme/control change: `dotnet run --project demo/StrataDemo/StrataDemo.csproj`
- Package/version change: `dotnet pack src/StrataTheme/StrataTheme.csproj -c Release -o ./nupkg`

Do not mark complete until required validation passes.

## 9) Versioning + Packaging

- If change is externally visible (new control/behavior/style API), bump `Version` in `src/StrataTheme/StrataTheme.csproj`.
- Keep `README.md` package example reasonably aligned with current package version.
- Package output goes to `nupkg/` and should remain ignored by git.

## 10) Commit Hygiene

- Use clear, scoped commit messages tied to the actual change set.
- Group related control + theme + demo updates together.
- Do not include generated artifacts unless explicitly requested.
