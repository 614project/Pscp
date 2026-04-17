# PS/CP Language Server Specification v0.3

## 1. Scope

This document defines version `v0.3` of the Language Server Protocol (LSP) server specification for the PS/CP language.

This document specifies:

1. language server architecture,
2. document and workspace model,
3. analysis pipeline integration,
4. feature behavior for syntax-aware and semantic-aware tooling,
5. intrinsic-symbol handling,
6. pass-through C#/.NET surface handling,
7. diagnostics policy,
8. implementation priorities.

This document is implementation-oriented and assumes the existence of the frontend and transpiler model described by the language, API, and transpiler specifications.

This document is normative unless otherwise stated.

---

## 2. Goals

The language server shall provide responsive and practical editor tooling for problem solving and competitive programming workflows.

The primary goals are:

1. low-latency feedback while typing,
2. reliable syntax and semantic diagnostics,
3. good support for contest-style single-file editing,
4. correct handling of language-owned sugar,
5. correct handling of pass-through C#/.NET surface,
6. predictable behavior in incomplete code,
7. useful completions for both intrinsic APIs and ordinary .NET usage.

---

## 3. Non-Goals

The following are outside the required scope of `v0.3` unless otherwise noted:

1. full build orchestration,
2. live execution or judging integration,
3. debugger integration,
4. semantic understanding of arbitrary external assemblies beyond the implementation's available symbol metadata,
5. advanced multi-file refactorings,
6. mandatory formatting support,
7. code generation preview as a required editor feature.

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
- request and notification dispatch,
- capability negotiation,
- cancellation,
- error propagation,
- optional progress reporting.

### 4.2 Workspace Manager

Responsible for:

- workspace folders,
- open document tracking,
- configuration state,
- global indexing lifecycle,
- symbol metadata integration.

### 4.3 Document Store

Responsible for:

- text snapshots by URI,
- version tracking,
- incremental text changes,
- line maps,
- UTF-16 position mapping for LSP.

### 4.4 Analysis Scheduler

Responsible for:

- debouncing edits,
- cancellation of stale work,
- foreground/background prioritization,
- scheduling lex/parse/bind/type passes.

### 4.5 Frontend Service Layer

Wraps compiler frontend services and exposes:

- tokens,
- syntax tree,
- AST,
- bound tree,
- type information,
- symbol tables,
- lowered semantic classifications as needed.

### 4.6 Semantic Model Cache

Responsible for caching:

- syntax trees,
- ASTs,
- diagnostics,
- semantic models,
- semantic token data,
- symbol indexes,
- completion contexts when appropriate.

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
- inlay hints,
- signature help,
- code actions,
- optional formatting,
- optional folding ranges.

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
- text snapshot.

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

All analysis shall operate on immutable document snapshots.

## 6.3 Line Index

A line index shall be maintained for each snapshot to support:

- LSP position conversion,
- diagnostics,
- semantic tokens,
- rename/edit ranges,
- hover and navigation spans.

---

## 7. Analysis Model

## 7.1 Analysis Stages

For each document snapshot, the server should support staged analysis:

1. lexing,
2. parsing,
3. AST construction,
4. binding,
5. type and shape analysis,
6. optional lowered-model derivation.

Not every feature requires every stage.

## 7.2 Syntax-Only Features

The following may operate on syntax trees or AST only:

- syntax diagnostics,
- basic document symbols,
- folding ranges,
- bracket matching,
- syntax highlighting fallback.

## 7.3 Semantic Features

The following require semantic analysis:

- semantic diagnostics,
- completion ranking by meaning,
- hover types and intrinsic docs,
- go-to-definition,
- references,
- rename,
- semantic tokens,
- inlay hints,
- signature help,
- code actions based on semantic intent.

---

## 8. Error Tolerance and Recovery

The language server must remain useful in incomplete or malformed code.

### 8.1 Required Recovery Areas

The parser should recover at least from:

1. missing braces,
2. incomplete declarations,
3. incomplete function headers,
4. incomplete collection expressions,
5. malformed spread and range forms,
6. malformed `if then else`,
7. malformed `for`, `while`, or `->`,
8. malformed input shorthand,
9. malformed output shorthand,
10. malformed modified arguments (`ref`, `out`, `in`),
11. malformed type declarations,
12. malformed comparator sugar,
13. malformed intrinsic aggregate calls.

### 8.2 Partial Trees

Recovered syntax nodes should be marked so later analysis can degrade gracefully.

### 8.3 Diagnostic Stability

The server should minimize cascading diagnostics. One missing token should not cause broad unstable error floods.

---

## 9. Symbol Model

## 9.1 Symbol Kinds

The semantic layer should distinguish at least the following symbol kinds:

- local variable,
- parameter,
- function,
- type,
- field,
- property or property-like member,
- method,
- constructor,
- intrinsic object,
- intrinsic helper,
- loop variable,
- fast-iteration binding,
- tuple element pseudo-symbol,
- namespace,
- external .NET symbol,
- discard pseudo-symbol.

## 9.2 Symbol Identity

Each symbol should have a stable internal identity suitable for:

- definition lookup,
- reference collection,
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

The language server shall model the following as intrinsic symbols:

- `stdin`,
- `stdout`,
- `Array.zero`,
- comparator sugar surfaces such as `T.asc` and `T.desc`,
- declaration-based input shorthand semantic targets,
- statement-based output shorthand semantic targets,
- compiler-known aggregate intrinsics such as `min`, `max`, `sum`, `minBy`, `maxBy`, `sumBy`, `chmin`, `chmax`.

These shall participate in:

- completion,
- hover,
- signature help,
- semantic tokens,
- code actions.

---

## 10. Interaction with Pass-Through C#/.NET Surface

## 10.1 General Rule

The language server shall preserve and understand ordinary C#/.NET surface accepted by the language.

Examples include:

- `namespace`,
- `using`,
- `class`, `struct`, `record`,
- `new`, `this`, `base`,
- ordinary generic types,
- ordinary method calls,
- `is`, `is not`,
- switch-expression surface,
- `ref`, `out`, `in`,
- target-typed `new()`,
- ordinary .NET type/member names.

## 10.2 External Symbol Integration

The implementation may expose .NET symbols through:

1. metadata reflection,
2. Roslyn integration,
3. precomputed stubs,
4. simplified symbol databases.

A conforming implementation may choose a limited external-symbol strategy for `v0.3`, provided syntax and name classification remain correct.

## 10.3 Completions for Pass-Through Surface

Completion should continue to work for ordinary .NET usage such as:

```txt
Queue<int> queue = new()
PriorityQueue<int, int> pq = new()
HashSet<int> set = new()
Math.Max(a, b)
```

The server shall not treat these as language-owned aliases.

---

## 11. LSP Initialization

## 11.1 `initialize`

The server shall accept `initialize` and advertise capabilities according to the implemented feature set.

Recommended `v0.3` capabilities:

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

- warm intrinsic symbol documentation,
- initialize comparer/intrinsic metadata,
- start background workspace indexing,
- prepare .NET symbol metadata caches.

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

All analysis results must be tied to a specific document version.

Stale results must not be published for newer snapshots.

## 12.4 Cancellation

If analysis for version `n` is still running when version `n+1` arrives, the older analysis should be cancelled or discarded unless partial results can be safely reused.

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
5. invalid spread outside collection expression,
6. malformed or unsupported input shorthand,
7. malformed or unsupported output shorthand,
8. `break`/`continue` outside loop,
9. malformed one-line `then` or `do`,
10. malformed or ambiguous space-separated call,
11. invalid use of `ref`, `out`, or `in`,
12. invalid out-variable declaration,
13. invalid `T.asc` / `T.desc`,
14. invalid use of known collection auto-construction,
15. invalid intrinsic aggregate call shape,
16. invalid `chmin` / `chmax` usage,
17. implicit return mismatch with declared return type,
18. final bare invocation in value-returning context where a value is required,
19. unsupported pass-through type/member construct in the current implementation subset.

## 13.3 Publication Strategy

Diagnostics should be published in two waves when possible:

1. fast syntax diagnostics,
2. semantic diagnostics after binding/type analysis.

Each publication replaces prior diagnostics for the same document version.

## 13.4 Severity Recommendations

- error: invalid syntax or semantic violation,
- warning: suspicious but legal usage,
- information: optional implementation notes,
- hint: style or simplification suggestions.

---

## 14. Completion

## 14.1 General Principles

Completion should prioritize:

1. low latency,
2. relevance to local context,
3. useful intrinsic suggestions,
4. continuity with ordinary C#/.NET editing expectations.

## 14.2 Completion Sources

The completion provider should combine:

1. keyword completion,
2. local symbol completion,
3. intrinsic API completion,
4. aggregate intrinsic completion,
5. member completion for typed receivers,
6. pass-through .NET type/member completion where available,
7. syntax-shape suggestions.

## 14.3 Important Contexts

The completion engine should recognize at least:

1. statement start,
2. expression position,
3. after `.` member access,
4. type position,
5. after `for`, `in`, `->`, `if`, `then`, `else`, `return`, `do`,
6. after `stdin.`, `stdout.`, `Array.`,
7. inside collection expressions,
8. inside type declarations,
9. argument position after `ref`, `out`, or `in`,
10. after a type name before `.asc` or `.desc`.

## 14.4 Context-Sensitive Suggestions

Examples:

### Statement start

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

### After `stdin.`

Suggest intrinsic input members.

### After `stdout.`

Suggest intrinsic output members.

### After `Array.`

Suggest `zero`.

### After a tuple expression receiver and `.`

Suggest valid tuple projections `.1`, `.2`, `.3`, ... when tuple arity is known.

### In an aggregate-call position

Suggest:

- `min`
- `max`
- `sum`
- `minBy`
- `maxBy`
- `sumBy`
- `chmin`
- `chmax`

### After a type before comparator sugar

If the parser supports completion after `T.`, suggest:

- `asc`
- `desc`

when the type is orderable or when orderability cannot yet be decided.

## 14.5 Snippet Completions

Optional snippet completions:

- `if { } else { }`
- `for i in 0..<n { }`
- `xs -> x { }`
- `rec int name(...) { }`
- `min [0..<n -> i do ...]`
- `sum [0..<n -> i do ...]`
- `chmin(ref target, value)`
- declaration-based input shorthand patterns.

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
7. type/member information for pass-through .NET constructs.

## 15.2 Important Hover Cases

Hover should be especially helpful for:

- `stdin.int`, `stdin.array<T>(n)`, `stdin.gridInt(n, m)`,
- `stdout.write`, `stdout.grid`,
- `Array.zero`,
- declaration-based input shorthand,
- statement-based output shorthand,
- `T.asc` / `T.desc`,
- `min`, `max`, `sum`, `minBy`, `maxBy`, `sumBy`, `chmin`, `chmax`,
- `int[n] arr;`,
- `List<int> list;` when auto-construction applies,
- `p.2` tuple projections,
- `ref` / `out` / `in` modified arguments,
- final expressions with implicit return significance.

## 15.3 Format

Hover content should be returned as markdown where supported.

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
- pass-through .NET symbols when metadata support exists.

## 16.2 Special Cases

### Tuple projections

For `p.2`, the server may:

1. navigate to the tuple-producing declaration, or
2. provide no definition and rely on hover.

### Shorthand syntax

For declaration-based input and statement-based output shorthand, go-to-definition on the intrinsic meaning may target the corresponding intrinsic API documentation surface.

### Comparator sugar

For `int.desc`, definition may resolve to intrinsic comparator documentation rather than a source declaration.

---

## 17. Find References

References should be supported for:

- local and global source declarations,
- function declarations,
- parameters,
- loop variables,
- type declarations,
- fields and methods declared in source.

Reference classification should distinguish:

1. declaration,
2. read,
3. write.

Intrinsic surfaces are not generally renameable and may or may not participate in reference search depending on implementation choice.

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
- comparator suffixes `asc` and `desc`,
- tuple projections such as `.1`, `.2`,
- keywords,
- generated temporaries,
- external .NET symbols unless explicitly supported.

## 18.3 Validation

Rename shall validate that the new name:

1. is a valid identifier,
2. is not a reserved keyword unless escaping is supported,
3. does not introduce an illegal conflict under the implementation's name rules.

---

## 19. Document Symbols

## 19.1 Required Symbol Kinds

The document symbol provider should expose at least:

- functions,
- top-level declarations,
- type declarations,
- fields and methods inside supported type declarations,
- optionally significant local declarations nested under functions.

## 19.2 Nesting

Document symbols should reflect lexical containment.

Example:

- class
  - constructor
  - method
  - field
- function
  - local declarations (optional)

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
- enumMember: optional,
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
- external.

## 20.3 Language-Specific Highlighting

The semantic token provider should distinguish:

- `let` bindings as immutable,
- `var` and `mut` bindings as mutable,
- `stdin`, `stdout`, `Array.zero` as intrinsic/default-library entities,
- `then`, `do`, `rec`,
- output shorthand leaders `=` and `+=` when used as statements,
- tuple projections `.1`, `.2`, ...,
- comparator sugar `.asc`, `.desc`,
- intrinsic aggregate names such as `min`, `max`, `sum`, `minBy`, `maxBy`, `sumBy`, `chmin`, `chmax`,
- pass-through C# nominal type keywords.

## 20.4 Full and Range Modes

The server should implement full semantic tokens first. Range-based updates are optional.

---

## 21. Signature Help

## 21.1 Supported Forms

Signature help should support:

- parenthesized calls,
- space-separated calls,
- intrinsic APIs,
- intrinsic aggregate calls,
- pass-through .NET calls when metadata is available,
- constructors.

## 21.2 Modified Arguments

Signature help should understand active parameter position in the presence of:

- `ref`,
- `out`,
- `in`,
- `out` variable declarations,
- `out _` discards.

## 21.3 Intrinsic Aggregate Signatures

The server should surface signatures such as:

- `min(left, right)`
- `min(values)`
- `max(left, right)`
- `max(values)`
- `sum(values)`
- `sumBy(values, selector)`
- `minBy(values, keySelector)`
- `maxBy(values, keySelector)`
- `chmin(ref target, value)`
- `chmax(ref target, value)`

The server may present both parenthesized and space-call usage in documentation.

---

## 22. Inlay Hints

## 22.1 Goals

Inlay hints should improve readability without cluttering short contest code.

## 22.2 Recommended Hint Categories

1. inferred types for nontrivial `let` and `var` declarations,
2. parameter labels for intrinsic helpers,
3. tuple projection type hints,
4. comparator sugar result hints, optional,
5. aggregate intrinsic disambiguation hints, optional,
6. implicit return hints, optional.

## 22.3 Good Candidate Hints

Examples:

- inferred type for `let best = min [ ... ]`,
- inferred type for `let cmp = int.desc`,
- optional hint that `List<int> list;` auto-constructs,
- optional hint that `int[n] arr;` allocates and initializes an array.

---

## 23. Code Actions

## 23.1 Required Quick Fixes

Recommended quick fixes for `v0.3`:

1. add missing `rec` to recursive function,
2. insert braces for malformed one-line forms,
3. replace final bare invocation with explicit `return`,
4. replace ignored value with `_ = expr`,
5. convert `int[n] arr;` to `int[] arr = Array.zero(n)` if explicit form is preferred,
6. convert declaration shorthand to explicit `stdin` helper form or vice versa,
7. add parentheses to disambiguate a space-separated call,
8. convert immutable binding to mutable when assignment error occurs,
9. rewrite `Math.Min(a, b)` to `min a b` when style preference is enabled,
10. rewrite `Math.Max(a, b)` to `max a b` when style preference is enabled,
11. rewrite explicit comparer factory to `T.asc` / `T.desc` when semantically appropriate,
12. rewrite manual compare-update pattern into `chmin` / `chmax` when safe.

## 23.2 Optional Refactorings

Optional refactorings include:

- convert parenthesized call to space-separated call,
- convert space-separated call to parenthesized call,
- convert one-line `if then else` to block form,
- convert block form to one-line form when safe,
- convert `min [ ... ]` into explicit loop form preview for debugging,
- convert auto-constructed known collection declaration into explicit `new()` form.

---

## 24. Formatting

Formatting remains optional.

If implemented, the formatter should:

1. preserve explicit braces,
2. preserve pass-through C# type syntax,
3. keep one-line `then` / `do` forms one-line only when short,
4. preserve space-separated calls unless a style rule explicitly rewrites them,
5. preserve declaration-based input and statement-based output shorthand as distinct source forms,
6. preserve comparator sugar and aggregate intrinsic call forms.

---

## 25. Folding Ranges

Optional for `v0.3`.

Suggested foldable regions:

- function bodies,
- type declarations,
- constructors and methods,
- multiline collection expressions,
- multiline builder blocks,
- multiline comments,
- multiline `if` branches.

---

## 26. Workspace Indexing

## 26.1 Index Scope

The workspace index should track:

- function declarations,
- type declarations,
- fields and methods in supported source-declared types,
- top-level declarations,
- intrinsic symbol documentation metadata,
- optional external .NET symbol metadata.

## 26.2 Single-File Priority

Because the language targets competitive programming, single-file responsiveness takes priority over deep project-level features.

## 26.3 Background Work

Workspace indexing should be background and cancellable. Open-file analysis always takes precedence.

---

## 27. Performance Strategy

## 27.1 Latency Priorities

Recommended priority order for foreground requests:

1. completion,
2. hover,
3. diagnostics for current open file,
4. definition,
5. references,
6. rename,
7. background workspace indexing.

## 27.2 Caching

The server should cache at least:

- tokens,
- syntax trees,
- ASTs,
- semantic models,
- symbol tables,
- semantic token results,
- document symbol results.

## 27.3 Incrementality

Whole-file reparsing is acceptable for `v0.3` if performance remains suitable for contest-sized files. Incremental parsing is desirable but not required.

---

## 28. Testing Strategy

## 28.1 Parser and Recovery Tests

Required tests should include:

- malformed input shorthand,
- malformed output shorthand,
- malformed spread/range/builder forms,
- malformed `->`,
- malformed modified arguments,
- malformed type declarations,
- ambiguous space-separated calls,
- malformed comparator sugar,
- malformed intrinsic aggregate calls.

## 28.2 Semantic Feature Tests

Required tests should include:

- missing `rec`,
- immutable assignment diagnostics,
- tuple projection typing,
- invalid `T.asc` / `T.desc`,
- invalid auto-construction targets,
- intrinsic aggregate call classification,
- `chmin` / `chmax` argument validation,
- implicit return behavior,
- final bare invocation in value-returning contexts,
- hover and completion for intrinsic aggregate family.

## 28.3 LSP Integration Tests

Required integration tests should include:

- open/change/close lifecycle,
- version-safe diagnostics,
- cancellation of stale analysis,
- completion in incomplete code,
- semantic tokens in partially broken documents,
- rename of locals and type members,
- go-to-definition on intrinsic surfaces,
- hover on shorthand forms,
- signature help for space-separated calls with modified arguments.

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
- modified arguments,
- intrinsic aggregate family,
- comparator sugar,
- known collection auto-construction,
- pass-through type/member support.

---

## 30. Minimal Viable `v0.3` LSP Feature Set

A minimal viable `v0.3` language server shall implement:

1. document synchronization,
2. syntax diagnostics,
3. core semantic diagnostics,
4. completion for keywords, locals, intrinsics, aggregate family, and basic pass-through surface,
5. hover for locals, functions, intrinsics, aggregate family, and shorthand forms,
6. go-to-definition for user symbols and intrinsic surfaces,
7. document symbols,
8. semantic tokens.

A strong `v0.3` implementation should additionally implement:

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
- offer quick conversion to explicit `stdin` form if implemented.

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

### 31.3 Comparator Sugar

For:

```txt
let cmp = int.desc
```

The language server should:

- infer that `cmp` is a comparer-like value,
- provide hover documenting descending default comparer semantics,
- offer completion for `asc` and `desc` after `int.` when appropriate.

### 31.4 Known Collection Auto-Construction

For:

```txt
PriorityQueue<int, int> pq;
```

The language server should:

- classify it as a declaration,
- understand that auto-construction semantics apply,
- surface this behavior in hover,
- optionally offer a quick action to rewrite it as `PriorityQueue<int, int> pq = new()`.

### 31.5 Aggregate Intrinsic Family

For:

```txt
let best = min [0..<n -> i do a[i] - i]
let total = sumBy arr (x => x * x)
_ = chmin(ref best, cand)
```

The language server should:

- classify these as intrinsic aggregate/comparison operations,
- provide correct hover and signature help,
- support completion for aggregate family names,
- validate `ref` usage for `chmin`.

### 31.6 Recursive Function

For:

```txt
int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

The language server should diagnose the missing `rec` keyword and provide an add-`rec` fix.

---

## 32. Conformance

A language server conforms to this specification if it:

1. correctly tracks documents and versions,
2. provides syntax and semantic feedback consistent with the `v0.3` language, API, and transpiler specifications,
3. recognizes language-owned intrinsic features including shorthand I/O, comparator sugar, aggregate family intrinsics, and known collection auto-construction,
4. preserves ordinary pass-through C#/.NET surface behavior in editor tooling,
5. degrades gracefully in incomplete code.

Implementation details such as exact cache structure, external symbol metadata strategy, or formatter design remain implementation-defined.

---

## 33. Versioning

This document defines language server draft version `v0.3`.

Backward compatibility is not guaranteed between draft revisions.

