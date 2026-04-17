# PS/CP Language Server Specification v0.1

## 1. Scope

This document defines the architecture, protocol behavior, data model, feature set, and implementation strategy for a Language Server Protocol (LSP) server targeting the PS/CP language.

This document is implementation-oriented. It assumes the existence of the following frontend components described elsewhere:

1. lexer,
2. parser,
3. AST,
4. binder / name resolver,
5. type checker,
6. desugaring and lowering pipeline.

The language server is not responsible for transpilation or execution, except where integration with those systems is explicitly defined.

This document is normative unless otherwise stated.

---

## 2. Goals

The language server shall provide editor tooling suitable for rapid problem-solving workflows, with emphasis on:

1. low-latency feedback,
2. accurate syntax and semantic diagnostics,
3. reliable navigation,
4. useful completion in a small language with deliberate syntax sugar,
5. good support for contest-style single-file editing,
6. predictable behavior in the presence of incomplete code.

---

## 3. Non-Goals

The following are outside the scope of v0.1 unless otherwise noted:

1. full-project build orchestration,
2. live transpilation previews,
3. code formatting guarantees,
4. refactoring beyond local rename and simple actions,
5. debugger integration,
6. semantic understanding of arbitrary external .NET assemblies beyond symbol exposure available from the implementation.

---

## 4. Protocol Version and Transport

The language server shall implement the Language Server Protocol over JSON-RPC 2.0.

The transport layer may be:

1. standard input/output,
2. named pipes,
3. sockets,
4. editor-provided process transport.

The reference implementation should prioritize standard input/output transport.

---

## 5. Server Identity

Recommended server identifier fields:

- language id: `pscp`
- server name: implementation-defined
- protocol implementation: LSP 3.x compatible

The server shall advertise capabilities based on the implemented feature set.

---

## 6. High-Level Architecture

The recommended language server architecture consists of the following subsystems:

1. protocol layer,
2. workspace manager,
3. document store,
4. incremental analysis scheduler,
5. frontend service layer,
6. semantic model cache,
7. feature providers,
8. diagnostics publisher.

### 6.1 Protocol Layer

Responsible for:

- JSON-RPC message handling,
- LSP request/notification dispatch,
- capability negotiation,
- cancellation token propagation,
- progress and error reporting.

### 6.2 Workspace Manager

Responsible for:

- tracking open documents,
- tracking workspace folders,
- resolving related source files,
- project-level configuration,
- global symbol cache lifecycle.

### 6.3 Document Store

Responsible for:

- storing document text by URI,
- version tracking,
- snapshot creation,
- incremental text change application,
- maintaining line maps.

### 6.4 Analysis Scheduler

Responsible for:

- debouncing edits,
- scheduling parse/bind/type passes,
- cancelling stale work,
- prioritizing foreground requests,
- preserving responsiveness under frequent typing.

### 6.5 Frontend Service Layer

Wraps the compiler frontend and exposes:

- lexed tokens,
- concrete syntax tree,
- AST,
- bound tree,
- type information,
- symbol tables,
- source maps.

### 6.6 Semantic Model Cache

Responsible for caching per-document analysis artifacts:

- syntax tree,
- AST,
- diagnostics,
- symbol index,
- semantic token data,
- hover and completion context precomputation where useful.

### 6.7 Feature Providers

Each LSP feature should be implemented as a provider over immutable snapshots.

Recommended providers:

- diagnostics provider,
- completion provider,
- hover provider,
- go-to-definition provider,
- references provider,
- document symbol provider,
- semantic token provider,
- inlay hint provider,
- signature help provider,
- rename provider,
- code action provider,
- formatting provider.

---

## 7. Document Model

## 7.1 Document Identity

Each open document is identified by URI and version.

Recommended model:

```txt
DocumentId
  uri: string

DocumentSnapshot
  id: DocumentId
  version: int
  text: string
  lineIndex: LineIndex
```

## 7.2 Snapshot Immutability

All analyses shall run against immutable document snapshots. Mutable shared state tied directly to the latest text buffer should be avoided.

## 7.3 Line Index

A line index shall be maintained for each snapshot to support:

- UTF-16 LSP position conversion,
- source span mapping,
- accurate diagnostics,
- semantic token ranges,
- rename/edit application.

---

## 8. Source Analysis Model

## 8.1 Analysis Stages

For each document snapshot, the server should support the following staged analysis:

1. lexing,
2. parsing,
3. AST construction,
4. binding,
5. type inference/checking,
6. optional lowered model generation.

Not every LSP feature requires every stage.

### 8.1.1 Syntax-Only Features

The following may operate on parse trees or AST only:

- syntax diagnostics,
- document symbols,
- simple folding ranges,
- bracket matching,
- syntax highlighting fallback.

### 8.1.2 Semantic Features

The following require binding or semantic analysis:

- semantic diagnostics,
- go to definition,
- references,
- hover type information,
- rename,
- inlay hints,
- semantic tokens,
- completion ranking by symbol kind.

---

## 9. Error Tolerance and Recovery

The language server must continue producing useful output in the presence of incomplete or invalid code.

### 9.1 Parser Recovery

The parser should support recovery at least for:

1. missing braces,
2. incomplete declarations,
3. incomplete function headers,
4. incomplete collection expressions,
5. incomplete `if then else`,
6. incomplete `for` / `while` / `->` constructs,
7. incomplete input/output shorthand,
8. malformed space-separated calls.

### 9.2 Partial Trees

Syntax nodes produced under recovery should be marked so later stages can degrade gracefully.

### 9.3 Diagnostic Stability

The server should avoid cascading diagnostics where possible. One missing token should not generate dozens of unstable errors.

---

## 10. Symbol Model

## 10.1 Symbol Kinds

The semantic layer should represent at least the following symbol kinds:

- local variable,
- parameter,
- function,
- intrinsic object,
- type,
- field or property exposed through .NET interop,
- method exposed through .NET interop,
- tuple element pseudo-symbol,
- loop variable,
- discard pseudo-symbol,
- generated temporary symbol, if exposed internally.

## 10.2 Symbol Identity

Every symbol should have a stable internal identity independent of source text spelling, sufficient for:

- definition lookup,
- reference grouping,
- rename eligibility checks,
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
```

## 10.3 Intrinsic Symbols

The server shall model `stdin`, `stdout`, and standard language helper surfaces as intrinsic symbols, not ordinary user declarations.

These symbols should participate in:

- completion,
- hover,
- signature help,
- semantic tokens.

---

## 11. Syntax Tree and Semantic Tree Exposure

The language server should define stable service interfaces for feature providers.

Recommended interfaces:

```txt
ISyntaxService
  Parse(snapshot) -> SyntaxTree
  GetTokenAt(snapshot, position)
  GetNodeAt(snapshot, position)
  GetEnclosingNode(snapshot, span)

ISemanticService
  Bind(snapshot) -> SemanticModel
  GetSymbolAt(snapshot, position)
  GetTypeAt(snapshot, position)
  GetDiagnostics(snapshot)
  FindReferences(snapshot, symbol)
  GetCompletions(snapshot, position)
```

---

## 12. LSP Initialization

## 12.1 `initialize`

The server shall accept `initialize` and respond with capabilities appropriate to the implemented feature set.

Recommended initial capabilities for v0.1:

- textDocumentSync: incremental,
- hoverProvider,
- completionProvider,
- definitionProvider,
- referencesProvider,
- documentSymbolProvider,
- semanticTokensProvider,
- signatureHelpProvider,
- renameProvider,
- codeActionProvider,
- inlayHintProvider,
- documentFormattingProvider: optional,
- foldingRangeProvider: optional.

## 12.2 `initialized`

After `initialized`, the server may:

- start background workspace indexing,
- preload intrinsic symbol metadata,
- prepare configuration state.

---

## 13. Text Synchronization

## 13.1 Recommended Sync Mode

The server should use incremental document synchronization.

## 13.2 Open / Change / Close Lifecycle

The server shall support:

- `textDocument/didOpen`,
- `textDocument/didChange`,
- `textDocument/didClose`,
- `textDocument/didSave`.

## 13.3 Versioning

All analyses must be associated with a specific document version. Stale results shall not be published for newer document versions.

## 13.4 Cancellation

If analysis for version `n` is in flight and version `n+1` arrives, older analysis should be cancelled or discarded unless it can still provide reusable intermediate results.

---

## 14. Diagnostics

## 14.1 Categories

Diagnostics should be divided into:

1. lexical diagnostics,
2. syntax diagnostics,
3. binding diagnostics,
4. type diagnostics,
5. language-specific semantic diagnostics,
6. optional style hints.

## 14.2 Required Diagnostic Areas

The following language-specific diagnostics should be supported:

1. invalid declaration vs expression ambiguity,
2. illegal use of `_` as a value,
3. recursive self-reference without `rec`,
4. invalid tuple projection,
5. invalid spread outside collection expression,
6. unsupported input or output shape,
7. mutable assignment to immutable binding,
8. `break`/`continue` outside loop,
9. malformed `then` / `do` one-line body,
10. ambiguous or invalid space-separated call,
11. implicit return mismatch with declared function return type,
12. final bare invocation in a non-void function when no explicit return exists, if implemented as warning.

## 14.3 Publication Strategy

Diagnostics should be published after successful parse, even if binding fails.

Recommended pipeline:

1. publish syntax diagnostics quickly,
2. publish semantic diagnostics after deeper analysis,
3. replace prior diagnostics for the document version atomically.

## 14.4 Severity

Recommended severities:

- errors: semantic violations and syntax errors,
- warnings: suspicious but legal constructs,
- information: optional contest-style hints,
- hints: style and simplification suggestions.

---

## 15. Completion

## 15.1 Goals

Completion should favor speed, relevance, and low noise over exhaustive enumeration.

## 15.2 Completion Sources

The completion provider should combine:

1. lexical keyword completion,
2. local symbol completion,
3. intrinsic API completion,
4. in-scope function completion,
5. member completion for typed receivers,
6. .NET type and member completion where available,
7. context-sensitive syntax suggestions.

## 15.3 Contexts

The completion engine should recognize at least the following contexts:

1. statement start,
2. expression position,
3. after `.` member access,
4. inside type position,
5. after `for` / `in`,
6. after `->`,
7. inside collection expression,
8. after `stdin.` and `stdout.`,
9. after `Array.`,
10. after `if`, `then`, `else`, `return`, `do`.

## 15.4 Syntax-Aware Suggestions

Examples of context-sensitive suggestions:

- at statement start: `let`, `var`, `mut`, `if`, `for`, `while`, `return`, `break`, `continue`, `=`, `+=`
- after `stdin.`: `int`, `long`, `str`, `char`, `line`, `chars`, `words`, `array`, `gridInt`, `charGrid`
- after `stdout.`: `write`, `writeln`, `lines`, `grid`, `flush`
- after `Array.`: `zero`
- after a tuple-typed expression and `.`, numeric tuple projections may be suggested

## 15.5 Snippet Completions

Optional snippet completions:

- `if { } else { }`
- `for i in 0..<n { }`
- `xs -> x { }`
- `rec int name(...) { }`
- `stdin` helpers
- aggregation patterns such as `min { for ... do ... }`

---

## 16. Hover

## 16.1 Required Content

Hover should provide, when available:

1. symbol kind,
2. declared type or inferred type,
3. mutability,
4. declaration signature,
5. intrinsic documentation for built-ins,
6. tuple projection meaning,
7. shorthand expansion meaning for special syntax.

## 16.2 Examples

Hover on:

- `stdin.int` => intrinsic input helper documentation,
- `Array.zero` => initialization helper documentation,
- `p.2` => tuple projection, resulting type,
- `= expr` => write statement meaning,
- `+= expr` => writeline statement meaning,
- `int[n] arr =` => declaration-based input shorthand explanation,
- `int[n] arr;` => sized array allocation explanation.

## 16.3 Format

The server should return markdown-formatted hover text.

---

## 17. Go to Definition

## 17.1 Required Targets

The server should support go-to-definition for:

- user functions,
- local variables,
- parameters,
- loop variables,
- intrinsic symbols,
- local type names where applicable,
- .NET members if metadata source mapping is available.

## 17.2 Tuple Projection

Tuple projections such as `p.2` do not necessarily have source declarations. The server may:

1. navigate to the tuple-producing declaration, or
2. provide no definition and rely on hover.

## 17.3 Special Forms

For syntax sugar such as `= expr` or declaration-based input shorthand, go-to-definition on intrinsic portions should navigate to the intrinsic API documentation surface if the editor supports virtual definitions or markdown documents.

---

## 18. Find References

References should be supported for:

- user functions,
- variables,
- parameters,
- loop variables,
- symbols introduced by declaration-based input shorthand.

References should distinguish between:

1. read access,
2. write access,
3. declaration site.

This distinction is useful for rename validation and optional semantic token modifiers.

---

## 19. Rename

## 19.1 Supported Symbols

Rename should be supported for:

- local variables,
- parameters,
- functions declared in the document or workspace,
- loop variables,
- fast-iteration variables.

## 19.2 Unsupported Rename Targets

Rename should be rejected for:

- discard `_`,
- intrinsic symbols such as `stdin`, `stdout`,
- tuple projections `p.1`, `p.2`,
- keywords,
- generated temporaries.

## 19.3 Validation

Rename shall ensure the new name:

1. is a valid identifier,
2. is not a reserved keyword unless escaping is supported,
3. does not create an illegal binding conflict according to the implementation's rules.

---

## 20. Document Symbols

## 20.1 Required Symbol Kinds

The document symbol provider should expose at least:

- functions,
- top-level declarations,
- optionally local declarations nested under functions,
- optionally intrinsic declarations surfaced as pseudo-symbols.

## 20.2 Folding Relationship

Nested symbols should reflect lexical containment.

Example structure:

- function
  - parameter list
  - local declarations
  - nested blocks: optional

---

## 21. Semantic Tokens

## 21.1 Token Types

Recommended semantic token types:

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
- enumMember: optional,
- builtin: optional custom mapping via token modifiers.

## 21.2 Token Modifiers

Recommended modifiers:

- declaration,
- readonly,
- modification,
- intrinsic,
- defaultLibrary,
- static,
- mutable.

## 21.3 Language-Specific Highlighting Guidance

The server should distinguish:

- `let` bindings as immutable variables,
- `var` / `mut` bindings as mutable variables,
- intrinsic `stdin` / `stdout` / `Array.zero`,
- keywords `then`, `do`, `rec`,
- output shorthand `=` and `+=` when used as statement leaders,
- tuple projections `p.1`, `p.2` as property-like or operator-like tokens,
- discard `_` as a dedicated variable token with modifier or as keyword-like highlighting.

## 21.4 Full and Range Updates

The server should implement full semantic tokens first. Range-based updates are optional for v0.1.

---

## 22. Signature Help

## 22.1 Supported Call Forms

Signature help should support:

- parenthesized calls,
- space-separated calls,
- intrinsic APIs,
- .NET calls when signatures are known.

## 22.2 Space-Separated Calls

For `f a b c`, signature help should infer the active parameter from argument count and parser context.

## 22.3 Intrinsic APIs

Signature help shall be provided for intrinsic APIs such as:

- `stdin.array<T>(n)`
- `stdin.gridInt(n, m)`
- `stdout.join(sep, xs)`
- collection helper functions if exposed as callable symbols

---

## 23. Inlay Hints

## 23.1 Goals

Inlay hints should help readability without cluttering short contest code.

## 23.2 Recommended Hint Categories

1. inferred type hints for `let` and `var`,
2. parameter name hints for intrinsic helpers,
3. tuple projection hints,
4. range endpoint or exclusivity hints: optional,
5. implicit return hints: optional,
6. mutable binding hint: optional style aid.

## 23.3 Suggested Defaults

Enabled by default:

- inferred types for nontrivial `let` / `var`,
- parameter labels for multi-argument intrinsic APIs.

Disabled by default or optional:

- hints on every small literal-heavy call,
- redundant hints in obvious local code.

---

## 24. Code Actions

## 24.1 Required Quick Fixes

Recommended quick fixes for v0.1:

1. add missing `rec` to recursive function,
2. insert braces for malformed one-line construct,
3. replace final bare invocation with explicit `return`,
4. replace value-ignored expression with `_ = expr`,
5. convert `int[n] arr;` to `int[] arr = Array.zero(n)` if the user prefers explicit form,
6. convert explicit `stdin` helper call to declaration shorthand or vice versa,
7. add parentheses to disambiguate space-separated call,
8. convert immutable declaration to mutable when assignment error is detected.

## 24.2 Refactorings

Optional future refactorings:

- convert parenthesized call to space-separated call,
- convert space-separated call to parenthesized call,
- convert one-line `if then else` to block form,
- convert block form to one-line form when safe,
- inline trivial variable.

---

## 25. Formatting

Formatting is optional in v0.1. If implemented, it should follow these rules.

## 25.1 Formatting Principles

1. braces always stay explicit,
2. one-line `then` / `do` forms remain one-line only if short,
3. collection expressions preserve compactness where possible,
4. space-separated calls should not be aggressively rewritten unless requested,
5. declaration-based input and output shorthand should remain visually distinct.

## 25.2 Minimal Formatter Requirements

If a formatter is implemented, it should at least:

- normalize indentation,
- insert line breaks around braces,
- normalize spaces around binary operators,
- preserve comments,
- preserve one-line constructs when syntactically valid and short.

---

## 26. Folding Ranges

Optional for v0.1.

Recommended foldable regions:

- function bodies,
- block statements,
- multiline collection expressions,
- multiline `if` branches,
- multiline comments.

---

## 27. Workspace Indexing

## 27.1 Index Scope

The workspace index should track:

- function declarations,
- top-level declarations,
- optionally file-level symbols,
- intrinsic symbol docs,
- optionally external .NET symbol metadata caches.

## 27.2 Single-File Priority

Because the target domain is competitive programming, the implementation should prioritize excellent single-file behavior even when full workspace indexing is incomplete.

## 27.3 Background Indexing

Workspace indexing should be background and cancellable. Open-file analysis should always take precedence.

---

## 28. Interop with .NET Symbols

## 28.1 General Rule

The language server should preserve and expose ordinary .NET API names as written by the user.

Examples:

- `Queue<int>`
- `HashSet<int>`
- `PriorityQueue<int, int>`
- `Math.Max`
- `Array.Sort`

## 28.2 Symbol Exposure Strategy

The implementation may expose .NET symbols via:

1. metadata reflection,
2. Roslyn integration,
3. precomputed symbol models,
4. simplified stubs.

v0.1 may choose a limited approach focused on commonly used namespaces and intrinsic helper integration.

## 28.3 Completion and Hover for .NET Types

If .NET symbol integration is partial, the server should still provide correct syntax and name classification even if full member documentation is unavailable.

---

## 29. Performance Strategy

## 29.1 Latency Targets

Recommended qualitative targets:

- lexical + parse diagnostics: near-immediate,
- local semantic features on open file: interactive,
- workspace references and rename: acceptable with indexing delay.

## 29.2 Caching

Cache at least:

- token stream,
- syntax tree,
- AST,
- semantic model,
- symbol tables,
- semantic token result,
- document symbol result.

## 29.3 Incrementality

v0.1 may reparse whole files after edits if necessary, provided performance is acceptable for contest-sized files. Incremental parsing is desirable but not required.

## 29.4 Request Prioritization

Foreground requests should be prioritized in this order:

1. completion,
2. hover,
3. diagnostics for current open document,
4. definition,
5. references,
6. background workspace indexing.

---

## 30. Testing Strategy

## 30.1 Parser and Recovery Tests

Required tests:

- incomplete `if then else`,
- incomplete `for` / `while`,
- malformed `->`,
- malformed `[]` with spread/range,
- malformed input and output shorthand,
- malformed tuple assignment,
- ambiguous space-separated calls.

## 30.2 Semantic Feature Tests

Required tests:

- recursive function without `rec`,
- tuple projection type checking,
- immutable assignment diagnostic,
- implicit return behavior,
- final bare invocation warning behavior if implemented,
- intrinsic symbol hover/completion,
- declaration-based input shorthand navigation.

## 30.3 LSP Integration Tests

Required integration tests:

- open/change/close document lifecycle,
- diagnostics update by version,
- cancellation of stale requests,
- completion inside incomplete code,
- semantic tokens on partially broken file,
- rename on local variables,
- go-to-definition on intrinsic and user symbols.

---

## 31. Suggested Development Order

Recommended implementation order for the language server:

1. document store and snapshot model,
2. lexer + parser integration,
3. syntax diagnostics publication,
4. symbol binding integration,
5. hover,
6. completion,
7. go-to-definition,
8. document symbols,
9. semantic tokens,
10. references,
11. rename,
12. code actions,
13. inlay hints,
14. formatting and optional advanced features.

This order maximizes usefulness early while relying on progressively deeper semantic infrastructure.

---

## 32. Minimal Viable LSP Feature Set

A minimal viable v0.1 language server shall implement:

1. open/change/close synchronization,
2. syntax diagnostics,
3. semantic diagnostics for core language rules,
4. completion for keywords, locals, intrinsics,
5. hover for locals, functions, intrinsics,
6. go-to-definition for user symbols,
7. document symbols,
8. semantic tokens.

A strong v0.1 implementation should additionally implement:

9. references,
10. signature help,
11. inlay hints,
12. rename,
13. quick-fix code actions.

---

## 33. Example Semantic Cases

### 33.1 Input Shorthand

For:

```txt
int n =
int[n] arr =
```

The language server should:

- classify both as declarations,
- provide hover indicating shorthand semantics,
- resolve `n` and `arr` as ordinary symbols,
- navigate `stdin`-equivalent meaning through intrinsic docs if requested.

### 33.2 Output Shorthand

For:

```txt
= ans
+= arr
```

The language server should:

- parse these as output statements only at statement start,
- classify them distinctly in semantic tokens if supported,
- provide hover explaining `write` and `writeln` semantics.

### 33.3 Tuple Projection

For:

```txt
(int, int, int) p = (1, 2, 3)
= p.2
```

The language server should:

- infer that `p.2` has type `int`,
- provide hover showing tuple projection meaning,
- optionally suggest `.1`, `.2`, `.3` completions after `p.`.

### 33.4 Recursive Function

For:

```txt
int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

The language server should emit a diagnostic that `rec` is required.

### 33.5 Bare Final Invocation

For:

```txt
bool add(HashSet<int> set, int x) {
    set.Add x
}
```

The language server should:

- understand that final bare invocation is not an implicit return,
- report an error or warning depending on function return typing policy,
- suggest `return set.Add x` or `let ret = set.Add x; ret` if appropriate.

---

## 34. Conformance

A language server conforms to this specification if it:

1. correctly tracks documents and versions,
2. exposes LSP capabilities consistent with implemented features,
3. provides syntax and semantic feedback consistent with the language specification,
4. recognizes and documents language intrinsics,
5. preserves correct behavior for special syntax including declaration-based input, output shorthand, tuple projection, range expressions, collection expressions, fast iteration, one-line constructs, and implicit return rules,
6. degrades gracefully under incomplete code.

Implementation details such as exact caching strategy, metadata source for .NET interop, or helper runtime visualization are otherwise implementation-defined.

---

## 35. Versioning

This document defines language server draft version `v0.1`.

Backward compatibility is not guaranteed between draft revisions.

