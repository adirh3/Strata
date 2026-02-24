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

## 9) UI Testing with Avalonia MCP

Strata has an Avalonia MCP server configured in `.vscode/mcp.json`. This gives you live access to the running demo app — you can see the UI, click buttons, type text, inspect controls, check bindings, and take screenshots. **Use it.**

**Every time you make a UI or control change, you must test it with the MCP tools.** Don't just build and hope it works — run the demo, poke at it, and confirm your changes look and behave correctly.

### Workflow

1. Run `dotnet tool restore` once to ensure the CLI tool is available
2. Start the demo: `dotnet run --project demo/StrataDemo/StrataDemo.csproj`
3. Use the MCP tools to verify your work

### What to test and how

- **Did your control actually render?** Use `find_control` to search by name (`#MyControl`) or type (`Button`). If it's not found, something is wrong.
- **Are properties set correctly?** Use `get_control_properties` to check values, visibility, enabled state, dimensions — anything you set in XAML or code-behind.
- **Do bindings work?** Use `get_data_context` to check ViewModel state, and `get_binding_errors` to catch broken bindings. Binding errors are silent failures — always check.
- **Does interaction work?** Use `click_control` to press buttons, `input_text` to type into text fields, `set_property` to change values at runtime. Verify the app responds correctly.
- **Does it look right?** Use `take_screenshot` to capture the window or a specific control. Check layout, alignment, and visual appearance.
- **Is the tree structure correct?** Use `get_visual_tree` or `get_logical_tree` to verify parent-child relationships and nesting.
- **What's focused?** Use `get_focused_element` to check focus behavior after interactions.
- **Styles applied?** Use `get_applied_styles` to inspect CSS classes, pseudo-classes, and style setters on a control.

### Control identifiers

Many tools take a `controlId` parameter. Three formats work:
- `#Name` — matches by `Name` property (e.g., `#SendButton`)
- `TypeName` — first control of that type (e.g., `TextBox`)
- `TypeName[n]` — nth control of that type, 0-indexed (e.g., `Button[2]`)

### When to use it

- After adding or modifying any XAML or code-behind UI code
- After changing data bindings or ViewModel properties that affect the UI
- After styling changes — verify pseudo-classes and setters apply
- When debugging layout issues — inspect bounds, margins, and visibility
- When a feature "should work" but you're not sure — take a screenshot and see

## 10) Versioning + Packaging

- If change is externally visible (new control/behavior/style API), bump `Version` in `src/StrataTheme/StrataTheme.csproj`.
- Keep `README.md` package example reasonably aligned with current package version.
- Package output goes to `nupkg/` and should remain ignored by git.

## 11) Performance Benchmarking (Demo-only)

Use this when asked to validate chat render/scroll/stream performance.

### Command

- Run benchmark mode (headless-ish automation through the demo app):
	- `dotnet run --project demo/StrataDemo/StrataDemo.csproj -- --chat-perf --chat-perf-report artifacts/chat-perf-report.json`
- Optional target render timer:
	- `dotnet run --project demo/StrataDemo/StrataDemo.csproj -- --chat-perf --chat-perf-target-fps 120 --chat-perf-report artifacts/chat-perf-report.json`

### Output contract

- Writes a JSON report to the path passed in `--chat-perf-report`.
- Prints `CHAT_PERF_*` lines to stdout for quick extraction.
- Includes `renderEvidence` fields to confirm the benchmark actually rendered the Chat Performance page.

### Guardrails (important)

- Benchmarking is **complementary and optional**; it must not affect normal app startup.
- Keep benchmark-only logic under `demo/StrataDemo`.
- Do **not** add benchmark/debug-only public API to `src/StrataTheme` controls.
- Do not commit generated benchmark JSON artifacts unless explicitly requested.

## 12) Commit Hygiene

- Use clear, scoped commit messages tied to the actual change set.
- Group related control + theme + demo updates together.
- Do not include generated artifacts unless explicitly requested.
