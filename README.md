# Regions Assignation

Unity Editor tool that automatically organizes C# class members into `#region` blocks based on configurable priority rules.

[![Unity](https://img.shields.io/badge/Unity-2020.3+-black.svg)](https://unity3d.com/get-unity/download/archive)
[![MIT License](https://img.shields.io/badge/License-MIT-green.svg)](https://choosealicense.com/licenses/mit/)

## Features

- **Rule-based classification** — Define rules with priority, member kind filters, name patterns (StartsWith, Contains, Regex), and attribute matching
- **7 member kinds** — Field, Property, Method, Constructor, Event, NestedType, Unknown (flags support for combining)
- **Dual control system** — Priority controls which rule wins when multiple match; list order (↑/↓ arrows) controls region placement in the file
- **Real-time preview** — Analyze scripts and preview the generated output before applying changes
- **Batch processing** — Scan entire folders with optional subfolder recursion
- **Clean existing regions** — Strip `#region`/`#endregion` before re-processing scripts that already have regions
- **Unity lifecycle detection** — Special matching for `Awake`, `Start`, `Update`, `OnEnable`, `OnDisable`, etc.
- **Override method detection** — Match methods with the `override` modifier
- **Persistent rules** — Rules saved via `EditorPrefs` and survive window close/reopen
- **Collapsible UI** — All panels (Configuration, Rules, Assignment, Content) are collapsible

## Installation

### Git URL (Package Manager)

1. Open Unity Package Manager (`Window > Package Manager`)
2. Click `+` → `Add package from git URL...`
3. Enter:
```
https://github.com/xavierarpa/RegionsAssignation.git
```

### Manual

Clone or download the repository into your project's `Assets/Plugins/` folder:
```
Assets/Plugins/xavierarpa/RegionsAssignation/
```

## Usage

1. Open the tool via **Tools > Regions Assignation**
2. **Configure** — Set target folder, enable/disable subfolder scanning, toggle "Clean Existing Regions"
3. **Define rules** — Each rule has:
   - **Region Name** — The `#region` name to generate
   - **Priority** — Higher priority rules win when multiple rules match the same member
   - **Member Kinds** — Filter by Field, Property, Method, Constructor, Event, NestedType
   - **Name filters** — StartsWith, Contains, Regex
   - **Attribute filter** — Match by attribute name
   - **Unity Lifecycle / Override** — Special toggles for lifecycle methods and overrides
4. **Reorder** — Use ↑/↓ arrows to control the visual order of regions in the output file
5. **Analyze + Preview** — Review the proposed changes before applying
6. **Apply** — Write changes to selected files

## How Rules Work

Each member is evaluated against all enabled rules **sorted by priority** (highest first). The **first matching rule** wins — each member is assigned to exactly one region.

The **position of the rule in the list** (controlled by ↑/↓ arrows) determines where that region appears in the generated file, from top to bottom.

### Example

| Rule | Priority | Kind | Filter | Position |
|------|----------|------|--------|----------|
| Nested Types | 1000 | NestedType | — | 1st |
| Fields | 900 | Field, Event | — | 2nd |
| Properties | 800 | Property | — | 3rd |
| Main | 700 | Constructor | — | 4th |
| Main | 600 | Method | Unity Lifecycle + Override | 5th |
| Methods | 400 | Method | — | 6th |
| Events | 500 | Method | NameStartsWith: "On" | 7th |

In this setup, `OnSomething()` methods match "Events" (P:500) before "Methods" (P:400) because 500 > 400. But "Events" appears **below** "Methods" in the file because it's position 7th in the list.

## Default Rules

The tool ships with a sensible default preset:

| Region | Priority | Kinds | Filters |
|--------|----------|-------|---------|
| Main Events | 100 | Method | Unity Lifecycle + Override |
| Main Events | 90 | Constructor | — |
| Events | 20 | Method | NameStartsWith: "On" |
| Properties | 0 | Property | — |
| Events Fields | -5 | Event | — |
| Fields | -10 | Field | — |
| Methods | -20 | Method | — |
| Nested Types | -30 | NestedType | — |

Use **Reset Defaults** to restore this preset at any time.

## Samples

The package includes 3 playground scripts for testing:

- **Simple** (~80 lines) — Basic MonoBehaviour with fields, properties, methods, and events
- **Medium** (~286 lines) — More complex script with generics, queues, dictionaries, and lifecycle methods
- **UltraComplex** (~1450 lines) — Stress test with 90+ compute methods, 30+ event handlers, nested types, and context menu methods

## License

[MIT](LICENSE)
