# PSCP v0.5 트랜스파일러 명세 및 최적화 가이드

## 0. 문서 목적

이 문서는 PSCP `v0.5`의 트랜스파일러 동작 원칙과 reference backend 최적화 정책을 정의한다.

이 문서는 두 가지 역할을 동시에 가진다.

1. **구현 명세**
   - 어떤 소스 구조를 어떻게 분석하고 낮춰야 하는지 정의한다.
2. **최적화 가이드**
   - 어떤 생성 코드를 피해야 하는지,
   - 어떤 경우 반드시 직접 lowering 해야 하는지,
   - release build에서 어떤 제약을 지켜야 하는지 정의한다.

이 문서는 문법/API 통합 명세를 전제로 한다.  
즉 여기서는 “언어가 무엇을 의미하는가”를 다시 설명하기보다, **그 의미를 어떤 형태의 C#으로 출력해야 하는가**를 다룬다.

---

## 1. 트랜스파일러의 최우선 목표

PSCP 트랜스파일러의 최우선 목표는 다음과 같다.

1. PSCP source semantics를 정확히 보존한다.
2. 생성된 C#이 가능한 한 직접적이고 읽을 수 있어야 한다.
3. syntax sugar의 비용을 런타임이 아니라 컴파일 타임에 제거한다.
4. 경쟁 프로그래밍 기준으로 낭비되는 allocation, iterator, helper abstraction, dynamic dispatch를 최대한 피한다.
5. release build에서는 불필요한 defensive runtime check를 넣지 않는다.

짧게 말하면:

> PSCP는 편하게 쓰고, generated C#은 직접적이고 빠르게 나온다.

---

## 2. reference backend 철학

reference backend는 “모든 경우에 가장 영리한 최적화기”가 아니다.  
대신 다음 성격을 가진다.

- shape-driven
- compile-time specialization 중심
- direct loop lowering 선호
- helper runtime 최소화
- generated code readability 중시

즉 reference backend의 핵심은 다음이다.

1. **고급 의미를 먼저 파악**하고
2. **의미가 간단한 것일수록 직접적인 C#으로 낮추며**
3. **generic/runtime abstraction은 fallback으로만 남긴다**

---

## 3. 구현 파이프라인 권장 구조

권장 파이프라인은 다음과 같다.

1. lexing
2. parsing
3. AST construction
4. binding
5. type / shape analysis
6. intrinsic classification
7. semantic desugaring
8. lowering
9. optimization / cleanup
10. C# emission
11. optional formatting

권장 internal representation:

```txt
TokenStream
SyntaxTree
AstTree
BoundTree
LoweredTree
CSharpTree
```

---

## 4. frontend가 꼭 구분해야 하는 것

lowering 이전 단계에서 다음 distinction은 반드시 살아 있어야 한다.

## 4.1 declaration 종류

- immutable explicit typed declaration
- immutable inferred declaration (`let`)
- mutable inferred declaration (`var`)
- mutable explicit declaration (`mut`)
- declaration-based input shorthand
- sized array declaration
- known collection auto-construction declaration
- target-typed `new[n]`
- target-typed `new![n]`

## 4.2 output shorthand

- `= expr`
- `+= expr`
- ordinary `+=` assignment
- known data-structure operator candidate

## 4.3 calls

- ordinary parenthesized call
- space-separated call
- modified argument call (`ref`, `out`, `in`)
- aggregate intrinsic call
- conversion-keyword call
- pass-through external call

## 4.4 collection / generator

- materialized collection expression `[]`
- spread element
- range element
- materialized builder
- generator expression `(...)`
- fast iteration statement form

## 4.5 assignment 종류

- ordinary statement assignment
- compound assignment
- value-yielding assignment expression `:=`

## 4.6 type-body sugar

- ordinary pass-through member declaration
- `operator<=>(other)` shorthand
- comparator sugar usage site

---

## 5. 문서 수준에서 강제되는 핵심 제약

## 5.1 release build에서 transpiler-added throw 금지

release build에서 PSCP 트랜스파일러가 추가하는 artificial `throw`는 금지된다.

허용되는 예외:

- source에 사용자가 직접 쓴 `throw`
- pass-through C# semantics로 원래 발생하는 예외

금지되는 예:

- step==0 defensive throw
- helper default path의 generic “invalid state” throw
- transpiler가 무해한 문법설탕을 방어한다고 추가한 runtime throw

필요한 검사는 다음 중 하나여야 한다.

1. compile-time에 제거
2. `#if DEBUG` 로만 남김
3. `Debug.Assert` 계열로 남김

---

## 5.2 reference fast I/O baseline

reference backend는 fast I/O baseline으로 다음을 사용해야 한다.

- `Console.OpenStandardInput()` + `StreamReader`
- `Console.OpenStandardOutput()` + `StreamWriter`
- 충분한 buffer size
- 프로그램 종료 시 `flush`

이건 권장이 아니라 baseline contract다.

추가 고도화는 가능하지만, reference baseline은 이 수준이면 충분하다.

---

## 5.3 simple numeric range는 helper enumerable 금지

다음 형태는 가능한 경우 반드시 direct `for`로 낮춘다.

```txt
for i in 0..<n { ... }
0..<n -> i { ... }
1..n -> _ { ... }
```

release default path에서 helper enumerable을 기본 lowering target으로 쓰면 안 된다.

---

## 5.4 generic input helper는 shorthand lowering의 기본 target이 아니다

`stdin.read<T>()`, `stdin.array<T>()`, `stdin.tuple2<T1,T2>()` 같은 generic API surface는 source-level helper로 남을 수는 있다.

그러나 declaration-based shorthand lowering의 기본 경로는 다음이어야 한다.

- direct typed reader call
- direct allocate + direct fill loop
- fixed-shape tuple read loop

즉 runtime generic dispatcher는 fallback 또는 explicit source use 전용이다.

---

## 6. lowering의 최상위 전략

PSCP lowering의 핵심 원칙은 다음과 같다.

### 6.1 syntax sugar는 의미를 남기지 않고 제거한다

예:

- `int n =` -> typed scanner read
- `= expr` -> direct write call
- `[]` -> concrete materialization
- `()` generator -> iterable form
- `HashSet +=` -> direct `.Add`
- `new![n]` -> allocate + fill loop

### 6.2 shape를 알 수 있으면 helper를 거치지 않는다

예:

- fixed-size input array -> direct `for`
- generator-fed `sum` -> fused loop
- small-arity `min/max` -> compare tree
- `chmin/chmax` -> direct if assignment

### 6.3 pass-through는 정말 pass-through 한다

- `class`, `struct`, `record`
- generic declarations
- `new()`, `this`, `base`
- `namespace`, `using`
- explicit .NET API calls

굳이 덮어쓰지 않는다.

---

## 7. const / readonly lowering 규칙

`v0.5`에서 새로 강화하는 규칙이다.

## 7.1 local immutable compile-time constant

다음과 같은 local declaration이 다음 조건을 만족하면 generated C#에서 `const`를 사용한다.

예:

```txt
let a = 100
int b = 3
let s = "hello"
```

### 조건

- binding이 immutable
- initializer가 compile-time constant
- 대상 타입이 C# `const`로 표현 가능한 타입
- local binding 또는 field lowering context에서 `const`가 유효한 위치

### 예시

PSCP:

```txt
let a = 100
let newline = '\n'
let msg = "hello"
```

가능한 generated C#:

```csharp
const int a = 100;
const char newline = '\n';
const string msg = "hello";
```

## 7.2 const 대상이 아닌 immutable local

initializer가 compile-time constant가 아니면 ordinary immutable local로 생성한다.

예:

```txt
let x = stdin.int()
let y = a + b
```

이는 `const`가 아니다.

## 7.3 type-level static immutable binding

PSCP source가 static member를 pass-through로 표현하는 경우, 그 binding이 immutable이라면 generated C#에서 `readonly`를 사용한다.

### 규칙

- static + immutable + const 불가능 -> `static readonly`
- instance field + immutable + constructor/initializer-assigned -> `readonly` 가능 시 `readonly`

예:

PSCP source 의미:

```txt
static int MOD = 1000000007
```

이 값이 compile-time constant라면 `const`도 가능하지만, const 대상이 아니거나 pass-through mapping상 readonly가 더 적절하면:

```csharp
static readonly int MOD = 1000000007;
```

### 권장 정책

- local immutable constant -> 적극적으로 `const`
- static immutable field -> 가능한 경우 `readonly`
- instance immutable field -> 가능한 경우 `readonly`

---

## 8. binding / semantic analysis가 추가로 해야 할 일

다음은 단순 type checking을 넘어 lowering 품질에 직접 영향을 준다.

## 8.1 const-eligibility analysis

binding이 다음인지 판정해야 한다.

- compile-time constant
- runtime immutable
- mutable

이 분석이 있어야 `const` / `readonly` lowering이 가능하다.

## 8.2 generator escape analysis

generator expression이:

- aggregate에 즉시 소비되는지
- 다른 iterable로 저장/전달되는지
- materialization이 필요한지

를 알아야 fused lowering이 가능하다.

## 8.3 collection shape analysis

builder 결과가:

- fixed size인지
- cheaply countable인지
- unknown size인지

를 구분해야 한다.

## 8.4 known DS operator applicability

receiver static type이 다음인지 알아야 한다.

- `HashSet<T>`
- `Stack<T>`
- `Queue<T>`
- `PriorityQueue<TElement, TPriority>`

## 8.5 ordering availability

`T.asc`, `T.desc`, `min/max` family, `sort()` 등에 대해 default ordering availability를 판단해야 한다.

---

## 9. 입력 lowering

## 9.1 scalar input shorthand

예:

```txt
int n =
long x =
string s =
```

권장 lowering:

```csharp
int n = stdin.@int();
long x = stdin.@long();
string s = stdin.@str();
```

## 9.2 multiple scalar input

```txt
int n, m =
```

권장 lowering:

```csharp
int n = stdin.@int();
int m = stdin.@int();
```

## 9.3 fixed-size array input

```txt
int[n] arr =
```

권장 lowering:

```csharp
int[] arr = new int[n];
for (int i = 0; i < n; i++)
    arr[i] = stdin.@int();
```

## 9.4 tuple array input

```txt
(int, int)[m] edges =
```

권장 lowering:

```csharp
(int, int)[] edges = new (int, int)[m];
for (int i = 0; i < m; i++)
    edges[i] = (stdin.@int(), stdin.@int());
```

## 9.5 grid input

```txt
int[n][m] grid =
```

권장 lowering:

- allocate outer array
- allocate each row
- fill with nested loops

## 9.6 charGrid / wordGrid

`stdin.charGrid(n)` 과 `stdin.wordGrid(n)` 는 source-level helper로 허용되며, generated code에서도 helper call을 남겨도 된다.

다만 `line()` / token scanner interaction은 line boundary를 정확히 처리해야 한다.

### 매우 중요한 구현 제약

토큰 scanner 다음에 `line()` 또는 `charGrid()`가 오더라도, 첫 줄이 빈 문자열로 잘못 소비되면 안 된다.

즉 scanner는 “token read 이후 남아 있는 줄 끝 처리 정책”을 명시적으로 가져야 한다.

---

## 10. 출력 lowering

## 10.1 shorthand output

```txt
= expr
+= expr
```

권장 lowering:

```csharp
stdout.write(expr);
stdout.writeln(expr);
```

## 10.2 scalar rendering

스칼라 값은 direct writer call을 선호한다.

## 10.3 tuple rendering

작은 tuple arity는 specialized overload 또는 direct rendering으로 처리한다.

## 10.4 1D collection rendering

primitive / scalar element collection은 specialized join-like rendering을 선호한다.

## 10.5 nested structure rendering

grid는 명시적 `stdout.grid` 또는 row-wise lowering이 바람직하다.

---

## 11. `[]` materialized collection lowering

## 11.1 fixed-size materialized builder

예:

```txt
[0..<n -> i do f(i)]
```

권장 lowering:

1. size 계산
2. 한 번 allocate
3. direct fill

예시 shape:

```csharp
var result = new int[n];
for (int i = 0; i < n; i++)
    result[i] = f(i);
```

## 11.2 block builder

```txt
[0..<n -> i {
    let x = g(i)
    x + 1
}]
```

도 동일하게 direct fill을 선호한다.

## 11.3 spread lowering

`[1, 2, ..xs, 7]` 는:

- total length를 싸게 알 수 있으면 pre-size
- 아니면 growable builder 후 finalize

## 11.4 range element lowering

`[]` 안 range는 auto-expand 되지만, helper enumerable을 거칠 필요는 없다.  
가능하면 direct fill로 처리한다.

---

## 12. generator lowering

## 12.1 generator의 기본 의미

`()` generator expression은 iterable semantics를 가진다.

즉 기본적으로 materialization을 의미하지 않는다.

## 12.2 aggregate consumer와 결합될 때

예:

```txt
sum (0..<n -> i do f(i))
```

권장 lowering:

- no intermediate array
- no helper enumerable if avoidable
- one direct accumulation loop

## 12.3 generator escape context

generator가 일반 iterable 값으로 저장/반환/전달되면 helper iterable을 허용할 수 있다.

그러나 simple aggregate / simple loop / immediate consumption에서는 direct lowering을 우선한다.

---

## 13. range lowering

## 13.1 기본 range는 step `+1`

`a..b`, `a..<b`, `a..=b` 는 기본적으로 step `+1` 이다.  
역방향 자동 추론은 금지다.

## 13.2 explicit stepped range

`a..step..b` 는 explicit step을 가진다.

예:

```txt
m-1..-1..0
```

## 13.3 simple numeric range fast path

다음은 direct `for`로 내려야 한다.

- `0..<n`
- `1..n`
- `0..=n`
- explicit descending step with compile-time known step

예:

```csharp
for (int i = 0; i < n; i++) { ... }
for (int i = 1; i <= n; i++) { ... }
for (int i = m - 1; i >= 0; i--) { ... }
```

## 13.4 dead runtime checks 제거

step이 compile-time constant이고 0이 될 수 없으면, `step==0` check를 생성하면 안 된다.

release build에서 이런 `throw`는 금지다.

---

## 14. fast iteration `->` lowering

## 14.1 statement form over range

```txt
0..<n -> i {
    ...
}
```

는 direct `for` 로 낮춘다.

## 14.2 indexed form over random-access collection

```txt
xs -> i, x {
    ...
}
```

receiver가 random-access array/list 형태라면 indexed loop를 선호할 수 있다.

## 14.3 general iterable form

array/list/range가 아닌 일반 iterable이라면 `foreach`를 사용할 수 있다.

## 14.4 discard binding

`0..<t -> _ { ... }` 같은 경우, backend에서는 `_ = loopVar;` 같은 문장을 생성하면 안 된다.

올바른 lowering:

- synthetic discard variable name 사용
- 혹은 직접 쓰이지 않는 ordinary loop variable name 사용
- 본문에 무의미한 discard assignment 문장을 생성하지 않음

---

## 15. assignment expression `:=` lowering

## 15.1 기본 원칙

가능하면 backend의 native assignment expression을 그대로 사용한다.

예:

```txt
parent[x] := find(parent[x])
```

은 가능하면 C# assignment expression으로 직접 출력한다.

## 15.2 temporary 도입

복잡한 lhs / indexing / property chain으로 인해 side-effect order 보존이 필요할 때만 temporaries를 도입한다.

## 15.3 nested `:=`

`:=` 는 right-associative이므로, lowering은 그 평가 순서를 보존해야 한다.

---

## 16. aggregate family lowering

## 16.1 small fixed-arity `min/max`

```txt
max a b c d
```

는 direct compare tree로 내린다.

임시 배열 생성 금지.

## 16.2 iterable `min/max`

한 번 순회하며 best를 추적한다.

empty iterable 정책은 language/API contract에 맞춰야 하며, release default path에서 무의미한 artificial throw를 기본으로 넣지 않는다.

## 16.3 `sum`

typed accumulator를 사용한 direct loop.

## 16.4 `sumBy`

selector를 원소당 한 번만 평가하고 direct accumulate.

## 16.5 `minBy` / `maxBy`

best element와 best key를 동시에 추적.

## 16.6 `chmin` / `chmax`

가능하면 helper call보다 direct conditional rewrite를 선호.

예:

```csharp
if (cand < best) { best = cand; return true; }
return false;
```

---

## 17. math intrinsic lowering

## 17.1 `abs`

기본적으로 `Math.Abs` 또는 이에 준하는 direct lowering.

primitive signed integer / floating scalar에 대해 specialized call 사용 가능.

## 17.2 `sqrt`

기본적으로 `Math.Sqrt`로 직접 lowering.

## 17.3 기타 math family

`gcd`, `lcm`, `popcount`, `bitLength` 등은 helper runtime 또는 direct lowered implementation을 가질 수 있다.  
reference backend는 hot path에서 generic/dynamic dispatch를 피해야 한다.

---

## 18. conversion keyword lowering

## 18.1 string -> numeric

direct parse call.

예:

- `int "123"` -> `int.Parse(...)`
- `double "3.14"` -> `double.Parse(...)`

## 18.2 numeric -> numeric

direct cast/convert expression.

## 18.3 bool -> numeric

`0/1` ternary 또는 equivalent.

## 18.4 numeric -> bool

`value != 0`.

## 18.5 string -> bool

PSCP truthiness policy를 따른다.  
즉 empty string이면 false, non-empty string이면 true 방향을 기본으로 한다.

## 18.6 redundant conversion 제거

이미 같은 타입이면 redundant conversion 제거.

## 18.7 compile-time constant conversion folding

가능하면 compile-time에 folding.

예:

- `int "123"` -> constant 123
- `int true` -> constant 1
- `bool ""` -> constant false

---

## 19. comparator / ordering lowering

## 19.1 `T.asc`, `T.desc`

가능하면 comparer object를 cache하거나 direct backend comparer 형태로 낮춘다.

## 19.2 primitive ordered type fast path

primitive type의 small compare는 comparer dispatch 대신 direct compare를 선호한다.

## 19.3 `operator<=>(other)`

이 shorthand는 type의 default ordering source로 lowering되어야 한다.

가능한 backend strategy:

- generated `CompareTo`
- helper comparer method
- comparer factory

핵심은 semantic ordering source로 일관되게 쓰인다는 점이다.

---

## 20. known DS operator rewrite lowering

## 20.1 `HashSet<T>`

- `set += x` -> `set.Add(x)`
- `set -= x` -> `set.Remove(x)`

반환값 bool을 그대로 보존한다.

## 20.2 `Stack<T>`

- `s += x` -> `s.Push(x)`
- `~s` -> `s.Peek()`
- `--s` -> `s.Pop()`

## 20.3 `Queue<T>`

- `q += x` -> `q.Enqueue(x)`
- `~q` -> `q.Peek()`
- `--q` -> `q.Dequeue()`

## 20.4 `PriorityQueue<TElement, TPriority>`

- `pq += (item, priority)` -> `pq.Enqueue(item, priority)`
- `~pq` -> item only
- `--pq` -> item only

priority가 필요하면 explicit API를 남긴다.

중요:

wrapper object를 만들지 말고, direct underlying .NET call로 낮춘다.

---

## 21. `new[n]`, `new![n]` lowering

## 21.1 `new[n]`

target element type을 context에서 resolve하고 direct array allocation.

## 21.2 `new![n]`

1. array allocation
2. direct initialization loop

예:

```csharp
var graph = new List<int>[n];
for (int i = 0; i < n; i++) graph[i] = new();
```

LINQ 기반 initialization 금지.

---

## 22. helper emission policy

## 22.1 used helper only

generated program에는 사용된 helper/member만 emit 하는 것을 기본 정책으로 한다.

즉 매번 거대한 helper runtime 전체를 붙이지 않는다.

## 22.2 dead using 제거

사용되지 않는 `using`은 제거한다.

## 22.3 helper library vs inline lowering

원칙:

- 의미가 단순하면 inline lowering
- shape가 복잡하거나 재사용 가치가 크면 helper
- reference backend hot path는 helper보다 direct lowering 선호

## 22.4 금지 helper

release reference backend에서 ordinary statement lowering을 위해 lambda thunk를 만드는 helper (`expr<T>(Func<T>)` 류)는 금지한다.

---

## 23. scanner / writer runtime 구조 권장안

## 23.1 `stdin`

권장 generated helper shape:

- `@int()`
- `@long()`
- `@str()`
- `@char()`
- `@double()`
- `@decimal()`
- `@bool()`
- `line()`
- `charGrid(int n)`
- 필요한 경우 최소한의 structured helper

## 23.2 `stdout`

권장 generated helper shape:

- `write(int)`
- `write(long)`
- `write(string)`
- `write(char)`
- `write(tuple)` small arity
- `write(array/list)` for common scalar types
- `writeln(...)`
- `flush()`

## 23.3 Run + flush pattern

생성된 entry point는 다음 패턴을 권장한다.

```csharp
public static void Main()
{
    Run();
    stdout.flush();
}
```

`try/finally`로 flush를 강제하는 기본 생성은 권장하지 않는다.

---

## 24. local cleanup optimization

lowering 이후, emission 전 단계에서 가벼운 cleanup pass를 권장한다.

대상:

1. dead temporary 제거
2. constant folding
3. redundant conversion 제거
4. dead `step==0` guard 제거
5. trivial compare-tree flattening
6. unused helper/member emission prune
7. tuple construction 후 즉시 projection 제거

---

## 25. generated code readability 규칙

PSCP의 generated C#은 디버깅 가능해야 한다.

따라서 다음을 선호한다.

- direct `for`
- direct `if`
- direct local variable
- direct scanner call
- direct writer call
- helper nesting 최소화

피해야 할 것:

- 의미 없는 `_ = loopVar;`
- helper chain 남발
- 거대한 generic runtime dispatcher
- artificial lambda thunk
- dead guard / dead branch

---

## 26. anti-pattern 목록

reference backend에서 피해야 할 대표 anti-pattern:

1. simple numeric range -> helper enumerable
2. shorthand input -> generic `read<T>()`
3. fixed-shape array read -> LINQ `Select(...).ToArray()`
4. generator-fed aggregate -> intermediate materialization
5. `HashSet +=` -> wrapper abstraction
6. `new![n]` -> LINQ initialization
7. aggregate numeric core -> `dynamic`
8. conversion core -> reflection-like runtime generic conversion
9. release build에 transpiler-added throw 남김
10. discard binding을 per-iteration statement로 남김

---

## 27. optimization checklist

좋은 `v0.5` backend라면 다음 질문에 “예”라고 답할 수 있어야 한다.

1. `0..<n -> i { ... }` 가 direct `for` 로 내려가는가?
2. `sum (0..<n -> i do f(i))` 가 중간 allocation 없이 direct loop가 되는가?
3. `int[n] arr =` 가 direct scanner fill loop로 내려가는가?
4. `HashSet += x` 의 bool 반환이 보존되는가?
5. `new![n]` 가 one allocation + one init loop가 되는가?
6. `_` discard가 generated code에서 의미 없는 assignment 문장을 만들지 않는가?
7. release build에 transpiler-added throw가 남지 않는가?
8. `let a = 100` 이 적절한 경우 `const int a = 100` 으로 내려가는가?
9. static immutable field가 적절한 경우 `readonly` 또는 `const` 로 내려가는가?
10. generated code가 helper noise 없이 읽을 수 있는가?

---

## 28. 예시

## 28.1 const local lowering

PSCP:

```txt
let MOD = 1000000007
let hello = "hello"
```

권장 generated C#:

```csharp
const int MOD = 1000000007;
const string hello = "hello";
```

## 28.2 DSU path compression

PSCP:

```txt
rec int find(int x) {
    if x == parent[x] then x
    else parent[x] := find(parent[x])
}
```

권장 generated C#:

```csharp
static int find(int x)
{
    if (x == parent[x]) return x;
    return parent[x] = find(parent[x]);
}
```

## 28.3 generator-fed sum

PSCP:

```txt
let total = sum (0..<n -> i do score(i))
```

권장 generated C# shape:

```csharp
long total = 0;
for (int i = 0; i < n; i++)
    total += score(i);
```

## 28.4 `new![n]`

PSCP:

```txt
List<int>[] graph = new![n]
```

권장 generated C#:

```csharp
List<int>[] graph = new List<int>[n];
for (int i = 0; i < n; i++) graph[i] = new();
```

---

## 29. 결론

PSCP `v0.5` 트랜스파일러의 기준은 명확하다.

- syntax sugar는 컴파일 타임에 제거되어야 하고
- generated C#은 직접적이어야 하며
- helper runtime은 최소화되어야 하고
- release build에는 artificial runtime noise가 없어야 한다.

특히 `v0.5`에서는 다음이 중요하다.

- `const` / `readonly` lowering
- release build throw 금지
- StreamReader / StreamWriter baseline
- simple range direct `for`
- direct aggregate lowering
- generic helper의 지위 축소

이 문서는 그 기준을 구현자에게 강하게 못박기 위한 문서다.

