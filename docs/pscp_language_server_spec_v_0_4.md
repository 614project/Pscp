# PSCP Language Server Specification v0.4

## 1. Scope

This document defines version `v0.4` of the Language Server Protocol (LSP) server specification for PSCP.

This document specifies:

1. language server architecture,
2. document and workspace model,
3. integration with the PSCP frontend and semantic model,
4. feature behavior for syntax-aware and semantic-aware tooling,
5. intrinsic-symbol handling,
6. pass-through C#/.NET surface handling,
7. diagnostics policy,
8. implementation priorities for editor tooling.

This document is implementation-oriented and assumes the existence of the frontend and transpiler model described by the PSCP language, API, and transpiler specifications.

This document is normative unless otherwise stated.

---

## 2. Goals

The language server shall provide practical, low-latency tooling for problem solving and competitive programming workflows.

The primary goals are:

1. responsive feedback while typing,
2. correct understanding of PSCP-specific sugar,
3. correct understanding of pass-through C#/.NET surface,
4. stable diagnostics in incomplete code,
5. useful completion for contest-oriented patterns,
6. editor help that reflects compile-time lowering reality where useful.

---

## 3. Non-Goals

The following are outside the required scope of `v0.4` unless explicitly stated otherwise:

1. full build orchestration,
2. online judge integration,
3. debugger integration,
4. guaranteed full semantic understanding of all external assemblies,
5. mandatory formatting support,
6. live generated-C# preview as a required feature,
7. advanced project-scale refactoring beyond reasonable local/document scope.

---

## 4. High-Level Architecture

A conforming language server should be organized into the following subsystems:

1. protocol layer,
2. workspace manager,
3. document store,
4. analysis scheduler,
5. frontend service layer,
6. semantic model cache,
7. feature providers,
8. diagnostics publisher.

### 4.1 Protocol Layer

Responsible for:

- JSON-RPC transport,
- request/notification dispatch,
- capability negotiation,
- cancellation,
- error propagation,
- optional progress reporting.

### 4.2 Workspace Manager

Responsible for:

- workspace folder tracking,
- open document tracking,
- configuration state,
- symbol metadata integration,
- background indexing lifecycle.

### 4.3 Document Store

Responsible for:

- immutable snapshots by URI,
- version tracking,
- incremental text application,
- UTF-16 line/column mapping for LSP,
- line-index caching.

### 4.4 Analysis Scheduler

Responsible for:

- debouncing edits,
- cancelling stale work,
- prioritizing foreground requests,
- scheduling lex/parse/bind/type passes.

### 4.5 Frontend Service Layer

Wraps compiler frontend services and exposes:

- tokens,
- syntax trees,
- AST,
- bound trees,
- type and shape information,
- intrinsic classification,
- lowered semantic facts where useful.

### 4.6 Semantic Model Cache

Responsible for caching:

- syntax trees,
- ASTs,
- diagnostics,
- semantic models,
- symbol tables,
- semantic token data,
- document symbol data,
- completion contexts when profitable.

### 4.7 Feature Providers

Recommended providers:

- diagnostics,
- completion,
- hover,
- go-to-definition,
- references,
- rename,
- document symbols,
- semantic tokens,
- signature help,
- inlay hints,
- code actions,
- optional folding ranges,
- optional formatting.

---

## 5. Protocol Baseline

The language server shall implement Language Server Protocol over JSON-RPC 2.0.

Recommended transport:

- standard input/output.

Recommended language identifier:

- `pscp`

A conforming implementation may use any supported LSP transport provided protocol semantics are preserved.

---

## 6. Document Model

## 6.1 Document Identity

Each open document is identified by:

- URI,
- version,
- immutable text snapshot.

Recommended structure:

```txt
DocumentId
  uri: string

DocumentSnapshot
  id: DocumentId
  version: int
  text: string
  lineIndex: LineIndex
```

## 6.2 Snapshot Immutability

All analysis must operate on immutable document snapshots.

## 6.3 Line Index

A line index shall be maintained for each snapshot to support:

- position conversion,
- diagnostics,
- semantic tokens,
- rename/edit application,
- navigation and hover spans.

---

## 7. Analysis Model

## 7.1 Analysis Stages

For each document snapshot, the server should support staged analysis:

1. lexing,
2. parsing,
3. AST construction,
4. binding,
5. type and shape analysis,
6. optional lowered semantic classification.

Not every feature requires every stage.

## 7.2 Syntax-Only Features

The following may operate on syntax trees or AST only:

- syntax diagnostics,
- basic document symbols,
- folding ranges,
- bracket matching,
- syntax highlighting fallback.

## 7.3 Semantic Features

The following require binding or deeper semantic analysis:

- semantic diagnostics,
- intrinsic-family completion,
- hover type and API information,
- definition and references,
- rename,
- semantic tokens,
- signature help,
- code actions,
- inlay hints.

---

## 8. Error Tolerance and Recovery

The language server must remain useful in incomplete or malformed code.

### 8.1 Required Recovery Areas

The parser should recover at least from:

1. missing braces,
2. incomplete declarations,
3. incomplete function headers,
4. malformed `[]` collection expressions,
5. malformed generator expressions `(...)` with `for` or `->`,
6. malformed spread and range forms,
7. malformed `if then else`,
8. malformed `for`, `while`, or `->`,
9. malformed input shorthand,
10. malformed output shorthand,
11. malformed modified arguments (`ref`, `out`, `in`),
12. malformed `:=`,
13. malformed slicing/index-from-end syntax,
14. malformed `public:` / `private:` style section labels,
15. malformed `operator<=>(other)` shorthand,
16. malformed known data-structure operator usage.

### 8.2 Partial Trees

Recovered nodes should be marked so later analysis can degrade gracefully.

### 8.3 Diagnostic Stability

The server should minimize cascading diagnostics. One missing token should not produce broad unstable floods of unrelated errors.

---

## 9. Symbol Model

## 9.1 Symbol Kinds

The semantic layer should distinguish at least the following symbol kinds:

- local variable,
- parameter,
- function,
- type,
- field,
- property,
- method,
- constructor,
- intrinsic object,
- intrinsic helper or intrinsic family symbol,
- namespace,
- loop variable,
- fast-iteration binding,
- tuple element pseudo-symbol,
- external .NET symbol,
- discard pseudo-symbol,
- section-label pseudo-symbol,
- ordering-shorthand pseudo-symbol.

## 9.2 Symbol Identity

Each symbol should have a stable internal identity suitable for:

- definition lookup,
- reference grouping,
- rename validation,
- semantic token classification.

Recommended fields:

```txt
symbolId
name
kind
declarationSpan
containerSymbolId?
typeInfo?
isMutable
isIntrinsic
isExternalDotNet
```

## 9.3 Intrinsic Symbols

The language server shall model the following as intrinsic symbols or intrinsic semantic categories:

- `stdin`,
- `stdout`,
- `Array.zero`,
- declaration-based input shorthand,
- statement-based output shorthand,
- aggregate intrinsic families (`min`, `max`, `sum`, `sumBy`, `minBy`, `maxBy`, `chmin`, `chmax`),
- conversion-keyword calls (`int`, `long`, `double`, `decimal`, `bool`, `char`, `string` in expression position),
- comparator sugar `T.asc` and `T.desc`,
- known data-structure operator rewrites,
- `new[n]` and `new![n]`,
- ordering shorthand `operator<=>(other)`.

These shall participate in:

- completion,
- hover,
- signature help,
- semantic tokens,
- code actions,
- diagnostics.

---

## 10. Interaction with Pass-Through C#/.NET Surface

## 10.1 General Rule

The language server shall preserve and understand ordinary C#/.NET surface accepted by PSCP.

Examples include:

- `namespace`,
- `using`,
- `class`, `struct`, `record`,
- `new`, `this`, `base`,
- ordinary generic types,
- ordinary method calls,
- `is`, `is not`,
- switch-expression surface,
- ordinary access modifiers without colon,
- target-typed `new()`,
- ordinary .NET type/member names.

## 10.2 External Symbol Integration

The implementation may expose .NET symbols through:

1. metadata reflection,
2. Roslyn integration,
3. precomputed stubs,
4. simplified symbol databases.

A conforming implementation may choose a limited external-symbol strategy for `v0.4`, provided syntax and name classification remain correct.

## 10.3 Access Modifier Distinction

The language server must clearly distinguish:

- `public` / `private` / `protected` / `internal` without colon as pass-through C# modifiers,
- `public:` / `private:` / `protected:` / `internal:` with colon as PSCP section labels.

This distinction is important for:

- diagnostics,
- hover,
- semantic tokens,
- code actions,
- completion suggestions.

---

## 11. LSP Initialization

## 11.1 `initialize`

The server shall accept `initialize` and advertise capabilities according to implemented features.

Recommended `v0.4` capabilities:

- incremental text sync,
- hover provider,
- completion provider,
- definition provider,
- references provider,
- rename provider,
- document symbol provider,
- semantic tokens provider,
- signature help provider,
- code action provider,
- inlay hint provider,
- optional folding range provider,
- optional document formatting provider.

## 11.2 `initialized`

After `initialized`, the server may:

- warm intrinsic documentation caches,
- prepare aggregate-family metadata,
- prepare conversion-keyword metadata,
- prepare known data-structure rewrite metadata,
- start background workspace indexing,
- warm .NET symbol metadata caches.

---

## 12. Text Synchronization

## 12.1 Recommended Sync Mode

Incremental synchronization is recommended.

## 12.2 Supported Notifications

The server shall support:

- `textDocument/didOpen`,
- `textDocument/didChange`,
- `textDocument/didClose`,
- `textDocument/didSave`.

## 12.3 Versioning

All analysis results must be associated with a specific document version.

Stale results must not be published for newer snapshots.

## 12.4 Cancellation

If analysis for version `n` is still running when version `n+1` arrives, older analysis should be cancelled or discarded unless safely reusable.

---

## 13. Diagnostics

## 13.1 Categories

Diagnostics should be divided into:

1. lexical diagnostics,
2. syntax diagnostics,
3. binding diagnostics,
4. type diagnostics,
5. intrinsic semantic diagnostics,
6. optional style hints.

## 13.2 Required Diagnostic Areas

The following language-specific diagnostics should be supported:

1. immutable assignment,
2. illegal use of `_` as a value,
3. recursive self-reference without `rec`,
4. invalid tuple projection,
5. invalid spread outside `[]`,
6. malformed or unsupported input shorthand,
7. malformed or unsupported output shorthand,
8. `break` / `continue` outside loop,
9. malformed single-statement `then` / `else` / `do`,
10. malformed or ambiguous space-separated call,
11. invalid use of `ref`, `out`, or `in`,
12. invalid out-variable declaration,
13. invalid `T.asc` / `T.desc`,
14. invalid known collection auto-construction target,
15. invalid `new![n]` target type,
16. invalid aggregate intrinsic call shape,
17. invalid conversion-keyword usage,
18. invalid known data-structure operator target,
19. invalid `:=` target or context,
20. invalid slicing/index-from-end target or syntax,
21. invalid `operator<=>(other)` shorthand usage,
22. misuse of section labels,
23. access-section warning diagnostics,
24. implicit return mismatch with declared return type,
25. final bare invocation in value-returning context where a value is required,
26. unsupported pass-through construct in the current implementation subset.

## 13.3 Experimental-Feature Diagnostics

The following should normally be warnings or informational diagnostics rather than hard errors unless the implementation explicitly chooses stricter behavior:

- `public:` / `private:` / `protected:` / `internal:` section labels,
- partial support of ordering shorthand,
- partial support of known data-structure operator rewrites.

## 13.4 Publication Strategy

Diagnostics should be published in two waves when practical:

1. fast syntax diagnostics,
2. semantic diagnostics after binding/type analysis.

Each publication replaces prior diagnostics for the same document version.

---

## 14. Completion

## 14.1 General Principles

Completion should prioritize:

1. low latency,
2. local relevance,
3. practical contest-oriented suggestions,
4. continuity with ordinary C#/.NET editing expectations.

## 14.2 Completion Sources

The completion provider should combine:

1. keyword completion,
2. local symbol completion,
3. intrinsic API completion,
4. aggregate-family completion,
5. conversion-keyword completion in expression position,
6. member completion for typed receivers,
7. pass-through .NET type/member completion where available,
8. syntax-shape suggestions.

## 14.3 Important Contexts

The completion engine should recognize at least:

1. statement start,
2. expression position,
3. after `.` member access,
4. type position,
5. after `for`, `in`, `->`, `if`, `then`, `else`, `return`, `do`,
6. after `stdin.`, `stdout.`, `Array.`,
7. inside `[]` collection expressions,
8. inside generator expressions `(...)`,
9. inside type declarations,
10. after `ref`, `out`, or `in`,
11. after a type before `.asc` / `.desc`,
12. after `new[` / `new![`,
13. after a receiver that may support known data-structure rewrites.

## 14.4 Context-Sensitive Suggestions

### Statement Start

Suggest:

- `let`
- `var`
- `mut`
- `if`
- `for`
- `while`
- `return`
- `break`
- `continue`
- `=`
- `+=`
- `class`
- `struct`
- `record`
- `using`
- `namespace`

### Expression Position

Suggest:

- aggregate family names,
- conversion-keyword forms,
- local symbols,
- relevant .NET members.

### After `stdin.`

Suggest intrinsic input members.

### After `stdout.`

Suggest intrinsic output members.

### After `Array.`

Suggest `zero`.

### After a tuple expression receiver and `.`

Suggest `.1`, `.2`, `.3`, ... when tuple arity is known.

### Aggregate Call Position

Suggest:

- `min`
- `max`
- `sum`
- `sumBy`
- `minBy`
- `maxBy`
- `chmin`
- `chmax`

### Conversion Position

When a type keyword would be valid as an expression-level conversion, the server may suggest:

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

### After a type before comparator sugar

Suggest:

- `asc`
- `desc`

when the type is orderable or orderability cannot yet be excluded.

### In type bodies near section labels

Suggest both:

- pass-through modifiers `public`, `private`, `protected`, `internal`,
- experimental section labels `public:`, `private:`, `protected:`, `internal:`

with documentation explaining the difference.

### Known Data-Structure Rewrite Contexts

If receiver type is known to be one of the supported structures, the server may surface hint completions or docs for:

- `+=`
- `-=` for `HashSet<T>`
- `~`
- prefix `--`

through hover/code action/documentation rather than ordinary completion where operator completion is not practical.

## 14.5 Snippet Completions

Optional snippets:

- `if { } else { }`
- `for i in 0..<n { }`
- `xs -> x { }`
- `sum (0..<n -> i do ...)`
- `min [0..<n -> i do ...]`
- `chmin(ref target, value)`
- `new[n]`
- `new![n]`
- `operator<=>(other) => ...`
- declaration-based input shorthand.

---

## 15. Hover

## 15.1 Required Content

Hover should provide, when available:

1. symbol kind,
2. declared or inferred type,
3. mutability,
4. declaration or signature,
5. intrinsic documentation for language-owned features,
6. sugar expansion meaning,
7. pass-through .NET type/member information where available.

## 15.2 Important Hover Cases

Hover should be especially helpful for:

- `stdin.int`, `stdin.array<T>(n)`, `stdin.gridInt(n, m)`,
- `stdout.write`, `stdout.grid`,
- `Array.zero`,
- declaration-based input shorthand,
- statement-based output shorthand,
- generator expressions vs materialized collection expressions,
- `T.asc` / `T.desc`,
- aggregate-family names,
- conversion-keyword usage,
- `new[n]`,
- `new![n]`,
- `HashSet +=`, `Stack +=`, `Queue +=`, `PriorityQueue +=`,
- `~receiver`, `--receiver` for known data structures,
- `p.2` tuple projections,
- slicing/index-from-end syntax,
- `ref` / `out` / `in` modified arguments,
- `:=`,
- `public:` / `private:` labels vs pass-through modifiers,
- `operator<=>(other)` shorthand.

## 15.3 Experimental Warning Content

For section labels, hover should make clear that:

- they are PSCP-specific,
- they differ from ordinary C# modifiers,
- they are currently lightweight and warning-oriented,
- transpilation is not blocked by them.

---

## 16. Go to Definition

## 16.1 Required Targets

Go-to-definition should support:

- functions,
- local variables,
- parameters,
- loop variables,
- type declarations,
- fields and methods declared in source,
- intrinsic objects and helper surfaces,
- aggregate-family intrinsics,
- conversion-keyword intrinsic docs,
- pass-through .NET symbols when metadata exists.

## 16.2 Special Cases

### Tuple projections

For `p.2`, the server may:

1. navigate to the tuple-producing declaration, or
2. provide no definition and rely on hover.

### Shorthand syntax

For declaration-based input and statement-based output shorthand, definition may target the corresponding intrinsic documentation surface.

### Comparator sugar

For `int.desc`, definition may resolve to intrinsic comparator documentation rather than a source declaration.

### Known DS operator rewrites

For operator uses like `visited += x`, definition may target the receiver type member (`Add`) or the intrinsic rewrite documentation surface. Either is acceptable if presented consistently.

### Ordering shorthand

For `operator<=>(other)`, navigation should target the shorthand declaration site.

---

## 17. Find References

References should be supported for:

- user-declared variables,
- functions,
- parameters,
- loop variables,
- type declarations,
- fields and methods declared in source.

Reference classification should distinguish:

1. declaration,
2. read,
3. write.

Intrinsic names and pass-through external symbols may be handled more conservatively depending on implementation capabilities.

---

## 18. Rename

## 18.1 Supported Rename Targets

Rename should be supported for:

- variables,
- parameters,
- functions,
- type declarations,
- fields and methods declared in the supported source subset,
- loop and fast-iteration variables.

## 18.2 Unsupported Rename Targets

Rename shall be rejected for:

- discard `_`,
- intrinsic objects such as `stdin`, `stdout`,
- aggregate-family intrinsic names,
- conversion-keyword intrinsics,
- comparator suffixes `asc` and `desc`,
- tuple projections such as `.1`, `.2`,
- section labels with colon,
- keywords,
- generated temporaries,
- external .NET symbols unless explicitly supported.

## 18.3 Validation

Rename shall validate that the new name:

1. is a valid identifier,
2. is not a reserved keyword unless escaping is supported,
3. does not introduce an illegal conflict.

---

## 19. Document Symbols

## 19.1 Required Symbol Kinds

The document symbol provider should expose at least:

- functions,
- top-level declarations,
- type declarations,
- constructors,
- methods inside type declarations,
- fields inside supported type declarations,
- optionally significant local declarations.

## 19.2 Special Symbol Presentation

Document symbols may also optionally expose:

- section-label regions,
- ordering shorthand declarations,
- top-level synthetic grouping for contest-style files.

## 19.3 Nesting

Document symbols should reflect lexical containment.

---

## 20. Semantic Tokens

## 20.1 Recommended Token Types

Recommended token types:

- keyword,
- variable,
- parameter,
- function,
- method,
- type,
- number,
- string,
- operator,
- property,
- namespace,
- class,
- struct,
- interface,
- builtin or intrinsic via token modifier.

## 20.2 Recommended Token Modifiers

Recommended modifiers:

- declaration,
- readonly,
- modification,
- intrinsic,
- defaultLibrary,
- mutable,
- static,
- external,
- experimental.

## 20.3 Language-Specific Highlighting

The semantic token provider should distinguish:

- `let` bindings as immutable,
- `var` and `mut` bindings as mutable,
- `stdin`, `stdout`, `Array.zero` as intrinsic/default-library,
- `then`, `else`, `do`, `rec`,
- output shorthand leaders `=` and `+=` when used as statements,
- tuple projections `.1`, `.2`, ...,
- comparator sugar `.asc`, `.desc`,
- aggregate-family intrinsic names,
- conversion-keyword calls in expression position,
- `:=`,
- `new[n]` / `new![n]`,
- known data-structure operator rewrites when semantically recognized,
- `public:` / `private:` / `protected:` / `internal:` as experimental section labels,
- pass-through access modifiers without colon as normal keywords.

## 20.4 Full and Range Modes

The server should implement full semantic tokens first. Range-based updates are optional.

---

## 21. Signature Help

## 21.1 Supported Forms

Signature help should support:

- parenthesized calls,
- space-separated calls,
- intrinsic APIs,
- aggregate-family intrinsic calls,
- conversion-keyword docs where useful,
- pass-through .NET calls when metadata is available,
- constructors.

## 21.2 Modified Arguments

Signature help should understand active parameter position in the presence of:

- `ref`,
- `out`,
- `in`,
- out-variable declarations,
- `out _` discards.

## 21.3 Aggregate Intrinsic Signatures

The server should surface signatures such as:

- `min(left, right)`
- `min(a, b, c, ...)`
- `min(values)`
- `max(left, right)`
- `max(a, b, c, ...)`
- `max(values)`
- `sum(values)`
- `sumBy(values, selector)`
- `minBy(values, keySelector)`
- `maxBy(values, keySelector)`
- `chmin(ref target, value)`
- `chmax(ref target, value)`

## 21.4 Conversion-Keyword Signature Help

The server may provide compact doc-style signature help for conversion-keyword use, for example:

- `int(value)`
- `bool(value)`
- `string(value)`

with notes about broad truthiness/cast/parse behavior.

---

## 22. Inlay Hints

## 22.1 Goals

Inlay hints should improve readability without cluttering short contest code.

## 22.2 Recommended Hint Categories

1. inferred types for nontrivial `let` and `var`,
2. parameter labels for intrinsic helpers,
3. tuple projection type hints,
4. aggregate-family overload hints, optional,
5. conversion-keyword result hints, optional,
6. generator-vs-materialized hinting, optional,
7. section-label warning hints, optional,
8. implicit return hints, optional.

## 22.3 Good Candidate Hints

Examples:

- inferred type for `let best = min (...)`,
- inferred type for `let cmp = int.desc`,
- optional hint that `List<int>[] graph = new![n]` initializes each element,
- optional hint that `HashSet +=` returns `bool`,
- optional hint that `int "123"` is parse-like rather than a type declaration.

---

## 23. Code Actions

## 23.1 Required Quick Fixes

Recommended quick fixes for `v0.4`:

1. add missing `rec` to recursive function,
2. insert braces for malformed single-statement `then` / `else` / `do`,
3. replace final bare invocation with explicit `return`,
4. replace ignored value with `_ = expr` when desired,
5. convert `int[n] arr;` to `int[] arr = Array.zero(n)` if explicit form is preferred,
6. convert declaration shorthand to explicit `stdin` form or vice versa,
7. add parentheses to disambiguate a space-separated call,
8. convert immutable binding to mutable when assignment error occurs,
9. rewrite `Math.Min(a, b)` to `min a b` when style preference is enabled,
10. rewrite `Math.Max(a, b)` to `max a b` when style preference is enabled,
11. rewrite explicit comparer factory to `T.asc` / `T.desc` when appropriate,
12. rewrite manual compare-update into `chmin` / `chmax` when safe,
13. rewrite explicit parse/cast into conversion-keyword form when style preference is enabled,
14. rewrite explicit array allocation plus fill into `new![n]` when safe and appropriate,
15. rewrite `visited.Add(x)` to `visited += x` when style preference is enabled and receiver type is known,
16. explain that `public:` differs from `public` when a likely mistake is detected.

## 23.2 Optional Refactorings

Optional refactorings include:

- convert parenthesized call to space-separated call,
- convert space-separated call to parenthesized call,
- convert one-line `if then else` to block form,
- convert block form to one-line form when safe,
- convert `sum (...)` to explicit loop preview for debugging,
- convert `new![n]` into explicit allocation plus fill,
- convert section labels into explicit pass-through modifiers if desired.

---

## 24. Formatting

Formatting remains optional.

If implemented, the formatter should:

1. preserve explicit braces,
2. preserve pass-through C# type syntax,
3. keep single-statement `then` / `else` / `do` forms one-line only when short,
4. preserve space-separated calls unless a style rule explicitly rewrites them,
5. preserve declaration-based input and statement-based output shorthand,
6. preserve comparator sugar and aggregate-family call forms,
7. preserve visible distinction between `[]` materialized forms and `()` generator forms,
8. preserve `public:` / `private:` labels exactly when present.

---

## 25. Folding Ranges

Optional for `v0.4`.

Suggested foldable regions:

- function bodies,
- type declarations,
- constructors and methods,
- multiline collection expressions,
- multiline generator expressions,
- multiline builder blocks,
- multiline comments,
- multiline `if` branches,
- experimental section-label regions.

---

## 26. Workspace Indexing

## 26.1 Index Scope

The workspace index should track:

- function declarations,
- type declarations,
- constructors, methods, and fields in supported type declarations,
- top-level declarations,
- intrinsic symbol documentation metadata,
- optional external .NET symbol metadata.

## 26.2 Single-File Priority

Because PSCP targets competitive programming, single-file responsiveness takes priority over deep project-scale features.

## 26.3 Background Work

Workspace indexing should be background and cancellable. Open-file analysis takes precedence.

---

## 27. Performance Strategy

## 27.1 Latency Priorities

Recommended priority order for foreground requests:

1. completion,
2. hover,
3. diagnostics for current open file,
4. definition,
5. signature help,
6. references,
7. rename,
8. background workspace indexing.

## 27.2 Caching

The server should cache at least:

- tokens,
- syntax trees,
- ASTs,
- semantic models,
- symbol tables,
- semantic token results,
- document symbol results,
- intrinsic overload metadata.

## 27.3 Incrementality

Whole-file reparsing is acceptable for `v0.4` if performance remains suitable for contest-sized files. Incremental parsing is desirable but not required.

---

## 28. Testing Strategy

## 28.1 Parser and Recovery Tests

Required tests should include:

- malformed input shorthand,
- malformed output shorthand,
- malformed `[]` builder forms,
- malformed generator expressions `(...)`,
- malformed spread/range forms,
- malformed `->`,
- malformed modified arguments,
- malformed `:=`,
- malformed slicing/index-from-end syntax,
- malformed section labels,
- malformed `operator<=>(other)`,
- malformed known data-structure operator contexts.

## 28.2 Semantic Feature Tests

Required tests should include:

- missing `rec`,
- immutable assignment diagnostics,
- tuple projection typing,
- invalid `T.asc` / `T.desc`,
- invalid auto-construction targets,
- invalid `new![n]`,
- aggregate-family classification,
- conversion-keyword classification and diagnostics,
- `HashSet +=` bool-return awareness,
- `Stack` / `Queue` / `PriorityQueue` rewrite recognition,
- `:=` typing and value behavior,
- section-label warning behavior,
- ordering shorthand recognition,
- implicit return behavior,
- final bare invocation in value-returning contexts.

## 28.3 LSP Integration Tests

Required integration tests should include:

- open/change/close lifecycle,
- version-safe diagnostics,
- cancellation of stale analysis,
- completion in incomplete code,
- semantic tokens in partially broken documents,
- hover on shorthand forms,
- hover on section labels vs pass-through modifiers,
- signature help for aggregate-family calls,
- signature help for space-separated calls with modified arguments,
- code actions involving aggregate/conversion/data-structure sugar,
- rename of locals and supported type members.

---

## 29. Suggested Development Order

Recommended implementation order:

1. document store and snapshot model,
2. syntax diagnostics,
3. binding and semantic model integration,
4. hover,
5. completion,
6. go-to-definition,
7. document symbols,
8. semantic tokens,
9. signature help,
10. references,
11. rename,
12. code actions,
13. inlay hints,
14. optional formatting and advanced features.

Within completion and hover, prioritize:

- shorthand I/O,
- aggregate family,
- conversion-keyword forms,
- generator vs materialized distinction,
- `new[n]` / `new![n]`,
- known data-structure operator rewrites,
- section labels,
- ordering shorthand,
- pass-through type/member support.

---

## 30. Minimal Viable `v0.4` LSP Feature Set

A minimal viable `v0.4` language server shall implement:

1. document synchronization,
2. syntax diagnostics,
3. core semantic diagnostics,
4. completion for keywords, locals, intrinsics, aggregate family, conversion-keyword forms, and basic pass-through surface,
5. hover for locals, functions, intrinsics, aggregate family, shorthand forms, and section labels,
6. go-to-definition for user symbols and intrinsic surfaces,
7. document symbols,
8. semantic tokens.

A strong `v0.4` implementation should additionally implement:

9. references,
10. signature help,
11. rename,
12. code actions,
13. inlay hints.

---

## 31. Example Semantic Cases

### 31.1 Declaration-Based Input

For:

```txt
int n =
int[n] arr =
```

The language server should:

- classify both as declarations,
- resolve `n` and `arr` as ordinary symbols,
- provide hover explaining shorthand meaning,
- offer conversion to explicit `stdin` form if implemented.

### 31.2 Statement-Based Output

For:

```txt
= ans
+= arr
```

The language server should:

- parse these as output statements at statement start,
- highlight them distinctly if semantic tokens support it,
- provide hover documenting write and writeline semantics.

### 31.3 Generator vs Materialized Builder

For:

```txt
sum (0..<n -> i do score(i))
let xs = [0..<n -> i do score(i)]
```

The language server should:

- distinguish the first as generator-fed aggregate usage,
- distinguish the second as materialized collection construction,
- surface that distinction in hover and optional hints.

### 31.4 Comparator Sugar

For:

```txt
let cmp = int.desc
```

The language server should:

- infer that `cmp` is comparer-like,
- provide hover documenting descending default comparer semantics,
- offer completion for `asc` and `desc` after `int.`.

### 31.5 Known Collection Auto-Construction

For:

```txt
PriorityQueue<int, int> pq;
```

The language server should:

- classify it as a declaration,
- understand that auto-construction semantics apply,
- surface this behavior in hover,
- optionally offer a quick action to rewrite it as `PriorityQueue<int, int> pq = new()`.

### 31.6 `new![n]`

For:

```txt
List<int>[] graph = new![n]
```

The language server should:

- validate that the element type is eligible,
- provide hover explaining per-element auto-construction,
- offer explicit rewrite if code actions support it.

### 31.7 HashSet Operator Rewrite

For:

```txt
if not (visited += me) then continue
```

The language server should:

- recognize `visited += me` as `HashSet.Add(me)` semantics,
- know that the expression returns `bool`,
- provide hover explaining the rewrite.

### 31.8 Section Labels

For:

```txt
class Node {
private:
    foo() { ... }
public:
    bar() { ... }
}
```

The language server should:

- recognize the labels as PSCP section labels,
- distinguish them from pass-through modifiers,
- optionally warn that the feature is lightweight/experimental,
- apply source-level intended accessibility to unlabeled methods for analysis if implemented.

### 31.9 Ordering Shorthand

For:

```txt
record struct Job(int Id, int Arrival, long Time) {
    operator<=>(other) => Arrival <=> other.Arrival
}
```

The language server should:

- recognize this as default-ordering shorthand,
- provide hover and semantic classification,
- surface completion/docs for comparator sugar usage where appropriate.

---

## 32. Conformance

A language server conforms to this specification if it:

1. correctly tracks documents and versions,
2. provides syntax and semantic feedback consistent with the `v0.4` language, API, and transpiler specifications,
3. recognizes language-owned intrinsic features including shorthand I/O, aggregate families, conversion-keyword forms, comparator sugar, `new[n]`, `new![n]`, known data-structure operators, section labels, and ordering shorthand,
4. preserves ordinary pass-through C#/.NET surface behavior in editor tooling,
5. degrades gracefully in incomplete code.

Implementation details such as exact cache structure, external symbol metadata strategy, or formatter design remain implementation-defined.

---

## 33. Versioning

This document defines language server draft version `v0.4`.

Backward compatibility is not guaranteed between draft revisions.

