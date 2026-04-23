# PSCP v0.6 언어 서버 및 VS Code 확장 통합 구현 가이드

## 0. 문서 목적

이 문서는 PSCP `v0.6`의 개발 도구 계층, 즉 **언어 서버(Language Server)** 와 **VS Code 확장(VS Code Extension)** 을 함께 다루는 통합 구현 가이드다.

이 문서는 다음을 설명한다.

1. 언어 서버의 역할과 내부 구조
2. VS Code 확장의 역할과 내부 구조
3. 둘 사이의 책임 분리
4. LSP(JSON-RPC) 통신 모델
5. 문서/워크스페이스/스냅샷 모델
6. 파서/바인더/시맨틱 서비스 구성
7. 진단, 자동완성, hover, semantic tokens, rename, code action, inlay hints
8. PSCP 특유의 shorthand / intrinsic / lowering-aware UX를 에디터에서 어떻게 보여줄지
9. 성능 목표, concurrency, cancellation, cache, freshness policy
10. VS Code 전용 UX 설계
11. 개발, 테스트, 디버깅, 배포 전략

이 문서는 단순한 기능 목록이 아니다.  
즉 “어떤 기능을 지원한다” 정도로 끝나지 않고,

- 각 기능이 어떤 semantic 데이터를 필요로 하는지
- 어떤 단계까지 분석해야 하는지
- 어느 정보를 language server가 책임지고, 어느 부분을 VS Code 확장이 책임지는지
- 무엇을 최신 snapshot 기준으로만 보여줘야 하는지
- 어떤 PSCP 문법 설탕을 editor UX로 친절하게 드러내야 하는지

까지 가능한 한 구체적으로 정의한다.

---

## 1. 큰 그림

PSCP 개발 도구는 두 층으로 나뉜다.

1. **Language Server**
   - 언어를 이해한다.
   - 문서를 파싱하고 바인딩하고 시맨틱 분석한다.
   - 진단, 자동완성, hover, semantic tokens, signature help, rename 등의 정보를 계산한다.

2. **VS Code Extension**
   - 에디터와 language server를 연결한다.
   - 명령(command), 상태 표시, 로그, 설정, 작업 실행, 미리보기, 사용자 UX를 제공한다.
   - 언어 서버가 계산한 결과를 VS Code에서 보기 좋게 전달/보조한다.

핵심 원칙은 다음과 같다.

> 언어 의미를 해석하는 책임은 language server가 가진다.  
> VS Code 확장은 그 결과를 IDE UX로 전달하고 보조하는 책임을 가진다.

즉 확장 쪽에서 PSCP 문법을 다시 파싱하거나, shorthand를 독자적으로 해석하려고 하면 안 된다.

---

## 2. 왜 둘을 통합해서 설계해야 하는가

PSCP는 일반 언어보다 문법 설탕과 lowering-aware UX의 비중이 크다.

예를 들면:

- `int n =` 는 단순 선언이 아니라 declaration-based input shorthand다.
- `= expr` 는 statement-based output shorthand다.
- `sum (0..<n -> i do f(i))` 는 generator + aggregate 결합 패턴이다.
- `new![n]` 는 per-element auto-construction 의미를 가진다.
- `visited += x` 는 `HashSet<T>.Add(x)` 로 rewrite되며 bool을 반환한다.
- `let MOD = 1000000007` 는 transpiler가 `const`로 내릴 가능성이 있다.

이런 것들은 editor에서 잘 보여주면 사용자 경험이 급격히 좋아진다.  
그러려면 language server와 VS Code extension을 따로따로 생각하면 안 되고, **한쪽이 어떤 semantic fact를 계산하고 다른 쪽이 그걸 어떻게 UX로 보여줄지**를 처음부터 같이 설계해야 한다.

---

## 3. 전체 아키텍처

권장 아키텍처는 다음과 같다.

```txt
VS Code UI
  └─ VS Code Extension (TypeScript)
       ├─ LSP client
       ├─ commands / tasks / status / UX glue
       ├─ optional preview & logs
       └─ settings bridge
            ↓ JSON-RPC / LSP
       Language Server (C# recommended)
       ├─ protocol layer
       ├─ document store
       ├─ workspace manager
       ├─ analysis scheduler
       ├─ lexer / parser / recovery
       ├─ binder / type & shape analyzer
       ├─ intrinsic classifier
       ├─ lowering-aware semantic facts
       ├─ feature providers
       └─ diagnostics publisher
```

### 권장 언어 선택

- **Language Server**: C# 권장
  - PSCP 자체가 C#/.NET을 타겟으로 하므로 metadata, type model, future transpiler integration 측면에서 유리하다.
- **VS Code Extension**: TypeScript 권장
  - VS Code 생태계 표준

---

## 4. 책임 분리

## 4.1 Language Server가 책임질 것

- 문서 파싱
- syntax recovery
- binding
- 타입 / shape 분석
- intrinsic / pass-through 구분
- name resolution 및 shadowing 판단
- shorthand 의미 해석
- known DS rewrite 인식
- aggregate/math/helper/call form 해석
- diagnostics
- completion
- hover
- semantic tokens
- definition / references / rename
- code action 제안 근거
- inlay hints 근거
- lowering-aware semantic facts 생성

## 4.2 VS Code Extension이 책임질 것

- language client 시작/종료
- 서버 실행 파일 찾기 / spawning
- 설정값 전달
- 상태 표시 (status bar)
- command 등록
- log channel 제공
- transpile/build/run 명령 연결
- 테스트/예제 파일 열기 command
- generated C# preview 같은 부가 기능
- diagnostic decorations / virtual documents / tree view 같은 VS Code 전용 UI

## 4.3 금지되는 경향

VS Code extension이 다음을 직접 해석하면 안 된다.

- `int n =` 의 shorthand 의미
- `sum arr` 의 aggregate family 의미
- `visited += x` 의 HashSet rewrite 의미
- `let MOD = 1000000007` 의 const eligibility

이런 건 모두 language server가 계산하고, extension은 보여주기만 해야 한다.

---

## 5. Language Server 프로세스 구조

## 5.1 기본 실행 형태

권장 형태:

- 별도 프로세스
- stdio transport
- JSON-RPC 2.0
- LSP 표준 메시지

## 5.2 왜 in-process가 아닌가

PSCP는 향후 transpiler / formatter / preview / metadata integration과 연결될 가능성이 높다.  
별도 프로세스로 두는 편이

- crash isolation
- memory isolation
- debugging
- versioning
- editor-independent reuse

측면에서 낫다.

---

## 6. 문서 모델

## 6.1 snapshot 기반 모델

모든 분석은 immutable document snapshot을 기준으로 수행해야 한다.

권장 구조:

```txt
DocumentId
  uri: string

DocumentSnapshot
  id: DocumentId
  version: int
  text: string
  lineIndex: LineIndex
  timestamp: long
```

## 6.2 line index

각 snapshot은 UTF-16 기준 LSP position 변환을 위한 line index를 가진다.

필수 용도:

- diagnostics span conversion
- semantic token range encoding
- hover span mapping
- code action edit mapping
- rename result mapping

## 6.3 snapshot immutability

한 번 생성된 snapshot은 절대 수정하지 않는다.  
새 편집은 새 snapshot을 만든다.

---

## 7. workspace 모델

## 7.1 workspace manager responsibilities

- open document registry
- closed document metadata cache
- workspace folder 목록
- per-folder settings overlay
- intrinsic metadata cache
- external metadata cache
- background index state
- symbol search index

## 7.2 single-file 우선 원칙

PSCP는 경쟁 프로그래밍 중심 언어이므로 single-file responsiveness를 최우선으로 둔다.

즉 다음이 항상 우선이다.

- 현재 열려 있는 파일의 completion
- 현재 커서 위치 hover
- 현재 버전 diagnostics

workspace indexing은 이보다 뒤다.

---

## 8. 분석 파이프라인

권장 파이프라인:

1. lexing
2. parsing
3. syntax recovery
4. AST construction
5. binding
6. type / shape analysis
7. intrinsic classification
8. lowering-aware fact extraction
9. feature-specific query layer

## 8.1 syntax 단계

syntax 단계에서 충분한 기능:

- lexical diagnostics
- syntax diagnostics
- bracket matching
- folding ranges
- basic outline
- fallback highlight

## 8.2 semantic 단계

semantic 단계가 필요한 기능:

- completion (정확한 후보 정렬)
- hover
- definition / references
- rename
- semantic tokens
- inlay hints
- code actions
- intrinsic overload / helper 문서
- shorthand expansion 설명
- lowering-aware hints

## 8.3 lowering-aware semantic facts

PSCP는 editor에서 “이 코드가 결국 어떻게 내려갈까”를 보여주는 것이 중요하다.  
따라서 language server는 일반 semantic model 외에 일부 **lowering-aware facts** 를 함께 계산하는 것이 강하게 권장된다.

예:

- `let MOD = 1000000007` → const eligible
- static immutable field → readonly eligible
- `sum (0..<n -> i do f(i))` → direct-loop lowering candidate
- `new![n]` → allocate + init loop candidate
- `visited += x` → `HashSet<T>.Add(x)` rewrite, return type `bool`
- `dict += (k, v)` → semantic-stage rewrite candidate
- `x = y` in value position → non-canonical assignment warning candidate

이 정보는 hover, code action, inlay hint, diagnostics에 매우 유용하다.

---

## 9. syntax recovery

PSCP는 작성 중인 코드를 다루므로 recovery가 매우 중요하다.

## 9.1 recovery가 반드시 필요한 영역

- incomplete declaration shorthand (`int n =` 입력 중)
- incomplete output shorthand (`=`만 입력한 상태)
- malformed `[]` collection
- malformed generator `()`
- malformed `->`
- malformed `:=`
- malformed tuple destructuring
- malformed local function declaration
- malformed `ref/out/in` argument
- malformed generic declaration/use
- malformed `operator<=>(other)`
- removed syntax (`public:` 등)

## 9.2 parser recovery metadata

파서는 recovery 노드에 최소한 다음을 남겨야 한다.

- `isMissing`
- `isRecovered`
- `hasSkippedTokens`

semantic 단계는 이 정보를 보고 보수적으로 동작해야 한다.

## 9.3 cascading diagnostics suppression

하나의 누락 토큰 때문에 수십 개의 엉뚱한 오류가 연쇄적으로 발생하지 않도록 suppression 정책이 필요하다.

---

## 10. concurrency 모델

이 절은 언어 서버 설계에서 매우 중요하다.

## 10.1 foreground vs background

두 종류 작업을 구분한다.

### foreground

- active document diagnostics
- completion
- hover
- semantic tokens for visible file
- signature help
- go-to-definition

### background

- workspace indexing
- external metadata loading
- symbol search index warmup
- closed file semantic precompute

foreground가 항상 우선한다.

## 10.2 cancellation policy

새 snapshot version이 오면:

- 이전 syntax pass 취소 시도
- 이전 semantic pass 취소 시도
- 취소 불가능한 작업은 완료되더라도 결과 publish 금지

## 10.3 freshness guarantee

다음은 보장되어야 한다.

- diagnostics는 최신 snapshot version에 대해서만 publish
- semantic tokens는 최신 snapshot version 기준
- hover/completion은 요청 시점과 호환되는 최신 snapshot 기준
- 오래된 결과가 최신 결과를 덮어쓰면 안 된다

## 10.4 shared model deduplication

같은 snapshot에 대해 동시에 여러 기능이 semantic model을 요구하면, 가능하면 build 결과를 공유한다.

예:

- completion 요청과 hover 요청이 거의 동시에 들어오면 같은 semantic future 공유

## 10.5 thread safety

- snapshot은 immutable
- semantic model은 immutable 또는 snapshot-bound readonly
- caches는 synchronization-safe
- background indexing이 foreground symbol table을 오염시키지 않음

## 10.6 starvation 방지

background 작업이 foreground latency를 망치면 안 된다.

권장:

- foreground queue / background queue 분리
- foreground high-priority worker
- background cooperative cancellation

---

## 11. 성능 목표

참고용 reference target:

## 11.1 warm cache 기준

- completion p95 < 30ms
- hover p95 < 30ms
- syntax diagnostics first pass p95 < 40ms
- semantic diagnostics second pass p95 < 120ms
- semantic tokens full document p95 < 80ms

## 11.2 cold-ish cache 기준

- completion p95 < 80ms
- hover p95 < 70ms

## 11.3 typical file 가정

- 200 ~ 2000 lines
- single contest file
- 여러 helper / local function / type decl 포함 가능

## 11.4 degraded mode

외부 metadata가 아직 덜 준비되었거나 파일이 비정상적으로 크면

- syntax-only fallback completion
- reduced hover
- delayed enrichment

을 허용하되 foreground responsiveness를 유지한다.

---

## 12. cache 설계

## 12.1 cache layers

권장 cache 계층:

1. token cache
2. syntax tree cache
3. AST cache
4. semantic model cache
5. intrinsic metadata cache
6. external symbol metadata cache
7. completion context cache (optional)
8. semantic token cache
9. document symbol cache

## 12.2 cache key

문서 분석 캐시는 최소한 다음을 key로 가진다.

- URI
- snapshot version

## 12.3 invalidation

새 snapshot이 도착하면:

- 이전 version syntax/semantic result stale 처리
- 재사용 가능한 부분만 구조적으로 재활용
- stale semantic token result publish 금지

## 12.4 intrinsic metadata cache

intrinsic metadata는 process lifetime 동안 재사용 가능하다.

예:

- `stdin` members
- `stdout` members
- aggregate family docs
- collection helper docs
- math family docs
- conversion keyword docs
- known DS rewrite docs

---

## 13. semantic model이 반드시 알아야 할 PSCP 고유 요소

language server는 최소한 다음을 semantic level에서 구분해야 한다.

- declaration-based input shorthand
- statement-based output shorthand
- `[]` materialized collection
- `()` generator
- spread
- range / stepped range
- fast iteration `->`
- `let`, `var`, `mut`, `rec`
- `=` vs `:=`
- local function / nested local function / rec group
- aggregate family
- collection helper family
- math intrinsic family
- conversion keyword family
- comparator sugar (`T.asc`, `T.desc`)
- `operator<=>(other)`
- `new[n]`, `new![n]`
- known DS rewrites
- tuple projection
- slicing / index-from-end
- name shadowing of intrinsic symbols

---

## 14. symbol model

## 14.1 symbol kinds

semantic layer는 최소한 다음 symbol kind를 구분해야 한다.

- local variable
- parameter
- function
- local function
- type
- field
- property
- method
- constructor
- namespace
- intrinsic object
- intrinsic family symbol
- helper family symbol
- loop variable
- tuple element pseudo-symbol
- external .NET symbol
- discard pseudo-symbol
- ordering pseudo-symbol

## 14.2 symbol metadata

권장 필드:

```txt
symbolId
name
kind
containerSymbolId?
declarationSpan
typeInfo?
isMutable
isIntrinsic
isHelperFamily
isExternalDotNet
isConstEligible
isReadonlyEligible
```

---

## 15. diagnostics

## 15.1 categories

1. lexical diagnostics
2. syntax diagnostics
3. binding diagnostics
4. type diagnostics
5. intrinsic semantic diagnostics
6. lowering-aware hints/warnings

## 15.2 필수 diagnostics

최소한 다음은 잡아야 한다.

- immutable assignment
- `_`를 값으로 사용
- non-`rec` self recursion
- invalid mutual recursion grouping
- malformed shorthand input/output
- invalid `:=` target
- final ordinary assignment in implicit-return context
- invalid `ref/out/in`
- invalid tuple projection
- invalid spread outside `[]`
- invalid `new![n]` element type
- invalid known DS rewrite target
- invalid `sortWith` comparator type
- invalid collection helper receiver/use
- removed feature usage (`public:` 등)
- suspicious intrinsic shadowing only if user requests linting (optional)
- non-canonical `=` in value position

## 15.3 lowering-aware diagnostics

특히 다음은 PSCP UX에 중요하다.

- `let MOD = 1000000007` → info: const eligible
- static immutable field → info: readonly eligible
- `x = y` in value position → warning: use `:=` for canonical value-yielding assignment
- `new int[n > 0 ? n : 0]` 같은 generated-pattern equivalent source → internal backend lint only
- generator-fed aggregate direct loop candidate → info (optional)

## 15.4 publish 전략

1. syntax diagnostics 빠르게 publish
2. 이후 semantic diagnostics publish

각 publish는 해당 version 전체를 교체한다.

---

## 16. completion

## 16.1 목표

completion은 PSCP 특유의 shorthand와 helper family를 반영해야 한다.

단순 identifier 나열이 아니라,

- intrinsic object
- aggregate family
- collection helper
- math family
- conversion keyword
- known DS rewrite affordance
- pass-through .NET members

를 적절히 섞어야 한다.

## 16.2 주요 completion contexts

### statement start

- `let`, `var`, `mut`, `if`, `for`, `while`, `return`, `break`, `continue`
- `=` / `+=`
- `class`, `struct`, `record`

### expression position

- locals
- parameters
- local functions
- aggregate family
- math family
- type names
- relevant external symbols

### after `stdin.`

- canonical `read*` members만 제안

### after `stdout.`

- `write`, `writeln`, `flush`, `lines`, `grid`, `join`

### after collection receiver

- `map`, `filter`, `fold`, `scan`, `mapFold`, `any`, `all`, `count`, `find`, `findIndex`, `findLastIndex`, `sort`, `sortBy`, `sortWith`, `distinct`, `reverse`, `copy`, `groupCount`, `index`, `freq`

### after type name in comparer context

- `asc`, `desc`

### inside type body

- ordinary modifiers
- `operator<=>(other)` snippet

## 16.3 completion item metadata

가능하면 다음 포함:

- label
- insert text
- detail
- documentation
- snippet 여부
- intrinsic/helper/pass-through 분류

---

## 17. hover

## 17.1 목표

hover는 단순 타입 표기가 아니라 **PSCP source 의미 + lowering 의미** 를 보여주는 데 강해야 한다.

## 17.2 중요한 hover 예시

### shorthand input

`int n =`

- declaration-based input shorthand
- semantic meaning: `int n = stdin.readInt()`

### shorthand output

`= ans`

- write shorthand
- semantic meaning: `stdout.write(ans)`

### `[]`

- materialized collection
- allocation intent

### `()` generator

- lazy iterable
- immediate aggregate candidate 여부 표시 가능

### `:=`

- canonical value-yielding assignment
- likely lowers to C# assignment expression

### `x = y` in value context

- compatibility assignment expression
- not canonical
- not implicit-return eligible

### `visited += x`

- known DS rewrite: `HashSet<T>.Add(x)`
- return type `bool`

### `dict += (k, v)`

- semantic-stage rewrite candidate
- `Dictionary<K,V>.TryAdd(k, v)`
- return type `bool`

### `let MOD = 1000000007`

- immutable inferred local
- const eligible

### `new![n]`

- allocate + per-slot auto-construction

### `operator<=>(other)`

- default ordering shorthand
- should integrate with .NET ordered APIs

---

## 18. signature help

지원 대상:

- ordinary calls
- space-calls
- local function calls
- aggregate family
- collection helpers
- math family
- constructors
- pass-through external APIs when metadata available

특히 다음은 친절해야 한다.

- `sortWith(cmp)` → `cmp` is `Comparison<T>` or `IComparer<T>`
- `fold(seed, f)` → `f: (state, item) -> state`
- `mapFold(seed, f)` → `f: (state, item) -> (mapped, nextState)`
- `stdout.join(separator, xs)` → char/string overload

---

## 19. semantic tokens

semantic tokens는 PSCP의 성격을 잘 드러내야 한다.

구분 권장 대상:

- `let` 바인딩 (readonly 느낌)
- `var`, `mut` (mutable)
- `stdin`, `stdout`
- aggregate family
- collection helper family
- math family
- conversion keyword
- `:=`
- `new![n]`
- comparator sugar
- known DS rewrite-capable operator site
- local function symbols
- removed syntax usage

---

## 20. definition / references / rename

## 20.1 go-to-definition

지원 대상:

- locals
- parameters
- functions / local functions
- types
- fields / methods in source
- intrinsic doc surfaces
- external symbols if metadata available

## 20.2 references

- locals
- parameters
- functions / local functions
- source types / members
- loop variables

## 20.3 rename

지원 대상:

- variables
- parameters
- functions
- local functions
- source types / members
- loop vars

rename 금지 또는 제한 대상:

- discard `_`
- intrinsic object names
- tuple projection `.1`, `.2`
- removed feature tokens

---

## 21. inlay hints

추천 hint 종류:

- inferred type for `let` / `var`
- const eligible / readonly eligible
- `visited += x` returns `bool`
- generator vs materialized
- aggregate overload hints
- `sortWith` comparator type hints
- `mapFold` tuple-return hint
- `new![n]` per-slot init hint

---

## 22. code actions

권장 quick fixes:

- add missing `rec`
- convert `=` to `:=` in value context
- rewrite explicit input to shorthand / shorthand to explicit input
- rewrite `Math.Min/Max/Abs/Sqrt` to PSCP family where appropriate
- rewrite `visited.Add(x)` to `visited += x`
- rewrite explicit init loop to `new![n]`
- suggest `const`
- suggest `readonly`
- replace removed access section label syntax
- disambiguate malformed call with parentheses

---

## 23. VS Code 확장 구조

## 23.1 extension responsibilities

VS Code extension은 다음을 책임진다.

- language client 생성
- server executable location 결정
- client/server lifecycle
- settings bridge
- commands 등록
- status bar item
- output / log channel
- task integration
- optional transpile/build/run/test convenience UI
- generated C# preview / virtual document / side panel 같은 부가 UX

## 23.2 activation events

권장 activation:

- `onLanguage:pscp`
- `workspaceContains:**/*.pscp` (선택)
- relevant commands

## 23.3 settings 예시

```txt
pscp.server.path
pscp.server.args
pscp.server.trace
pscp.transpiler.path
pscp.transpiler.args
pscp.preview.generatedCSharp
pscp.diagnostics.loweringHints
pscp.experimental.enableGeneratedPreview
```

VS Code extension은 설정을 language server 초기화 옵션 또는 workspace/didChangeConfiguration로 전달한다.

---

## 24. VS Code에서 제공할 command

권장 command:

- `pscp.restartLanguageServer`
- `pscp.showServerLog`
- `pscp.transpileCurrentFile`
- `pscp.previewGeneratedCSharp`
- `pscp.runCurrentFile`
- `pscp.openExampleFile`
- `pscp.copyGeneratedCSharp`
- `pscp.showSemanticDebugInfo`

이 중 언어 의미를 재계산하는 것은 server에 요청하고, 확장은 command wiring만 담당하는 편이 좋다.

---

## 25. VS Code 전용 UX

## 25.1 status bar

다음 정도가 유용하다.

- server status (ready / indexing / error)
- current file semantic phase (optional debug mode)

## 25.2 log channels

최소 두 개 권장:

- Language Server Log
- PSCP Tools / Transpiler Log

## 25.3 generated C# preview

이 기능은 VS Code 확장에서 제공하기 좋다.

가능한 방식:

- command 실행 시 virtual document 생성
- side-by-side preview
- current snapshot transpile request -> virtual text

중요:

- preview 생성 논리는 extension이 아니라 server 또는 transpiler service에 위임
- 확장은 UI만 담당

## 25.4 problem matcher / tasks

transpiler/build/run을 VS Code task와 연결하면 편하다.

예:

- transpile current file
- build generated C# project
- run current solution

---

## 26. language server와 VS Code extension 사이의 custom protocol

기본은 LSP를 사용하지만, PSCP 특유 기능은 custom request/notification을 추가할 수 있다.

예:

- `pscp/previewGenerated`
- `pscp/getLoweringFacts`
- `pscp/getSemanticDebugTree`
- `pscp/runTranspiler`

원칙:

- editor-independent 핵심 의미는 가능하면 표준 LSP에 담는다
- PSCP 특수 도구 기능은 custom request로 분리한다

---

## 27. 테스트 전략

## 27.1 language server 테스트

- parser/recovery tests
- semantic/binding tests
- intrinsic shadowing tests
- shorthand input/output tests
- `=` vs `:=` tests
- local function / rec group tests
- collection helper type/family tests
- known DS rewrite recognition tests
- diagnostics freshness tests
- completion/hover/signature tests

## 27.2 VS Code extension 테스트

- extension activation
- server launch / reconnect
- config propagation
- command execution
- virtual document preview
- log channel behavior
- task integration smoke tests

## 27.3 end-to-end tests

- open `.pscp` file
- edit shorthand input
- confirm diagnostics update
- hover on intrinsic/helper
- run preview generated C# command
- rename local function / symbol

---

## 28. 디버깅 전략

### language server

- separate console app debugging
- JSON-RPC trace logs
- semantic debug dump endpoint

### VS Code extension

- Extension Development Host
- output channel + trace logs
- request/response timing logs

### combined debugging

- extension host에서 server spawn
- server attach debugging
- snapshot version / request id logging

---

## 29. 배포 전략

## 29.1 versioning

권장:

- extension version
- server version
- protocol capability version

을 분리해서 관리한다.

## 29.2 bundling

가능한 전략:

1. extension이 server binary를 함께 배포
2. extension이 server 다운로드를 관리
3. developer mode에서는 local server path 사용

## 29.3 compatibility

extension과 server는 capability negotiation 또는 최소 version check를 지원하는 것이 좋다.

---

## 30. 구현 우선순위

## 30.1 language server 우선순위

1. document model + snapshot versioning
2. lexer/parser + recovery
3. syntax diagnostics
4. binder / semantic model
5. intrinsic classification
6. hover
7. completion
8. definition
9. semantic tokens
10. signature help
11. rename/references
12. code actions
13. inlay hints
14. custom preview/debug requests

## 30.2 VS Code extension 우선순위

1. basic language client wiring
2. config + logging
3. restart command
4. generated preview command
5. transpile/run convenience commands
6. status bar and debug utilities

---

## 31. 최소 viable toolchain

처음 shipping 가능한 최소 조합:

### language server

- syntax diagnostics
- semantic diagnostics 핵심 일부
- completion
- hover
- semantic tokens
- definition

### VS Code extension

- language client bootstrap
- logs
- restart command
- preview generated C# command (가능하면)

이 조합만 있어도 PSCP 사용자 경험은 꽤 좋아진다.

---

## 32. PSCP다운 UX란 무엇인가

좋은 PSCP 도구는 단순히 오류 밑줄만 잘 긋는 도구가 아니다.

다음 같은 감각을 사용자에게 줘야 한다.

- `int n =` 에 올리면 “input shorthand” 라는 걸 즉시 알 수 있다.
- `visited += x` 에 올리면 “HashSet.Add returning bool” 이 보인다.
- `sum (0..<n -> i do score(i))` 를 보면 “direct loop로 내려갈 수 있겠다”는 감이 든다.
- `let MOD = 1000000007` 를 보면 const로 내려갈 수 있다는 걸 안다.
- `parent[x] = find(parent[x])` 는 경고를 통해 `:=` 를 떠올리게 된다.

즉 language server와 VS Code 확장은 **PSCP의 문법설탕을 사용자가 자연스럽게 배우는 도구**가 되어야 한다.

---

## 33. 맺음말

PSCP `v0.6`의 language server와 VS Code extension은 단순한 편의 기능이 아니다.  
이 둘은 PSCP가 가진 shorthand, intrinsic family, lowering-aware 철학을 사용자가 실제로 체감하게 만드는 핵심 계층이다.

좋은 구현은 다음을 만족해야 한다.

- semantic binding을 정확히 한다
- intrinsic과 user symbol shadowing을 올바르게 처리한다
- shorthand의 진짜 의미를 UX로 잘 보여준다
- generated code 감각을 editor 안에서 자연스럽게 암시한다
- foreground responsiveness를 지킨다
- stale 결과를 절대 최신 문서 위에 덮어쓰지 않는다

이 문서는 그 구현을 위한 통합 가이드다.

