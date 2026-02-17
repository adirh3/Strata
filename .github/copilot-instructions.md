# Copilot Instructions for Strata

These instructions are optimized for first-contact, autonomous coding tasks in this repository.

## 1) Architecture quick map

- `src/StrataTheme`: theme/control library published as NuGet `StrataUI.Theme`.
- `demo/StrataDemo`: showcase app for visual and behavioral verification.

Key files:

- Theme entry: `src/StrataTheme/StrataTheme.axaml`
- Tokens: `src/StrataTheme/Tokens/*.axaml`
- Controls: `src/StrataTheme/Controls/<Control>.cs` + `<Control>.axaml`
- Demo shell: `demo/StrataDemo/MainWindow.axaml`
- Demo logic: `demo/StrataDemo/MainWindow.axaml.cs`, `MainViewModel.cs`
- Localization: `demo/StrataDemo/Localization/Strings.cs`

## 2) Default working behavior

- Make focused, minimal changes for the user’s request only.
- Preserve existing style language and component architecture.
- Do not refactor unrelated code paths.
- Prefer fixing root cause over patching symptoms.

## 3) Coding standards

- Use concise, readable C# with clear names.
- Avoid one-letter variable names.
- Add XML docs for all new public controls, public properties, and public events.
- For control docs, include template parts and pseudo-classes in `<remarks>`.

## 4) Avalonia control conventions

For new controls:

- Derive from `TemplatedControl` (or `ItemsControl` when appropriate).
- Model state with `StyledProperty` / `DirectProperty` / routed events.
- Apply state to visual styles via pseudo-classes (`PseudoClasses.Set(...)`).
- Implement keyboard interactions (`Enter`, `Space`, `Escape`) where relevant.
- Include focus-visible treatment in AXAML.

Important constraint:

- Do not use WPF-only attributes (`TemplatePartAttribute`, `PseudoClassesAttribute`).

## 5) AXAML and styling conventions

- Use existing semantic tokens (`Brush.*`, `Radius.*`, `Stroke.*`, `Font.*`, `Size.*`, `Space.*`).
- Avoid hard-coded colors/sizing when token alternatives exist.
- Keep motion subtle and performant.
- Prefer transitions over heavy keyframes.
- For transform animation, prefer `TransformOperationsTransition`.

## 6) Strata design principles

Strata is not just a set of styles. It is a system with visual and behavioral rules that should remain coherent across all controls and pages.

### A) Token-first, never value-first

- Every visual choice should come from semantic tokens first.
- Use direct literals only when no token exists and the value is intentionally one-off.
- If a value repeats, add/extend a token rather than duplicating literals.

### B) Professional readability over decorative styling

- Prioritize legibility for dense, enterprise-like UIs.
- Maintain clear hierarchy: display/headline/title/subtitle/body/caption.
- Avoid visual noise (extra shadows, unnecessary borders, flashy gradients).

### C) Layered surfaces with restrained contrast

- Differentiate surfaces primarily through tone and keylines, not heavy elevation.
- Preserve the Surface0/1/2 and BorderSubtle/Default rhythm.
- Keep cards/panels visually calm so data and actions remain primary.

### D) Signature geometry and spacing consistency

- Respect existing corner-radius language (base vs interactive vs overlay).
- Keep spacing on the existing scale (`Space.*`, `Padding.*`, `Size.*`).
- Do not invent ad-hoc spacing patterns in isolated controls.

### E) Motion should communicate state, not decorate

- Motion must explain interaction/state changes (expand, select, stream, progress).
- Keep durations short and easing smooth; avoid theatrical movement.
- Prefer transitions and composition-safe animations over complex keyframe choreography.

### F) Accessibility is a baseline, not an enhancement

- Ensure keyboard parity for pointer interactions.
- Provide visible focus states for all interactive controls.
- Preserve contrast quality across Light/Dark/HighContrast variants.
- Do not hide critical status solely in color; include text/state affordances when needed.

### G) Composable controls and stable APIs

- New controls should be composable in host layouts and templates.
- Expose state through dependency properties and routed events.
- Avoid breaking public API shape unless explicitly requested.

### H) Localization and RTL readiness by default

- User-facing demo shell text should be localizable.
- New layout patterns should tolerate both LTR and RTL flow direction.
- Avoid hard assumptions about icon/text order where direction matters.

### I) Demo quality should reflect package quality

- Demo blocks are product examples, not throwaway snippets.
- New controls should be demonstrated in realistic and edge-case states.
- Keep demo content aligned with intended real-world usage.

### J) What to avoid

- Random visual experiments that deviate from Strata language.
- Hard-coded colors when semantic brushes exist.
- Control-specific hacks that bypass tokens/state model.
- Over-animation that competes with content.

## 7) Task playbooks

### A) Add a new custom control

1. Add `src/StrataTheme/Controls/<Control>.cs`
2. Add `src/StrataTheme/Controls/<Control>.axaml`
3. Include style in `src/StrataTheme/StrataTheme.axaml`
4. Add demo usage in `demo/StrataDemo/MainWindow.axaml`
5. Build and run demo

### B) Modify an existing control

1. Update `.cs` and `.axaml` pair consistently
2. Keep public API stable unless explicitly requested
3. Validate visual states (hover/pressed/focus/disabled + custom pseudo-classes)
4. Build + run demo

### C) Demo localization / RTL update

1. Add/adjust keys in `demo/StrataDemo/Localization/Strings.cs`
2. Bind user-facing text in XAML to localized keys
3. Verify RTL layout behavior for affected UI
4. Build + run demo

### D) Package release update

1. Bump version in `src/StrataTheme/StrataTheme.csproj`
2. Pack to `./nupkg`
3. Keep `README.md` package example reasonably aligned

## 8) Validation requirements (must run before finishing)

- Always: `dotnet build`
- If UI/theme/control changed: `dotnet run --project demo/StrataDemo/StrataDemo.csproj`
- If packaging/version changed: `dotnet pack src/StrataTheme/StrataTheme.csproj -c Release -o ./nupkg`

## 9) Frequent pitfalls

- Runtime animation exceptions from unsupported keyframe properties.
- Forgetting to register new control AXAML in `StrataTheme.axaml`.
- Adding user-facing demo text without localization keys.
- Build failure due to locked `StrataDemo.exe` when demo is still running.

## 10) Done criteria

A task is complete only when:

- Requested behavior is implemented.
- Integration steps are complete (theme include + demo usage if applicable).
- Required validation commands pass.
- Scope remains limited to the user request.
