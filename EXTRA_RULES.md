# Possible Extra Elements for Rules

This document lists potential extra elements that could be added to the rule configuration system in future iterations.

---

## Member Modifiers / Keywords

| Element | Description | Example |
|---------|-------------|---------|
| **Static** | Filter by `static` modifier | Separate static fields/methods into their own region |
| **Abstract** | Filter by `abstract` modifier | Group abstract members together |
| **Virtual** | Filter by `virtual` modifier | Group virtual members that can be overridden |
| **Sealed** | Filter by `sealed` modifier | Identify sealed overrides |
| **Extern** | Filter by `extern` modifier | Group P/Invoke declarations |
| **Async** | Filter by `async` modifier | Group async methods separately |
| **Readonly** | Filter by `readonly` modifier | Distinguish readonly fields from mutable ones |
| **Volatile** | Filter by `volatile` modifier | Group thread-sensitive fields |
| **Const** | Filter by `const` modifier | Separate constants from regular fields |
| **New** | Filter by `new` (hiding) modifier | Identify members that hide base members |
| **Partial** | Filter by `partial` modifier | Group partial method declarations |
| **Unsafe** | Filter by `unsafe` modifier | Group unsafe code separately |

---

## Return Type Filters

| Element | Description | Example |
|---------|-------------|---------|
| **ReturnType** | Filter by return type name or pattern | Group all `void` methods, or all `Task`/`Task<T>` async methods |
| **ReturnTypeContains** | Token-based filter on return type | Match `Task`, `IEnumerable`, `List` etc. |
| **IsGenericReturn** | Whether the return type is generic | Filter `Task<T>`, `List<T>`, etc. |
| **ReturnsVoid** | Boolean for void-returning methods | Separate void methods from value-returning ones |

---

## Parameter Filters

| Element | Description | Example |
|---------|-------------|---------|
| **ParameterCount** | Filter by number of parameters (min/max) | Group parameterless methods separately |
| **ParameterTypeContains** | Filter by parameter type names | Group methods that take a specific type |
| **HasParams** | Whether the method has a `params` parameter | Identify variadic methods |
| **HasOptionalParams** | Whether the method has optional parameters | Group methods with default values |

---

## Attribute Filters (Extended)

| Element | Description | Example |
|---------|-------------|---------|
| **AttributeRegex** | Regex pattern for attribute matching | More flexible attribute filtering |
| **HasAnyAttribute** | Boolean: member has at least one attribute | Separate decorated from undecorated members |
| **AttributeCount** | Min/max attribute count | Group heavily-decorated members |

---

## Inheritance / Interface

| Element | Description | Example |
|---------|-------------|---------|
| **IsExplicitInterfaceImpl** | Whether it's an explicit interface implementation | `IService.Method()` vs `Method()` |
| **InterfaceNameContains** | Filter by interface name in explicit implementations | Group members implementing a specific interface |
| **IsImplicitInterfaceImpl** | Members that implicitly implement interfaces | Requires deeper analysis |

---

## Documentation / Comments

| Element | Description | Example |
|---------|-------------|---------|
| **HasXmlDoc** | Whether the member has XML documentation comments | Group documented vs undocumented members |
| **HasSummary** | Whether the member has a `<summary>` tag | Filter by documentation completeness |

---

## Naming Patterns (Extended)

| Element | Description | Example |
|---------|-------------|---------|
| **NameEndsWith** | Tokens that the member name ends with | Match `Handler`, `Service`, `Callback` suffixes |
| **NameExact** | Exact name match | Target a specific member like `Dispose`, `ToString` |
| **NameLength** | Min/max character length of member name | Separate short utility names from long descriptive ones |
| **NameCasePattern** | Filter by naming convention | `camelCase`, `PascalCase`, `UPPER_CASE`, `_prefixed` |

---

## Body / Complexity

| Element | Description | Example |
|---------|-------------|---------|
| **LineCount** | Min/max line count of the member body | Separate short one-liners from long methods |
| **IsExpressionBodied** | Whether the member uses `=>` syntax | Group expression-bodied members |
| **IsAutoProperty** | Whether the property is auto-implemented | `{ get; set; }` vs full body property |

---

## Ordering / Grouping Enhancements

| Element | Description | Example |
|---------|-------------|---------|
| **SubRegion** | Nested region name within a region | Create hierarchical `#region` blocks |
| **SortMembersBy** | Sort members within a region | Alphabetical, by access modifier, by line count |
| **SortDirection** | Ascending or descending sort | Control member order within regions |
| **GroupByReturnType** | Automatic sub-grouping by return type | Within a Methods region, group by return type |

---

## Special Patterns

| Element | Description | Example |
|---------|-------------|---------|
| **IsDisposable** | Match `Dispose()` or `IDisposable` pattern | Group dispose-related members |
| **IsOperatorOverload** | Match operator overload declarations | `operator +`, `operator ==`, etc. |
| **IsConversionOperator** | Match implicit/explicit conversion operators | `implicit operator int(...)` |
| **IsDestructor** | Match finalizer/destructor `~ClassName()` | Group destructors separately |
| **IsIndexer** | Match indexer `this[...]` declarations | Already partially supported |
| **IsExtensionMethod** | Match extension methods (`this` first param) | Group extension methods |

---

## Platform / Framework Specific

| Element | Description | Example |
|---------|-------------|---------|
| **UnityAttributeType** | Filter by Unity-specific attributes | `[SerializeField]`, `[Header]`, `[Tooltip]` |
| **IsUnityMessage** | Extended Unity lifecycle detection | Include lesser-known callbacks |
| **IsEditorOnly** | Members wrapped in `#if UNITY_EDITOR` | Group editor-only code |
| **IsTestMethod** | Methods with `[Test]`, `[UnityTest]`, etc. | Group test methods |

---

## Notes

- Not all of these elements require the same level of parsing complexity. Some (like **Static**, **Const**, **Async**) are simple keyword checks similar to the existing **Override** toggle.
- Others (like **ReturnType**, **ParameterCount**, **LineCount**) would require deeper parsing of the member signature or body.
- Implementation priority should be based on frequency of use and parsing complexity.
