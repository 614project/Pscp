# PSCP v0.5 언어 서버 명세

## 0. 문서 목적

이 문서는 PSCP `v0.5` 언어 서버(Language Server) 구현을 위한 상세 명세다.

이 문서는 다음을 정의한다.

1. 언어 서버의 전체 구조
2. 문서/워크스페이스 모델
3. syntax / semantic 분석 단계
4. concurrency 및 cancellation 정책
5. 성능 목표
6. diagnostics 정책
7. completion / hover / signature help / semantic tokens / rename / code action / inlay hint 등의 기능 요구사항
8. intrinsic / pass-through / lowered semantic을 editor tooling에서 어떻게 다뤄야 하는지
9. cache / freshness / consistency 정책
10. 구현 우선순위와 testing 방향

이 문서는 단순한 “기능 목록”이 아니다.  
즉 어떤 LSP method를 지원해야 하는지만 나열하지 않고,

- 어떤 순서로 분석해야 하는지
- 어떤 경우 이전 결과를 취소해야 하는지
- 최신 snapshot 기준으로 어떤 응답만 보여야 하는지
- 어떤 semantic 정보를 hover/completion에 보여줘야 하는지

까지 가능한 한 구체적으로 정의한다.

---

## 1. 범위와 철학

PSCP 언어 서버는 다음 철학을 따른다.

1. **사용자 편의 우선**
   - 문제 풀이 중 빠르게 피드백을 준다.
2. **불완전 코드에서도 유용해야 한다**
   - 작성 중인 소스가 깨져 있어도 completion/hover/diagnostics가 가능한 한 살아 있어야 한다.
3. **PSCP 고유 sugar를 깊게 이해해야 한다**
   - shorthand input/output, aggregate family, conversion keyword, generator, `:=`, `new![n]`, known DS rewrite 등을 단순 텍스트로 보지 않는다.
4. **C# pass-through surface도 존중해야 한다**
   - generic type, `using`, `namespace`, ordinary .NET member access, nullable marker `?`, `record struct` 등을 제대로 이해해야 한다.
5. **semantic freshness를 보장해야 한다**
   - 오래된 snapshot의 결과가 최신 편집 결과를 덮어쓰면 안 된다.
6. **foreground latency를 적극적으로 관리해야 한다**
   - open document의 completion/hover/diagnostics가 background indexing보다 항상 우선이다.

---

## 2. 비목표

다음은 `v0.5`에서 필수 목표가 아니다.

1. IDE 수준의 완전한 project system 구축
2. debugger integration
3. online judge integration
4. 모든 외부 .NET metadata를 완전하게 이해하는 것
5. full-fidelity source formatter를 필수 기능으로 제공하는 것
6. generated C# preview를 필수 기능으로 제공하는 것
7. 모든 refactoring을 지원하는 것

다만 구조를 잘 잡아두면 나중에 확장 가능한 방향으로 설계하는 것이 좋다.

---

## 3. 지원하는 언어 의미 범위

LSP는 최소한 다음을 semantic level에서 이해해야 한다.

### 3.1 PSCP 고유 surface

- `let`, `var`, `mut`, `rec`
- declaration-based input shorthand
- statement-based output shorthand
- `[]` materialized collection
- `()` generator expression
- range (`..`, `..<`, `..=` , stepped range)
- spread element
- fast iteration `->`
- aggregate family (`min`, `max`, `sum`, `sumBy`, `minBy`, `maxBy`, `chmin`, `chmax`)
- math intrinsic family (`abs`, `sqrt`, `clamp`, `gcd`, `lcm`, `floor`, `ceil`, `round`, `pow`, `popcount`, `bitLength`)
- conversion keyword calls (`int`, `long`, `double`, `decimal`, `bool`, `char`, `string` in expression position)
- `:=`
- `new[n]`, `new![n]`
- comparator sugar (`T.asc`, `T.desc`)
- known data structure operator rewrites
- `operator<=>(other)` shorthand
- tuple projection `.1`, `.2`, ...
- slicing / index-from-end

### 3.2 pass-through C#/.NET surface

- `namespace`, `using`
- `class`, `struct`, `record`, `record struct`
- `new`, `this`, `base`
- generic type / generic function declaration 및 사용
- nullable marker `?`
- `is`, `is not`
- inline switch-expression surface
- ordinary .NET type/member names
- ordinary access modifiers (`public`, `private`, `protected`, `internal`)

---

## 4. 프로토콜 및 언어 식별자

## 4.1 프로토콜

언어 서버는 JSON-RPC 2.0 기반 Language Server Protocol을 사용한다.

## 4.2 권장 transport

- 표준 입력 / 표준 출력

## 4.3 language id

권장 language id:

- `pscp`

---

## 5. 전반적 아키텍처

권장 아키텍처는 다음 계층을 가진다.

1. protocol layer
2. document store
3. workspace manager
4. analysis scheduler
5. parser / binder / semantic service
6. symbol & metadata service
7. feature providers
8. diagnostics & publish coordinator
9. optional background indexer

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
  openedAt: timestamp
```

## 6.2 line index

각 snapshot은 UTF-16 기준 LSP position 변환을 위한 line index를 가진다.

필수 용도:

- diagnostics span conversion
- semantic token range encoding
- hover span mapping
- code action text edit mapping
- rename result mapping

## 6.3 snapshot immutability

한 번 생성된 snapshot은 절대 수정하지 않는다.

새 편집은 새 snapshot을 만든다.

---

## 7. workspace 모델

## 7.1 workspace responsibilities

workspace manager는 다음을 관리한다.

- open document registry
- closed document metadata cache
- workspace folders
- project / file grouping metadata (있다면)
- intrinsic metadata cache
- external symbol metadata cache
- background index state

## 7.2 single-file 우선 원칙

PSCP는 경쟁 프로그래밍 중심 언어이므로, single-file responsiveness를 최우선으로 둔다.

workspace-wide 기능은 open-file foreground 분석을 방해하면 안 된다.

---

## 8. 분석 단계

## 8.1 단계 구분

각 snapshot에 대해 최소한 다음 분석 단계를 구분한다.

1. lexing
2. parsing
3. syntax recovery
4. AST construction
5. binding
6. type / shape analysis
7. intrinsic classification
8. lowered semantic classification (optional but strongly recommended)

## 8.2 syntax-only stage

syntax-only stage로 충분한 기능:

- lexical diagnostics
- syntax diagnostics
- folding ranges
- basic bracket matching
- basic document symbols
- fallback highlighting

## 8.3 semantic stage

semantic stage가 필요한 기능:

- completion (정확한 타입/shape 기반)
- hover
- definition / references
- rename
- semantic tokens
- inlay hints
- code actions
- intrinsic overload 설명
- generator/materialized distinction 설명
- known DS operator recognition
- const/readonly-related hinting

## 8.4 lowered semantic facts

`v0.5`에서는 단순 semantic model 외에, 일부 lowered semantic facts를 LSP가 알 수 있는 것이 강력히 권장된다.

예:

- `let a = 100` 이 `const` eligible인지
- static immutable binding이 `readonly` eligible인지
- `sum (0..<n -> i do score(i))` 가 direct loop lowering candidate인지
- `new![n]` 가 direct init loop candidate인지
- `HashSet +=` 가 `.Add` bool return rewrite인지

이 정보는 hover/code action/inlay hint에서 유용하다.

---

## 9. error recovery 정책

## 9.1 목표

언어 서버는 코드가 불완전해도 최대한 많은 기능을 계속 제공해야 한다.

## 9.2 recovery가 필요한 영역

다음은 반드시 recovery를 고려해야 한다.

1. incomplete declaration
2. incomplete input shorthand (`int n =` 작성 중)
3. incomplete output shorthand (`=` 또는 `+=` 작성 중)
4. malformed `[]` collection expression
5. malformed generator expression `(...)`
6. malformed spread / range syntax
7. malformed `if then else`
8. malformed `for`, `while`, `->`
9. malformed `:=`
10. malformed slicing / index-from-end
11. malformed tuple projection
12. malformed modified arguments
13. malformed generic declaration/use
14. malformed `operator<=>(other)`
15. malformed pass-through C# member syntax

## 9.3 recovery 노드 표시

parser는 recovery로 만든 노드에 플래그를 남겨야 한다.

예:

```txt
isMissing
isRecovered
hasSkippedTokens
```

semantic 단계는 이 플래그를 보고 보수적으로 동작해야 한다.

## 9.4 cascading diagnostics 억제

하나의 누락 토큰 때문에 수십 개의 엉뚱한 오류가 쏟아지지 않도록 diagnostics suppression 정책이 필요하다.

---

## 10. concurrency 모델

이 절은 `v0.5`에서 특히 중요하다.

## 10.1 기본 원칙

언어 서버는 다음 두 부류 작업을 구분해야 한다.

1. **foreground analysis**
   - open file completion
   - hover
   - diagnostics for active snapshot
   - signature help
   - semantic tokens for visible file
2. **background work**
   - workspace indexing
   - external symbol metadata loading
   - cold-cache semantic precomputation
   - document symbol warmup for closed files

foreground가 항상 우선한다.

## 10.2 cancellation policy

새 snapshot version이 들어오면 다음 규칙을 따른다.

- older syntax pass는 취소 가능하면 취소
- older semantic pass는 취소 가능하면 취소
- 취소가 불가능한 작업은 완료되더라도 결과를 publish하지 않음

## 10.3 publication freshness guarantee

LSP는 다음을 보장해야 한다.

- diagnostics는 최신 snapshot version에 대해서만 publish
- semantic tokens는 최신 snapshot version에 대해서만 return/publish
- hover/completion/signature help는 요청 시점의 snapshot 또는 그보다 최신 호환 snapshot 기준
- 오래된 version 결과가 최신 결과를 덮어쓰면 안 됨

## 10.4 analysis deduplication

같은 snapshot에 대해 동시에 여러 기능이 semantic model을 요구하는 경우, 가능하면 한 번의 semantic build 결과를 공유해야 한다.

예:

- completion 요청과 hover 요청이 거의 동시에 들어오면 semantic model future를 공유

## 10.5 thread safety

다음을 만족해야 한다.

- snapshot은 immutable
- semantic model은 immutable 또는 readonly snapshot-bound object
- caches는 synchronization-safe 구조 사용
- background indexing이 open document symbol table을 오염시키지 않음

## 10.6 starvation 방지

background indexing이 계속 새 작업을 만들어 foreground latency를 악화시키면 안 된다.

권장 정책:

- foreground queue와 background queue 분리
- foreground high-priority worker
- background tasks는 cooperative cancellation 지원

---

## 11. 성능 목표

정확한 목표치는 구현 환경에 따라 다를 수 있지만, `v0.5` reference target으로 다음 수준을 권장한다.

## 11.1 open-file foreground latency 목표

### completion

- warm cache 기준 p95 < 30ms
- cold-ish cache 기준 p95 < 80ms

### hover

- warm cache 기준 p95 < 30ms
- cold-ish cache 기준 p95 < 70ms

### syntax diagnostics first pass

- typical contest-sized file 기준 p95 < 40ms

### semantic diagnostics second pass

- typical contest-sized file 기준 p95 < 120ms

### semantic tokens full document

- warm cache 기준 p95 < 80ms

## 11.2 typical file size assumption

PSCP는 경쟁 프로그래밍 기준으로 single-file source가 많으므로, “typical file”은 대략 다음을 상정한다.

- 200 ~ 2000 lines
- generic type declarations 일부 포함 가능
- 하나의 파일에 여러 helper/function/type 선언이 섞일 수 있음

## 11.3 degraded mode

파일이 매우 크거나 external metadata가 아직 준비되지 않았을 때, 서버는 degraded mode로 동작할 수 있다.

예:

- syntax-only completion fallback
- reduced hover info
- delayed external-symbol enrichment

단, foreground responsiveness가 우선이다.

---

## 12. cache 정책

## 12.1 cache layers

권장 cache 계층:

1. token cache
2. syntax tree cache
3. AST cache
4. semantic model cache
5. intrinsic metadata cache
6. external symbol metadata cache
7. document symbol cache
8. semantic token cache
9. completion context cache (optional)

## 12.2 cache key

모든 문서 분석 캐시는 최소한 다음을 key로 가져야 한다.

- document URI
- snapshot version

## 12.3 invalidation

새 snapshot이 도착하면:

- 이전 version token/syntax/semantic result는 stale 처리
- 재사용 가능한 부분만 구조적으로 재활용
- stale semantic token result는 publish 금지

## 12.4 intrinsic metadata cache

intrinsic metadata는 정적이므로 process lifetime 동안 재사용 가능하다.

예:

- `stdin` members
- `stdout` members
- aggregate family signatures
- math intrinsic family signatures
- conversion-keyword doc metadata
- comparator sugar doc metadata
- known DS rewrite docs

---

## 13. symbol model

## 13.1 symbol kinds

semantic layer는 최소한 다음 symbol kind를 구분해야 한다.

- local variable
- parameter
- function
- type
- field
- property
- method
- constructor
- namespace
- intrinsic object
- intrinsic family symbol
- loop variable
- fast-iteration binding
- tuple element pseudo-symbol
- external .NET symbol
- discard pseudo-symbol
- ordering shorthand pseudo-symbol

## 13.2 symbol info fields

권장 필드:

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
isConstEligible
isReadonlyEligible
```

## 13.3 const / readonly relevance

`v0.5`에서 symbol model은 다음 추가 semantic fact를 들고 있으면 좋다.

- local immutable compile-time constant 여부
- static immutable field의 readonly lowering 적합 여부
- instance immutable field의 readonly 적합 여부

이건 hover / inlay hint / code action에서 큰 도움이 된다.

---

## 14. intrinsic model

LSP는 다음을 intrinsic semantic entity로 이해해야 한다.

## 14.1 intrinsic objects

- `stdin`
- `stdout`
- `Array.zero`

## 14.2 intrinsic shorthand

- declaration-based input shorthand
- statement-based output shorthand

## 14.3 intrinsic families

### aggregate family

- `min`
- `max`
- `sum`
- `sumBy`
- `minBy`
- `maxBy`
- `chmin`
- `chmax`

### math family

- `abs`
- `sqrt`
- `clamp`
- `gcd`
- `lcm`
- `floor`
- `ceil`
- `round`
- `pow`
- `popcount`
- `bitLength`

### conversion family

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

## 14.4 intrinsic semantic sugar

- `[]` vs `()` distinction
- `new[n]`
- `new![n]`
- `T.asc`, `T.desc`
- known data-structure operator rewrites
- `operator<=>(other)`
- tuple projection `.1`, `.2`, ...
- slicing / index-from-end

이들은 completion/hover/signature help/semantic tokens/diagnostics에서 별도 취급되어야 한다.

---

## 15. diagnostics 정책

## 15.1 diagnostics categories

1. lexical diagnostics
2. syntax diagnostics
3. binding diagnostics
4. type diagnostics
5. intrinsic semantic diagnostics
6. optional style / hint diagnostics

## 15.2 필수 diagnostic 영역

최소한 다음은 잡아야 한다.

1. immutable assignment
2. `_`를 값으로 사용
3. non-`rec` self recursion
4. invalid tuple projection
5. invalid spread outside `[]`
6. malformed input shorthand
7. malformed output shorthand
8. `break` / `continue` outside loop
9. malformed single-statement `then` / `else` / `do`
10. malformed or ambiguous space-separated call
11. invalid use of `ref`, `out`, `in`
12. invalid out-variable declaration
13. invalid `T.asc` / `T.desc`
14. invalid known collection auto-construction target
15. invalid `new![n]` element type
16. invalid aggregate family call shape
17. invalid math intrinsic call shape
18. invalid conversion-keyword use
19. invalid known DS operator target
20. invalid `:=` target
21. invalid slicing/index-from-end target
22. invalid `operator<=>(other)` usage
23. removed feature usage (`public:`, `private:` 등 access section label)
24. implicit return과 declared return type mismatch
25. final bare invocation in value-returning context
26. unsupported pass-through construct in current implementation subset
27. line/token boundary misuse 가능성 경고 (implementation can surface as info)

## 15.3 release-aware diagnostics

LSP는 transpiler policy와 연결된 진단도 제공할 수 있다.

예:

- explicit stepped range가 아닌데 descending semantics를 기대하는 코드
- `let a = 100` 이 const eligible임을 hint
- static immutable field가 readonly eligible임을 hint
- generator-fed aggregate가 direct-loop lowering candidate임을 info로 노출

## 15.4 diagnostics publish 전략

권장 전략:

1. 빠른 syntax diagnostics publish
2. 이후 semantic diagnostics publish

각 publish는 해당 document version 전체 diagnostics를 교체한다.

---

## 16. completion

## 16.1 목표

completion은 문맥에 맞는 정보 밀도가 높아야 한다.

단순히 identifier 목록만 뿌리는 것이 아니라,

- intrinsic family
- shorthand forms
- generic declarations
- pass-through .NET type/member
- lowered semantic hint

까지 제공할 수 있으면 좋다.

## 16.2 completion source

권장 source:

1. local symbol completion
2. keyword completion
3. intrinsic family completion
4. member completion
5. type completion
6. external .NET metadata completion
7. snippet completion

## 16.3 문맥별 completion

### statement start

추천 후보:

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

### expression position

추천 후보:

- local symbols
- aggregate family names
- math family names
- conversion-keyword forms
- type names
- relevant .NET symbols

### after `stdin.`

- `int`
- `long`
- `str`
- `char`
- `double`
- `decimal`
- `bool`
- `line`
- `lines`
- `words`
- `chars`
- `array`
- `list`
- `linkedList`
- `tuple2`
- `tuple3`
- `tuples2`
- `tuples3`
- `gridInt`
- `gridLong`
- `charGrid`
- `wordGrid`

### after `stdout.`

- `write`
- `writeln`
- `flush`
- `lines`
- `grid`
- `join`

### after `Array.`

- `zero`

### after type before comparator sugar

- `asc`
- `desc`

### inside type body

- ordinary access modifiers
- `operator<=>(other)` snippet
- generic member patterns

### range / builder context

문맥이 `[]` 또는 `()` generator 내부임을 인식하면, iterator variable name snippet이나 aggregate completion을 더 적극적으로 제안할 수 있다.

## 16.4 completion item에 담아야 할 정보

가능하면 다음을 포함한다.

- label
- insert text
- detail (type/signature)
- documentation
- snippet 여부
- intrinsic / pass-through 여부
- experimental / removed feature 여부

---

## 17. hover

## 17.1 목표

hover는 단순 타입 표시를 넘어서, PSCP sugar의 의미를 짧게 설명해야 한다.

## 17.2 hover에 포함할 정보

가능하면 다음을 제공한다.

1. symbol kind
2. declared or inferred type
3. mutability
4. intrinsic 여부
5. signature
6. shorthand expansion meaning
7. lowering-related semantic hint (optional)
8. external .NET doc summary (가능하면)

## 17.3 특히 중요한 hover 케이스

### declaration-based input shorthand

```txt
int n =
```

hover는 대략 이런 정보를 줄 수 있다.

- declaration-based input shorthand
- semantic meaning: `int n = stdin.int()`

### statement-based output shorthand

```txt
= ans
+= arr
```

- write / writeln shorthand 설명

### `[]` vs `()`

- materialized collection vs generator distinction
- allocation intent

### aggregate family

- overload family 설명
- iterable form / fixed-arity form distinction

### math family

- `abs(x)` return type expectations
- `sqrt(x)` return type expectations

### conversion keyword

- parse / truthiness / cast-like behavior 설명

### `:=`

- value-yielding assignment expression 설명

### `new![n]`

- per-element auto-construction 의미
- known collection element type requirement

### known DS operator rewrites

예:

```txt
visited += x
```

- `HashSet<T>.Add(x)` rewrite
- bool result preserved

### tuple projection

- tuple arity and selected element type

### `operator<=>(other)`

- default ordering shorthand 설명

### const/readonly eligibility

예:

```txt
let MOD = 1000000007
```

hover는 optional하게 다음을 보여줄 수 있다.

- immutable inferred local
- compile-time constant
- transpiler may lower to `const`

---

## 18. signature help

## 18.1 지원 대상

- parenthesized calls
- space-separated calls
- intrinsic families
- constructors
- pass-through .NET calls (metadata available 시)

## 18.2 modified arguments

signature help는 다음을 이해해야 한다.

- `ref`
- `out`
- `in`
- out-variable declaration
- `out _`

## 18.3 intrinsic family signature help

### aggregate

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

### math

- `abs(x)`
- `sqrt(x)`
- `clamp(x, lo, hi)`
- `gcd(a, b)`
- `lcm(a, b)`
- `pow(x, y)`

### conversion

compact doc-style signature help:

- `int(value)`
- `bool(value)`
- `string(value)`

---

## 19. semantic tokens

## 19.1 기본 목표

semantic tokens는 syntax coloring보다 더 풍부해야 한다.

즉 다음을 구분할 수 있어야 한다.

- immutable vs mutable binding
- intrinsic vs ordinary symbol
- pass-through modifier vs ordinary keyword
- conversion keyword in expression position
- aggregate family symbol
- math intrinsic symbol
- comparator sugar suffix
- removed feature usage

## 19.2 권장 token types

- keyword
- variable
- parameter
- function
- method
- type
- property
- namespace
- class
- struct
- interface
- number
- string
- operator

## 19.3 권장 token modifiers

- declaration
- readonly
- mutable
- intrinsic
- defaultLibrary
- external
- static
- modification
- removedFeature
- loweringHint (optional internal use)

## 19.4 language-specific highlighting targets

- `let` binding -> readonly/immutable 느낌
- `var`, `mut` -> mutable 느낌
- `stdin`, `stdout`, `Array.zero` -> intrinsic/default library
- aggregate family names
- math family names
- conversion keyword calls
- `:=`
- `new[n]`, `new![n]`
- tuple projection `.1`, `.2`, ...
- `T.asc`, `T.desc`
- known DS operator rewrites recognized by type
- `operator<=>(other)`
- removed access section label syntax if encountered

---

## 20. definition / references / rename

## 20.1 go-to-definition

지원 대상:

- local variable
- parameter
- function
- type declaration
- field / method declared in source
- intrinsic symbol doc surface
- pass-through .NET symbol when metadata available

special case:

- declaration shorthand definition target는 intrinsic doc surface로 연결 가능
- known DS operator rewrite는 underlying member 또는 intrinsic doc로 연결 가능

## 20.2 references

지원 대상:

- source user symbols
- type declarations
- fields / methods in source subset
- loop / fast-iteration bindings

가능하면 read / write reference 구분도 제공한다.

## 20.3 rename

지원 대상:

- variables
- parameters
- functions
- source types
- source fields / methods
- loop variables

rename 금지 대상:

- discard `_`
- intrinsic object (`stdin`, `stdout`)
- intrinsic family names
- comparator suffix `asc` / `desc`
- tuple projection `.1`, `.2`, ...
- removed syntax keyword / token
- external .NET symbols unless explicitly supported

---

## 21. document symbols

## 21.1 기본 노출 대상

- top-level function
- top-level type
- constructor
- method
- field
- property
- optionally significant top-level declarations

## 21.2 특수 표시 가능 대상

- `operator<=>(other)`
- generic type/function declaration
- top-level synthetic grouping

---

## 22. inlay hints

## 22.1 목표

짧은 contest code를 지나치게 어지럽히지 않으면서 의미를 보강한다.

## 22.2 추천 hint 종류

1. inferred type hints for `let` / `var`
2. tuple projection type hints
3. aggregate-family overload hints
4. math intrinsic result type hints
5. conversion-keyword result hints
6. generator vs materialized hint
7. const/readonly eligibility hint
8. known DS rewrite result-type hint

## 22.3 좋은 예

- `let MOD = 1000000007` -> optional hint: `const eligible`
- `let cmp = int.desc` -> comparer-like hint
- `visited += x` -> returns `bool`
- `new![n]` -> initializes each slot
- `sum (0..<n -> i do score(i))` -> generator-fed aggregate hint

---

## 23. code actions

## 23.1 quick fixes

추천 quick fixes:

1. add missing `rec`
2. insert braces for malformed `then` / `else` / `do`
3. add parentheses to disambiguate call
4. convert immutable binding to mutable when assignment occurs
5. rewrite declaration shorthand to explicit `stdin` form
6. rewrite explicit `stdin` form to shorthand when safe
7. rewrite `Math.Min` / `Math.Max` to `min` / `max`
8. rewrite `Math.Abs` / `Math.Sqrt` to `abs` / `sqrt`
9. rewrite parse/cast to conversion-keyword form
10. rewrite manual compare-update to `chmin` / `chmax`
11. rewrite `visited.Add(x)` to `visited += x`
12. rewrite explicit array init loop to `new![n]`
13. replace final bare invocation with `return`
14. suggest `const` for eligible local immutable binding
15. suggest `readonly` for eligible field
16. explain removed access section label syntax and suggest ordinary modifiers

## 23.2 refactorings

optional refactorings:

- convert parenthesized call <-> space-separated call
- convert block form <-> one-line form when safe
- convert generator-fed aggregate into explicit loop preview
- convert `new![n]` into explicit allocate+fill
- convert shorthand output into explicit `stdout` form

---

## 24. formatting

formatting은 optional이지만, 구현한다면 다음 원칙을 지켜야 한다.

1. braces를 보존
2. pass-through C# generic/type surface를 보존
3. single-statement `then` / `do`는 짧을 때만 한 줄 유지
4. `[]`와 `()`의 구분을 눈에 띄게 유지
5. aggregate family와 math family call 형태를 망치지 않음
6. shorthand input/output를 불필요하게 풀어쓰지 않음
7. removed syntax는 재삽입하지 않음

---

## 25. lowered-semantic-aware UX

`v0.5`에서는 단순 source meaning뿐 아니라 “transpiler가 어떻게 볼 가능성이 큰지”를 UX에 부분적으로 반영하는 것이 좋다.

예:

- `let a = 100` hover -> `const eligible`
- static immutable field hover -> `readonly eligible`
- `sum (0..<n -> i do f(i))` hover -> `direct loop lowering candidate`
- `new![n]` hover -> `allocate + init loop`
- `HashSet +=` hover -> `lowered to Add(value)`

이건 generated C# preview가 없어도 사용자에게 중요한 감각을 준다.

---

## 26. removed / changed feature policy

LSP는 `v0.5`에서 제거되거나 바뀐 기능도 사용자에게 분명히 알려야 한다.

### 26.1 removed reserved words

`match`, `when`은 더 이상 PSCP 예약어가 아니다.

LSP는 이를 ordinary identifier 또는 pass-through surface 후보로 다뤄야 한다.

### 26.2 removed access section labels

`public:`, `private:`, `protected:`, `internal:` 는 `v0.5`에서 제거되었다.

이 토큰 조합이 보이면:

- syntax or semantic diagnostic 제공
- code action으로 ordinary modifier 사용을 제안 가능
- hover에서 removed feature 설명 제공 가능

---

## 27. external metadata integration

## 27.1 목적

PSCP는 .NET pass-through surface를 많이 허용하므로, 가능하면 external symbol metadata와 연동하는 것이 좋다.

## 27.2 허용 전략

다음 중 하나 이상:

1. Roslyn integration
2. reflection-based metadata reading
3. stubbed metadata database
4. mixed strategy

`v0.5`에서는 완전성이 꼭 필요하진 않지만, 최소한 자주 쓰는 BCL surface는 completion/hover/signature help에 도움을 줄 수 있으면 좋다.

---

## 28. 테스트 전략

## 28.1 parser / recovery tests

반드시 포함할 것:

- malformed input shorthand
- malformed output shorthand
- malformed `[]` builder
- malformed generator `()`
- malformed spread/range
- malformed `->`
- malformed modified args
- malformed `:=`
- malformed slicing
- malformed generics
- malformed `operator<=>(other)`
- removed access section label usage

## 28.2 semantic tests

반드시 포함할 것:

- missing `rec`
- immutable assignment
- tuple projection typing
- invalid `T.asc` / `T.desc`
- invalid `new![n]`
- aggregate family classification
- math family classification
- conversion-keyword classification
- `HashSet +=` returns bool
- `Stack` / `Queue` / `PriorityQueue` rewrite recognition
- `:=` type/value behavior
- const eligibility
- readonly eligibility
- implicit return vs bare invocation distinction

## 28.3 LSP integration tests

반드시 포함할 것:

- open/change/close lifecycle
- stale analysis cancellation
- diagnostics freshness guarantee
- completion in incomplete code
- hover on shorthand and intrinsic family
- semantic tokens in partially broken document
- signature help for space-separated call + modified args
- code actions for intrinsic/math/DS sugar
- rename for local and supported type members

---

## 29. 구현 우선순위

권장 구현 순서:

1. document store + snapshot versioning
2. lexer / parser + recovery
3. syntax diagnostics
4. binder / semantic model
5. intrinsic classification
6. hover
7. completion
8. definition
9. document symbols
10. semantic tokens
11. signature help
12. references
13. rename
14. code actions
15. inlay hints
16. optional formatting
17. optional advanced external metadata integration

특히 초기에 priority가 높은 semantic 영역:

- shorthand input/output
- aggregate family
- math family
- conversion keyword
- `[]` vs `()`
- `new[n]`, `new![n]`
- known DS rewrite
- `:=`
- const/readonly eligibility
- `operator<=>(other)`

---

## 30. 최소 기능 세트

강한 `v0.5` 구현이 되려면 다음은 반드시 있어야 한다.

1. incremental document sync
2. syntax diagnostics
3. core semantic diagnostics
4. completion for locals / keywords / intrinsic families / shorthand / pass-through basics
5. hover for shorthand / intrinsic families / known DS rewrite / `:=` / `new![n]` / `operator<=>(other)`
6. go-to-definition for user symbols and intrinsic surfaces
7. document symbols
8. semantic tokens

강력 권장 기능:

9. signature help
10. references
11. rename
12. code actions
13. inlay hints

---

## 31. 예시 semantic UX

## 31.1 const eligible local

PSCP:

```txt
let MOD = 1000000007
```

LSP가 보여줄 수 있는 것:

- type: `int`
- immutable binding
- compile-time constant
- transpiler may lower to `const`

## 31.2 generator-fed aggregate

```txt
let total = sum (0..<n -> i do score(i))
```

LSP가 보여줄 수 있는 것:

- generator expression
- aggregate family overload
- likely direct-loop lowering candidate

## 31.3 known DS rewrite

```txt
if not (visited += x) then continue
```

LSP가 보여줄 수 있는 것:

- receiver type: `HashSet<T>`
- rewrite: `visited.Add(x)`
- return type: `bool`

## 31.4 `new![n]`

```txt
List<int>[] graph = new![n]
```

LSP가 보여줄 수 있는 것:

- target-typed array allocation
- known collection element auto-construction
- likely lowering: allocate + init loop

## 31.5 ordering shorthand

```txt
record struct Job(...) {
    operator<=>(other) => ...
}
```

LSP가 보여줄 수 있는 것:

- default ordering shorthand
- implicit return type `int`
- used by comparator sugar / ordered operations

---

## 32. 적합성

언어 서버 구현은 다음을 만족하면 이 명세에 적합하다.

1. snapshot/version 기반 분석을 수행한다.
2. stale semantic result를 최신 결과 위에 publish하지 않는다.
3. foreground analysis 우선 정책을 지킨다.
4. PSCP 고유 sugar와 pass-through C#/.NET surface를 모두 인식한다.
5. `v0.5` intrinsic family와 removed feature policy를 반영한다.
6. diagnostics, completion, hover, semantic tokens가 언어 의미와 모순되지 않는다.
7. concurrency / cancellation / cache 정책이 freshness를 보장한다.

---

## 33. 맺음말

PSCP `v0.5` 언어 서버의 핵심은 단순하다.

- 짧은 contest code를 빠르게 이해하고
- PSCP sugar를 깊게 해석하고
- pass-through C# surface와 충돌 없이 공존하며
- stale 결과 없이 신선한 semantic feedback을 주는 것

좋은 LSP는 단순히 “오류 밑줄을 긋는 도구”가 아니라,
PSCP의 문법설탕이 어떤 의미를 갖는지, 그리고 어떻게 쓰면 좋은지를 사용자가 에디터 안에서 자연스럽게 배우게 만드는 도구여야 한다.