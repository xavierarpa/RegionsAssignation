# Changelog

All notable changes to this package will be documented in this file.

## [1.0.0] - 2026-03-17

### Added
- **EditorWindow** accessible via `Tools > Regions Assignation`
- **Rule-based member classification** — define rules with priority, member kind filters, name patterns, regex, and attribute matching
- **7 member kinds** — Field, Property, Method, Constructor, Event, NestedType, Unknown (with flags support)
- **Priority system** — higher priority rules win when multiple rules match a member; list order controls region placement in the output file
- **Rule reordering** — ↑/↓ arrows to control the visual order of regions in the generated file
- **Real-time preview** — analyze scripts and preview the generated output before applying changes
- **Batch processing** — scan entire folders (with optional subfolder recursion)
- **Clean existing regions** — option to strip `#region`/`#endregion` before re-processing scripts that already have regions
- **Unity lifecycle detection** — special matching for Awake, Start, Update, OnEnable, OnDisable, etc.
- **Override method detection** — match methods with the `override` modifier
- **Unassigned region** — configurable fallback region for members that don't match any rule
- **Persistent rules** — rules saved via EditorPrefs and survive window close/reopen
- **Reset Defaults** — one-click restore to the default rule preset
- **Collapsible panels** — Configuration, Rules, Assignment, and Content sections are all collapsible
- **Per-rule foldout** — each rule can be expanded/collapsed individually with summary line
- **3 playground scripts** — Simple, Medium, and UltraComplex test files included as samples
