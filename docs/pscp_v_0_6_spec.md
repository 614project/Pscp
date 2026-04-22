# PSCP v0.6 통합 언어 / API / 트랜스파일 사양서

## 0. 문서 목적과 범위

이 문서는 PSCP `v0.6`의 **통합 사양서**다.

이 문서는 다음 셋을 분리하지 않고 하나의 체계로 함께 정의한다.

1. **언어 문법**
2. **내장 API / intrinsic family / helper surface**
3. **트랜스파일 및 lowering 계약**

PSCP에서는 이 셋이 강하게 결합되어 있다.

예를 들어:

- `int n =` 는 문법이면서 동시에 `stdin.readInt()` 의미를 가진다.
- `= expr` 는 문법이면서 동시에 `stdout.write(expr)` 의미를 가진다.
- `sum (0..<n -> i do f(i))` 는 aggregate family 호출이면서 generator를 direct loop로 낮추는 계약과 연결된다.
- `new![n]` 는 문법이면서 동시에 allocate + init loop 의미를 가진다.
- `list += x` 는 연산자 설탕이면서 `List<T>.Add(x)` 또는 `LinkedList<T>.AddLast(x)` 로 lowering되는 API 계약이다.

따라서 PSCP는 “문법”, “API”, “트랜스파일 전략”을 따로 읽는 것보다,

> 하나의 기능을 source 의미와 API 의미, lowering 의미까지 함께 읽는 편이 정확하다.

이 문서는 그 목적을 위해 작성된다.

---

## 1. 설계 목표

PSCP의 핵심 목표는 다음과 같다.

1. **문제 풀이 코드를 짧게 쓴다.**
2. **의도를 빠르게 드러낸다.**
3. **반복되는 입출력 보일러플레이트를 줄인다.**
4. **range / aggregate / collection / data structure 조작을 압축해 표현한다.**
5. **C#/.NET 생태계를 유지한다.**
6. **문법 설탕의 비용은 런타임이 아니라 트랜스파일러가 감당한다.**
7. **generated C#은 직접적이고 읽을 수 있어야 한다.**
8. **사용자 코드가 intrinsic 이름과 충돌할 때 일반 언어 관습을 따른다.**

즉 PSCP는 새로운 생태계를 강요하는 언어가 아니라,

> C#과 .NET을 유지하면서, 문제 풀이 문맥에서 반복되는 귀찮음을 줄이는 언어

를 목표로 한다.

---

## 2. 비목표

다음은 PSCP의 직접 목표가 아니다.

1. 일반 목적 애플리케이션 프레임워크 제공
2. .NET 전체 API를 새 이름으로 재포장하기
3. 모든 C# 기능을 새 PSCP 문법으로 바꾸기
4. 모든 코드를 함수형 스타일로 강제하기
5. 모든 intrinsic을 runtime wrapper abstraction으로 감싸기

따라서 PSCP에서는 다음 같은 C#/.NET surface가 자연스럽다.

```txt
using System.Collections.Generic
Dictionary<int, List<int>> graph = new()
Math.Max(a, b)
PriorityQueue<int, int> pq = new()
```

---

## 3. 이름 해석과 우선순위

이 절은 `v0.6`에서 매우 중요하다.

## 3.1 일반 원칙

PSCP는 **사용자가 선언한 이름이 intrinsic 이름보다 우선한다.**

즉 unqualified name resolution의 우선순위는 다음과 같다.

1. 가장 가까운 lexical scope의 user-defined symbol
2. enclosing local function / outer scope symbol
3. 같은 block 또는 enclosing block의 local function group symbol
4. top-level user-defined function / variable / type
5. `using` 또는 namespace를 통해 들어온 ordinary external symbol
6. PSCP intrinsic family / intrinsic helper name

이 순서는 일반적인 언어 관습과 같아야 하며, 트랜스파일러가 임의로 바꾸면 안 된다.

## 3.2 intrinsic shadowing

intrinsic name이 user-defined symbol에 의해 가려졌다면, 그 scope에서는 intrinsic이 숨겨진다.

예:

```txt
rec int gcd(int a, int b) {
    if b == 0 then a
    else gcd(b, a % b)
}

= gcd(10, x)
```

여기서 `gcd`는 사용자 함수이므로, intrinsic `gcd`가 있더라도 **반드시 사용자 함수가 우선**해야 한다.

잘못된 lowering:

```csharp
return __PscpMath.gcd(...);
```

올바른 lowering:

```csharp
return gcd(...);
```

## 3.3 intrinsic object names

`stdin`, `stdout`, `Array.zero`, aggregate family 이름, math family 이름, comparator sugar 등은 intrinsic surface이지만, ordinary symbol resolution을 무시하지 않는다.

즉 사용자 코드가 `stdin`, `stdout`, `sum`, `gcd` 같은 이름을 가리면, unqualified 위치에서는 **가려진다**.

단, declaration-based input shorthand나 statement-based output shorthand는 identifier 이름에 의존하지 않는 독립 syntax이므로 shadowing의 영향을 받지 않는다.

## 3.4 transpiler obligation

트랜스파일러는 lowering 시에 “이 이름은 intrinsic이니까 무조건 special lowering” 해서는 안 된다.

반드시 **semantic binding 결과**를 먼저 보고, 그 binding이 intrinsic symbol일 때만 intrinsic lowering을 적용해야 한다.

---

## 4. pass-through와 PSCP-owned surface

PSCP를 이해할 때 가장 중요한 구분 중 하나다.

## 4.1 pass-through

PSCP가 가능한 한 C# 의미를 그대로 유지하는 표면.

예:

- `using`
- `namespace`
- `class`, `struct`, `record`, `record struct`
- generic syntax
- generic constraint syntax
- `new()`, `this`, `base`
- nullable marker `?`
- `is`, `is not`
- ordinary .NET member access
- ordinary access modifiers
- inline switch-expression surface

## 4.2 PSCP-owned surface

PSCP가 언어 차원에서 특별한 의미를 갖는 표면.

예:

- `let`, `var`, `mut`, `rec`
- declaration-based input shorthand
- statement-based output shorthand
- `[]` materialized collection
- `()` generator expression
- `:=`
- `new[n]`, `new![n]`
- aggregate family
- math intrinsic family
- conversion keyword calls
- comparator sugar
- known data structure operator rewrites

## 4.3 통합 문서 규칙

이 문서는 기능을 설명할 때 가능한 한 다음 순서로 설명한다.

1. source surface
2. source meaning
3. exact API semantics
4. lowering contract
5. 충돌 규칙 / warning / edge case

---

## 5. 어휘, 문장, 블록

## 5.1 블록

PSCP는 들여쓰기가 아니라 **중괄호**를 사용한다.

```txt
if x < 0 {
    x = -x
}
```

### lowering contract

중괄호 블록은 가능한 한 그대로 C# 블록으로 보존한다.

---

## 5.2 문장 구분

문장은 보통 줄바꿈으로 구분된다.  
세미콜론 `;`은 한 줄 다중 문장 분리에 사용할 수 있다.

```txt
var x = 0; x += 1; += x
```

### lowering contract

세미콜론은 source convenience일 뿐이다. generated C#은 ordinary statement formatting을 사용한다.

---

## 5.3 top-level 구성 요소

하나의 PSCP 파일에는 다음이 섞여 있을 수 있다.

- top-level statements
- top-level function declarations
- type declarations
- `using`
- `namespace`

### lowering contract

reference backend는 보통 synthetic program class와 `Main()` / `Run()` 구조를 생성한다.

권장 형태:

```csharp
public static void Main()
{
    Run();
    stdout.flush();
}
```

`try/finally`로 flush를 강제하는 기본 생성은 지양한다.

---

## 6. 예약어와 제거된 기능

## 6.1 핵심 예약어

대표 예약어:

```txt
let var mut rec
if then else for in do while
break continue return
true false null
and or xor not
class struct record
ref out in
new this base
namespace using is
public private protected internal
```

## 6.2 제거된 예약어

`match`, `when`은 `v0.6`에서 별도 PSCP 구문으로 예약하지 않는다.

즉 `match`, `when`은 ordinary identifier 또는 pass-through surface 문맥으로 다뤄진다.

## 6.3 제거된 section label

`public:`, `private:`, `protected:`, `internal:` 같은 access section label은 제거된 기능이다.

ordinary C# modifier만 남는다.

---

## 7. 타입과 선언

## 7.1 기본 타입

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

## 7.2 튜플 타입

```txt
(int, int)
(long, int, string)
```

## 7.3 배열 타입

```txt
int[]
int[][]
(string, int)[]
```

## 7.4 generic type

```txt
List<int>
Dictionary<int, string>
PriorityQueue<int, int>
```

generic type declaration과 사용은 C# surface를 따른다.

예:

```txt
class Box<T> {
    T value
}

T id<T>(T x) {
    x
}
```

## 7.5 generic constraint

generic constraint 역시 pass-through다.

예:

```txt
T maxOfTwo<T>(T a, T b) where T : IComparable<T> {
    if a.CompareTo(b) >= 0 then a else b
}
```

트랜스파일러는 generic constraint syntax를 재해석하지 않는다.

## 7.6 nullable marker `?`

`?` 는 C# nullable marker와 호환되는 pass-through surface다.

```txt
string?
NodeData?
int?
```

### lowering contract

가능한 한 C# nullable surface를 그대로 유지한다.

---

## 8. 변수 선언, 가변성, 구조 분해

## 8.1 `let`

```txt
let x = 10
let s = "abc"
```

의미:

- 타입 추론
- 불변

### lowering contract

immutable binding이다.  
또한 compile-time constant면 `const` lowering 후보가 된다.

예:

```txt
let MOD = 1000000007
let NL = '\n'
let MSG = "hello"
```

가능한 generated C#:

```csharp
const int MOD = 1000000007;
const char NL = '\n';
const string MSG = "hello";
```

---

## 8.2 `var`

```txt
var sum = 0
```

의미:

- 타입 추론
- 가변

---

## 8.3 `mut`

```txt
mut int ans = 0
```

의미:

- 명시적 타입
- 가변

---

## 8.4 명시적 타입 + 불변

```txt
int n = 3
string text = "hello"
```

이 역시 immutable binding이다.

### lowering contract

compile-time constant면 local `const` lowering 후보가 된다.

---

## 8.5 초기화 없는 선언

### 허용되는 경우

- `mut` scalar/reference
- sized array declaration
- known collection auto-construction

예:

```txt
mut int x;
int[n] arr;
List<int> list;
Queue<int> q;
```

### lowering contract

- `mut int x;` -> default 초기화 local/field
- `int[n] arr;` -> direct array allocation
- `List<int> list;` -> implicit `new()`

---

## 8.6 known collection auto-construction

initializer 없이 선언 시 implicit `new()` 대상으로 인식하는 type:

- `List<T>`
- `LinkedList<T>`
- `Queue<T>`
- `Stack<T>`
- `HashSet<T>`
- `Dictionary<K, V>`
- `SortedSet<T>`
- `PriorityQueue<TElement, TPriority>`

예:

```txt
List<int> list;
Queue<int> q;
HashSet<int> visited;
```

### lowering contract

```csharp
List<int> list = new();
Queue<int> q = new();
HashSet<int> visited = new();
```

---

## 8.7 const / readonly / static readonly lowering

PSCP source가 type member context에서 immutable field를 표현하는 경우,
트랜스파일러는 다음 lowering을 고려해야 한다.

### 규칙

- compile-time constant 가능 -> `const`
- static immutable field + const 불가 -> `static readonly`
- instance immutable field + constructor/initializer-assigned -> `readonly`

즉 `v0.6`에서는 단순히 “불변이다”에서 끝나는 것이 아니라,
**generated C#의 `const` / `readonly` / `static readonly` 품질도 목표**로 삼는다.

---

## 8.8 구조 분해 선언 / 다중 바인딩

PSCP는 tuple-like destructuring declaration과 다중 바인딩을 허용한다.

예:

```txt
let a, b = foo()
let suffix, rank = makeSuffix(...)
(int x, int y) = p
(a, _, c) = foo()
```

### 의미

- 오른쪽 값이 tuple 또는 destructurable value일 때 왼쪽으로 분해한다.
- discard `_` 허용

### lowering contract

가능하면 C# tuple deconstruction으로 직접 보존한다.  
불가능한 경우 임시값을 만든 뒤 field access로 분해할 수 있다.

---

## 9. discard `_`

`_` 는 discard token이다.

허용 위치:

- declaration target
- assignment target
- destructuring target
- lambda parameter
- fast iteration binding
- `out _`

예:

```txt
_ = f()
let _ = g()
(a, _, c) = foo()
_ => 1
foo(out _)
0..<t -> _ {
    ...
}
```

### lowering contract

discard는 generated C#에서 무의미한 assignment 문장을 만들면 안 된다.

잘못된 lowering 예:

```csharp
_ = __item0;
```

올바른 lowering:

- synthetic 이름을 가진 ordinary loop variable
- discard 자체는 source semantics에만 존재

---

## 10. 호출, 람다, `ref/out/in`

## 10.1 괄호 호출

```txt
f(x)
g(x, y)
obj.method(a, b)
```

ordinary C# 호출과 호환된다.

## 10.2 space-call

PSCP는 간결한 호출을 위해 space-call을 허용한다.

```txt
f x
g x y
sum arr
min a b
```

### 규칙

- space-call은 left-associative application chain으로 해석한다.
- argument가 복잡하면 괄호를 쓴다.

예:

```txt
f (a + b) x
sum (0..<n -> i do score(i))
```

## 10.3 member call

```txt
xs.map(f)
arr.sort()
points.minBy(p => p.x)
```

## 10.4 람다

```txt
x => x + 1
(acc, x) => acc + x
(acc, x) => {
    let y = acc + x
    y % mod
}
```

discard parameter도 허용한다.

```txt
_ => 1
(a, _, c) => a + c
```

## 10.5 `ref`, `out`, `in`

PSCP는 modified argument를 C#과 같은 방향으로 지원한다.

### 괄호 호출

```txt
foo(ref x, out y, in z)
foo(out int a, out int b)
foo(out _, ref arr[i])
```

### space-call

```txt
foo ref x out y in z
foo out int a out int b
foo out _ ref arr[i]
```

### lowering contract

가능한 한 C# modified argument syntax를 그대로 보존한다.

---

## 11. 로컬 함수와 중첩 로컬 함수

PSCP는 C#처럼 **어느 block scope에서도 local function 선언을 허용**한다.

## 11.1 허용 위치

- ordinary block
- `if` / `else` block
- `while` body
- `for` body
- `->` body
- local function body 내부

즉 중첩 로컬 함수도 허용한다.

예:

```txt
for i in 0..<n {
    int helper(int x) {
        x + i
    }

    += helper(a[i])
}
```

그리고 다음도 허용한다.

```txt
int solve(int x) {
    int inner(int y) {
        int deep(int z) {
            z + 1
        }
        deep(y) + x
    }
    inner(x)
}
```

## 11.2 바인딩 규칙

local function은 해당 block scope 전체에 대해 바인딩된다.

즉 선언 위치 앞뒤 호출 가능 여부, 자기 재귀, block 내부 상호 참조 등은 block-level binding 정책에 따라 처리해야 한다.

## 11.3 `rec`와 상호 재귀

### ordinary recursion

자기 자신을 재귀 호출하려면 `rec`가 필요하다.

```txt
rec int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

### mutual recursion

같은 block scope에서 연속해서 선언된 `rec` 함수들은 하나의 **mutually recursive group**을 형성할 수 있다.

예:

```txt
rec bool even(int n) {
    if n == 0 then true else odd(n - 1)
}
rec bool odd(int n) {
    if n == 0 then false else even(n - 1)
}
```

이 경우 `even`과 `odd`는 서로를 참조할 수 있다.

### rule

- same block
- contiguous `rec` declarations
- group으로 predeclare

## 11.4 lowering contract

트랜스파일러는 local function을 가능한 한 **C# local function**으로 직접 보존해야 한다.

금지되는 경향:

- ordinary local function을 굳이 lambda delegate로 바꾸기
- expression thunk helper로 감싸기
- loop body local function을 위험하게 바깥으로 hoist하기

### 원칙

1. block scope local function은 block scope local function으로 유지
2. nested local function도 동일
3. ordinary statement lowering 우선
4. capture semantics를 깨는 hoisting 금지
5. `rec` group은 binding 단계에서 먼저 해석

---

## 12. 제어 흐름과 암묵적 반환

## 12.1 `if`

```txt
if cond {
    ...
} else {
    ...
}

if bad then continue
if ok then return ans else return -1
```

## 12.2 dangling else rule

`else`는 항상 **가장 가까운 unmatched `if`** 에 결합한다.

즉:

```txt
if a then if b then c else d
```

는 다음과 같다.

```txt
if a then (if b then c else d)
```

## 12.3 `then` / `else` / `do`

- `then`은 바로 다음 한 statement에만 결합
- `else`도 바로 다음 한 statement에만 결합
- `do`도 바로 다음 한 statement에만 결합

길어지면 중괄호를 써야 한다.

## 12.4 `while`

```txt
while cond {
    ...
}
while cond do x += 1
```

## 12.5 `for ... in ...`

```txt
for i in 0..<n {
    ...
}
for i in 0..<n do sum += a[i]
```

## 12.6 `break`, `continue`, `return`

```txt
break
continue
return x
return
```

## 12.7 implicit return

value-returning function / local function / block expression에서,
**마지막 statement가 return-eligible expression statement** 이면 implicit return 대상이 된다.

### return-eligible expression statement 정의

다음만 implicit return 후보가 된다.

- ordinary non-assignment expression statement
- `:=` assignment expression statement
- conditional expression statement
- comparison/arithmetic/call-composed expression that is not classified as bare invocation or ordinary assignment statement

### 후보가 아닌 것

- ordinary assignment statement (`=`)
- compound assignment statement (`+=`, `-=`, ...)
- bare invocation statement

즉 다음은 implicit return이 아니다.

```txt
foo(x)
obj.method(y)
x = 5
x += 1
```

반면 다음은 implicit return이다.

```txt
a <=> b
not visited.Add(x)
parent[x] := find(parent[x])
if ok then 1 else 2
```

### lowering contract

가능하면 generated C#에서 direct `return expr;` 로 명시화한다.

---

## 13. 연산자와 우선순위

## 13.1 특별히 중요한 규칙

- `=` 와 `:=` 모두 expression 문맥에 올 수 있지만 의미가 다르다.
- `=` 는 compatibility assignment expression이며 warning 대상이 될 수 있다.
- `:=` 는 canonical value-yielding assignment expression이다.
- `~x` 와 `--x` 는 ordinary unary operator이거나, known DS operand라면 special rewrite다.
- `|>` / `<|` 는 application chain과 결합 규칙이 있다.

## 13.2 우선순위 표 (높음 → 낮음)

1. postfix
   - member access `.`
   - indexing / slicing `[]`
   - tuple projection `.1`, `.2`, ...
   - ordinary call `f(x)`
   - space-call application chain
   - postfix `++`, `--`

2. prefix unary
   - unary `+`, `-`
   - `!`, `not`
   - `~`
   - prefix `++`, `--`

3. multiplicative
   - `*`, `/`, `%`

4. additive
   - `+`, `-`

5. range
   - `..`, `..<`, `..=`
   - explicit stepped range `a..step..b`

6. comparison
   - `<`, `<=`, `>`, `>=`, `<=>`, `is`, `is not`

7. equality
   - `==`, `!=`

8. logical and
   - `&&`, `and`

9. xor
   - `^`, `xor`

10. logical or
   - `||`, `or`

11. pipe
   - `|>`, `<|`

12. assignment family
   - `=`
   - `:=`
   - `+=`
   - `-=`
   - `*=`
   - `/=`
   - `%=`

assignment family operators are right-associative.

### semantic note

assignment-family operators의 정확한 의미는 우선순위 표가 아니라 각 절에서 정의한다.

예를 들어 `list += x` 가 known DS rewrite인지, ordinary compound assignment인지 여부는 semantic binding에 의해 결정된다.

## 13.3 `|>` / `<|` 와 application chain

파이프는 단순 우선순위만으로 설명하면 헷갈리기 쉽다. `v0.6`에서는 다음 rewrite 규칙을 canonical semantics로 둔다.

### pipeline target 제한

파이프의 canonical target은 다음 중 하나다.

- bare identifier function head
- parenthesized callable expression

즉:

- `xs |> filter(pred)` 는 canonical
- `xs |> (pred => filter(pred))` 도 가능
- `xs |> obj.method(pred)` 같은 member-call target은 stable core의 canonical pipe target으로 두지 않는다. 이런 경우는 괄호나 람다로 의도를 명시한다.

### `lhs |> head arg1 arg2`

는

```txt
head lhs arg1 arg2
```

로 해석한다.

즉 `lhs`가 **오른쪽 application chain의 첫 번째 인자**로 삽입된다.

예:

```txt
value |> f x
```

는

```txt
f value x
```

이다.

### `lhs |> head(arg1, arg2)`

는

```txt
head(lhs, arg1, arg2)
```

로 해석한다.

예:

```txt
xs |> filter(pred)
```

는

```txt
filter(xs, pred)
```

이다.

즉 `filter(pred)(xs)` 가 아니다.

### `head arg1 arg2 <| rhs`

는

```txt
head arg1 arg2 rhs
```

즉 `rhs`가 **왼쪽 application chain의 마지막 인자**로 삽입된다.

### `head(arg1, arg2) <| rhs`

는

```txt
head(arg1, arg2, rhs)
```

로 해석한다.

---

## 14. range

## 14.1 기본 문법

```txt
1..10
0..<n
0..=n
10..-1..0
```

### 의미

- `a..b` : inclusive, step `+1`
- `a..<b` : right-exclusive, step `+1`
- `a..=b` : explicit inclusive
- `a..step..b` : explicit stepped range

### 매우 중요한 규칙

기본 range는 **역방향 step을 자동 추론하지 않는다.**

즉 descending이 필요하면 반드시 explicit step을 적어야 한다.

---

## 14.2 lowering contract

simple numeric range는 가능한 한 direct `for`로 낮춘다.

예:

```txt
0..<n
1..n
m-1..-1..0
```

가능한 generated C#:

```csharp
for (int i = 0; i < n; i++)
for (int i = 1; i <= n; i++)
for (int i = m - 1; i >= 0; i--)
```

### 금지 방향

- 단순 range를 helper enumerable 기본 경로로 내리기
- compile-time known step에 대해 dead `throw`를 남기기
- `a..b`에서 자동으로 descending step을 추론하기

---

## 15. `[]`, `()` 그리고 builder

## 15.1 `[]` = materialized collection

```txt
[1, 2, 3]
[0..<n]
[0..<n -> i do i * i]
```

의미:

- 실제로 만들어진 컬렉션
- 기본 문맥에선 array가 자연스러운 기본값
- typed context가 있으면 `List<T>` 등으로 들어갈 수 있음

### lowering contract

- 크기를 알 수 있으면 한 번 allocate + direct fill
- spread와 mixed element가 있으면 가능한 한 pre-size 또는 growable builder 후 finalize
- range element는 helper enumerable 없이 direct fill 우선

---

## 15.2 spread `..`

```txt
let a = [3, 4, 5]
let b = [1, 2, ..a, 6, 7]
```

의미:

- `[]` 안에서 iterable을 펼침

### lowering contract

- total size를 싸게 알 수 있으면 pre-size
- 아니면 growable builder

---

## 15.3 `()` = generator expression

```txt
(0..<n -> i do i * i)
```

의미:

- lazy iterable / generator 의미
- intermediate materialization을 의도하지 않음

### lowering contract

- aggregate에 즉시 소비되면 direct fused loop 우선
- 값으로 저장/반환/전달되면 helper iterable 허용 가능
- simple immediate-consumption context에서 unnecessary materialization 금지

---

## 15.4 materialized builder

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
```

### lowering contract

가능하면 direct allocate + fill loop로 내린다.

---

## 15.5 statement fast iteration `->`

```txt
xs -> x {
    ...
}

xs -> i, x {
    ...
}
```

의미:

- single binding: item
- indexed binding: index + item

### lowering contract

- source가 range면 direct `for`
- source가 array/list이고 index 필요하면 indexed loop 선호 가능
- general iterable이면 `foreach`
- discard binding은 무의미한 assignment 문장을 만들지 않음

---

## 16. 연산자: `=`, `:=`, `~`, `--`, `^`

## 16.1 ordinary assignment `=`

```txt
x = y
arr[i] = 0
```

PSCP에서 `=`는 ordinary assignment statement이면서, 필요하면 **compatibility assignment expression**으로도 읽힐 수 있다.

### 의미

- standalone statement로 쓰이면 ordinary assignment statement
- larger expression 내부에서 쓰이면 canonical form은 아니지만 compatibility assignment expression으로 허용 가능
- transpiler는 이에 대해 경고를 줄 수 있다

### practical rule

다음은 모두 파서가 수용할 수 있다.

```txt
a = b = c
f(x = 5)
```

단,

- `=` 는 canonical value-yielding assignment가 아니다
- block final implicit return 대상이 아니다
- value intention이 분명하면 `:=` 를 권장한다

예:

```txt
rec int find(int x) {
    if x == parent[x] then x
    else parent[x] = find(parent[x])
}
```

이 코드는 assignment는 되더라도 implicit return 의도로는 부적절하며, warning 대상이다.

## 16.2 value-yielding assignment `:=`

```txt
lhs := rhs
```

의미:

1. 대입한다
2. 그 대입값을 결과로 돌려준다

예:

```txt
parent[x] := find(parent[x])
a = b := c
```

### lowering contract

가능하면 C# native assignment expression으로 직접 보존한다.

예:

```csharp
return parent[x] = find(parent[x]);
```

## 16.3 `=` vs `:=` warning policy

트랜스파일러는 다음에 대해 경고할 수 있다.

- value position에서 `=` 사용
- `a = b = c` 같은 chained assignment
- 마지막 statement assignment가 implicit return 문맥으로 오해될 수 있는 경우

권장 rewrite:

```txt
a = b := c
```

## 16.4 `~` 규칙

`~expr` 는 기본적으로 **ordinary bitwise complement** 다.

단, 피연산자의 **정적 타입**이 다음 exact known type 중 하나이면 special rewrite를 적용한다.

- `Stack<T>`
- `Queue<T>`
- `PriorityQueue<TElement, TPriority>`

이 경우:

- `~stack` -> `stack.Peek()`
- `~queue` -> `queue.Peek()`
- `~pq` -> `pq`의 top element (priority 제외)

즉 `~`의 의미는 **타입 기반으로 결정**된다.

### 우선 규칙

1. operand static type이 known DS exact type이면 Peek rewrite
2. 아니면 ordinary bitwise complement

임의의 user-defined type에 대해서 `~`를 Peek rewrite로 해석하지 않는다.

## 16.5 `--` 규칙

`--expr` 는 기본적으로 **ordinary prefix decrement** 다.  
`expr--` 는 ordinary postfix decrement다.

단, 피연산자의 **정적 타입**이 다음 exact known type 중 하나이고, 연산자가 **prefix `--`** 일 때만 special rewrite를 적용한다.

- `Stack<T>`
- `Queue<T>`
- `PriorityQueue<TElement, TPriority>`

이 경우:

- `--stack` -> `stack.Pop()`
- `--queue` -> `queue.Dequeue()`
- `--pq` -> top element dequeue (priority 제외)

즉 `--`도 `~`와 마찬가지로 **타입 기반 special rewrite** 를 가진다.

### 우선 규칙

1. prefix `--` + known DS exact type -> Pop/Dequeue rewrite
2. 그 외 -> ordinary decrement semantics

postfix `stack--`, `queue--`, `pq--` 는 stable core에서 지원하지 않는다.

## 16.6 `^` / `xor`

`^` 는 C#과 동일하게 **bitwise XOR** 이다.  
`bool` 피연산자에서는 C#처럼 boolean XOR 의미를 가진다.

`xor` keyword는 `^`의 alias다.

즉:

- integer context: bitwise XOR
- bool context: boolean XOR

logical family라고 별도 재정의하지 않는다.

---

## 17. 입력 shorthand와 `stdin`

PSCP의 가장 큰 장점 중 하나다.

## 17.1 declaration-based input shorthand

### scalar

```txt
int n =
long x =
string s =
char c =
```

### multiple scalar

```txt
int n, m =
long a, b, c =
```

### arrays

```txt
int[n] arr =
long[m] cost =
```

### nested arrays

```txt
int[n][m] grid =
char[h][w] board =
```

### tuples

```txt
(int, int) p =
(int, int)[m] edges =
```

### source meaning

이들은 각각 `stdin`에 대한 direct read 또는 shaped read를 의미한다.

### lowering contract

**중요:** shorthand lowering의 기본 경로는 generic runtime dispatcher가 아니다.

예:

```txt
int[n] arr =
```

가능한 generated C#:

```csharp
int[] arr = new int[n];
for (int i = 0; i < n; i++)
    arr[i] = stdin.readInt();
```

즉 `stdin.read<T>()`, `stdin.readArray<T>()` 같은 generic helper는 source-level API로 존재할 수는 있어도,
**shorthand lowering의 기본 타겟이 되어서는 안 된다.**

---

## 17.2 explicit `stdin` API — canonical names

`v0.6`에서는 explicit input API 이름을 다음으로 **정규화**한다.

```txt
stdin.readInt()
stdin.readLong()
stdin.readString()
stdin.readChar()
stdin.readDouble()
stdin.readDecimal()
stdin.readBool()
stdin.readLine()
stdin.readLines(n)
stdin.readWords()
stdin.readChars()
stdin.readArray<T>(n)
stdin.readList<T>(n)
stdin.readLinkedList<T>(n)
stdin.readTuple2<T1, T2>()
stdin.readTuple3<T1, T2, T3>()
stdin.readTuples2<T1, T2>(n)
stdin.readTuples3<T1, T2, T3>(n)
stdin.readGridInt(n, m)
stdin.readGridLong(n, m)
stdin.readCharGrid(n)
stdin.readWordGrid(n)
stdin.readRestOfLine()
```

### 매우 중요한 정책

`stdin.int()` / `stdin.long()` / `stdin.str()` 같은 표기는 **PSCP source의 canonical explicit API가 아니다.**

이 이름들은 C# keyword collision을 만들거나, backend-private verbatim identifier (`@int`)와 섞이기 쉽기 때문이다.

즉:

- source-level canonical API는 `readInt`, `readLong`, ...
- backend가 generated C# 내부에서 `@int` 같은 이름을 쓰는 것은 구현 선택일 뿐
- source 문법은 canonical API를 기준으로 설명한다

## 17.3 token-readable type 제약

`readArray<T>`, `readList<T>`, `readLinkedList<T>`, `readTuple*` 계열의 `T`에는 제약이 있다.

`v0.6` stable core에서 **token-readable type** 은 다음과 같다.

1. scalar token types
   - `int`
   - `long`
   - `double`
   - `decimal`
   - `bool`
   - `char`
   - `string`
2. flat tuple of token-readable scalar types
   - arity 2 or 3
   - 예: `(int, int)`, `(long, string, bool)`

stable core 바깥:

- arbitrary user-defined class
- arbitrary user-defined struct
- dictionary/set-like shape
- arbitrary nested generic collection
- arbitrary nested tuple tower

즉:

- `stdin.readArray<int>(n)` 가능
- `stdin.readArray<(int,int)>(n)` 가능
- `stdin.readArray<MyStruct>(n)` 는 stable core 바깥

## 17.4 `readBool()` 정책

`readBool()` 는 다음 토큰을 true/false로 읽는다.

- case-insensitive `true` -> `true`
- case-insensitive `false` -> `false`
- `1` -> `true`
- `0` -> `false`

그 외 토큰은 **invalid bool token** 이다.

language-level valid input contract는 `true/false/1/0` 이다.

reference backend는 invalid bool token에 대해 parse failure로 취급한다.

## 17.5 line/token contract

PSCP의 line-oriented API는 `v0.6`에서 다음처럼 **명시적으로 정의**한다.

### token-oriented readers

- `readInt`, `readLong`, `readString`, `readChar`, `readDouble`, `readDecimal`, `readBool`, shaped token readers
- 토큰 단위로 읽는다
- 줄 경계와 독립적으로 whitespace를 구분자로 처리한다

### line-oriented readers

- `readLine`
- `readLines(n)`
- `readChars`
- `readWords`
- `readCharGrid(n)`
- `readWordGrid(n)`
- `readRestOfLine`

### exact rule

- `readLine()` 은 **다음 물리적 줄 전체**를 반환한다.
- 직전에 token-oriented read가 수행되었다면, `readLine()` 은 pending line break를 정리한 뒤 다음 물리적 줄을 반환한다.
- 현재 줄의 남은 텍스트가 필요하다면 `readRestOfLine()` 을 사용한다.

즉:

```txt
int n =
string s = stdin.readLine()
```

는 “정수 다음 줄 전체”를 읽는 용도다.

반면 현재 줄의 나머지를 원하면:

```txt
string rest = stdin.readRestOfLine()
```

를 쓴다.

## 17.6 line helpers의 정확한 의미

- `readChars()` : 다음 물리적 줄을 읽어 `char[]` 반환
- `readWords()` : 다음 물리적 줄을 읽어 whitespace split 후 `string[]` 반환
- `readCharGrid(n)` : 다음 `n`개 물리적 줄을 각각 `char[]`로 읽어 `char[][]` 반환
- `readWordGrid(n)` : 다음 `n`개 물리적 줄을 각각 whitespace split 하여 `string[][]` 반환

### lowering contract

reference backend baseline:

- `Console.OpenStandardInput()` + `StreamReader`
- 충분한 buffer size
- token scanner + line-aware handling
- token/line transition contract 준수

---

## 18. 출력 shorthand와 `stdout`

## 18.1 statement shorthand

```txt
= expr
+= expr
```

의미:

- `= expr` -> write without newline
- `+= expr` -> write with newline

### lowering contract

```csharp
stdout.write(expr);
stdout.writeln(expr);
```

statement start에서만 shorthand로 해석한다.

---

## 18.2 explicit `stdout`

```txt
stdout.write(x)
stdout.writeln(x)
stdout.flush()
stdout.lines(xs)
stdout.grid(g)
stdout.join(separator, xs)
```

### 타입과 의미

#### `stdout.write(x)` / `stdout.writeln(x)`

- `x`: printable value
- return: `void`

#### `stdout.flush()`

- return: `void`

#### `stdout.lines(xs)`

- `xs`: iterable of printable values
- return: `void`
- 각 원소를 한 줄씩 출력

#### `stdout.grid(g)`

- `g`: iterable of rows
- 각 row는 printable row value 또는 printable iterable
- return: `void`
- 각 row를 한 줄로 출력

#### `stdout.join(separator, xs)`

이 API는 union type 표기가 아니라 **오버로드 두 개**를 갖는다.

```txt
stdout.join(separator: string, xs: Iterable<Printable>) -> void
stdout.join(separator: char,   xs: Iterable<Printable>) -> void
```

의미:

- `xs`를 `separator`로 이어 붙여 출력
- 줄바꿈은 자동으로 붙지 않음

### lowering contract

- scalar 출력은 direct writer call 우선
- tuple은 small arity specialized rendering 우선
- 1D scalar array/list는 direct joined rendering 우선
- nested enumerable fallback renderer는 가능하면 최소화

### runtime contract

reference backend는 `StreamWriter` 기반 buffered output을 사용한다.

---

## 19. 보간 문자열

```txt
$"answer = {ans}"
$"{min a b} {sum (0..<n -> i do a[i])}"
```

의미:

- C# 스타일 보간 문자열과 호환
- hole 안에는 PSCP expression 허용
- format specifier는 C#과 호환

예:

```txt
$"{value:F2}"
```

### lowering contract

가능한 한 C# interpolated string으로 직접 보존한다.

---

## 20. 튜플, projection, slicing

## 20.1 튜플 생성

```txt
(1, 2)
(a, b, c)
```

## 20.2 tuple projection

```txt
p.1
p.2
p.3
```

의미:

- 1-based tuple element access

### lowering contract

C# tuple의 `.Item1`, `.Item2`, ... 로 lowering한다.

## 20.3 tuple assignment

```txt
(a, b) = (b, a)
(arr[i], arr[j]) = (arr[j], arr[i])
```

가능하면 C# tuple assignment 그대로 유지한다.

## 20.4 slicing / index-from-end

```txt
text[^1]
text[1..^2]
arr[..n]
arr[1..]
arr[..]
```

의미:

- C# style slicing / index-from-end surface

### lowering contract

가능한 한 C# slicing / range/index surface로 직접 보존한다.

---

## 21. conversion keyword family

PSCP는 type keyword를 expression position에서 conversion/parsing function처럼 쓸 수 있게 한다.

핵심:

- `int`
- `long`
- `double`
- `decimal`
- `bool`
- `char`
- `string`

예:

```txt
int "123"
long "1000"
int(true)
bool 0
bool "hello"
string 123
```

### 의미

- string parsing
- numeric conversion
- bool ↔ numeric conversion
- string truthiness
- scalar-to-string conversion

### lowering contract

- string → numeric: direct parse
- numeric → numeric: direct cast/convert
- bool → numeric: `0/1`
- numeric → bool: `!= 0`
- string → bool: empty false / non-empty true
- compile-time constant conversion은 folding 가능

### 이름 충돌 규칙

conversion keyword family는 keyword-owned surface다.  
`int`, `long`, `string` 자체는 ordinary identifier와 충돌하지 않는다.

---

## 22. aggregate family

핵심:

- `min`
- `max`
- `sum`
- `sumBy`
- `minBy`
- `maxBy`
- `chmin`
- `chmax`

## 22.1 call forms

aggregate family는 **세 가지 canonical call form**을 가진다.

1. free call + space-call
2. free call + parenthesized call
3. member-style alias call

예:

```txt
sum arr
sum(arr)
arr.sum()

sumBy arr (x => x * x)
sumBy(arr, x => x * x)
arr.sumBy(x => x * x)
```

이 셋은 semantic level에서 동등하다.

즉 `sum arr` 와 `arr.sum()` 은 동일 의미다.

## 22.2 `min`, `max`

```txt
min a b
max a b c
min(arr)
arr.min()
```

- fixed-arity compare
- iterable aggregation

## 22.3 `sum`

```txt
sum arr
sum (0..<n -> i do score(i))
arr.sum()
```

## 22.4 `sumBy`

```txt
sumBy arr (x => x * x)
arr.sumBy(x => x * x)
```

## 22.5 `minBy`, `maxBy`

```txt
minBy points (p => p.x)
points.minBy(p => p.x)
```

## 22.6 `chmin`, `chmax`

```txt
chmin ref best cand
chmax ref ans value
```

member-style alias는 두지 않는다.

### lowering contract

- small fixed-arity `min/max` -> compare tree
- iterable `min/max/sum` -> direct loop
- `sumBy` -> selector 1회 + direct loop
- `minBy/maxBy` -> best element + best key tracking
- `chmin/chmax` -> direct compare-update rewrite 우선

### 이름 충돌 규칙

user-defined symbol이 `sum`, `min`, `gcd` 같은 intrinsic family name과 충돌하면 user-defined symbol이 우선한다.

---

## 23. math intrinsic family

기본 집합:

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

### source examples

```txt
abs(a - b)
sqrt(dx * dx + dy * dy)
gcd(a, b)
lcm(a, b)
popcount(mask)
bitLength(x)
```

### lowering contract

- `abs` -> `Math.Abs` 또는 direct primitive path
- `sqrt` -> `Math.Sqrt`
- `gcd/lcm/popcount/bitLength` -> direct specialized helper 또는 inline lowering
- hot path에서 generic/dynamic dispatch 금지

### 이름 충돌 규칙

사용자 정의 `gcd`가 있으면 intrinsic `gcd`는 숨겨진다.

즉 generated C#이 `__PscpMath.gcd(...)`를 호출하면 잘못된 lowering이다.

---

## 24. collection helper surface

PSCP는 collection-oriented helper를 가진다.

핵심:

- `map`
- `filter`
- `fold`
- `scan`
- `mapFold`
- `any`
- `all`
- `count`
- `find`
- `findIndex`
- `findLastIndex`
- `sort`
- `sortBy`
- `sortWith`
- `distinct`
- `reverse`
- `copy`
- `groupCount`
- `index`
- `freq`

## 24.1 왜 member-call only인가

aggregate family는 언어 차원의 짧은 free-call style (`sum arr`, `min a b`)을 적극 지원한다.

반면 collection helper는 다음 이유로 **member-call only** 를 canonical form으로 둔다.

1. `map xs f` 형태는 space-call / pipe / ordinary call과의 파싱 모호성이 더 크다.
2. `xs.map(f)` 형태가 pipeline과 receiver 중심 읽기에 더 자연스럽다.
3. aggregate는 “집계 언어 기능”이고, collection helper는 “receiver 변환 연산”이라는 역할 구분이 있다.

따라서:

```txt
xs.map(f)
xs.filter(pred)
xs.fold(seed, f)
```

가 canonical form이다.

## 24.2 generator receiver와 반환 family 규칙

collection helper는 array / list / linked-list 뿐 아니라 **임의의 iterable receiver**, 즉 generator receiver에도 적용될 수 있다.

예:

```txt
(0..<n -> i do i).map(x => x * x)
```

는 유효하다.

### family preservation rule

materializing helper는 다음 container-family 규칙을 가진다.

#### preserve concrete container family when receiver static family is one of:

- `T[]`
- `List<T>`
- `LinkedList<T>`

#### otherwise

- default materialized result family is array

즉:

- array receiver -> array 결과
- List receiver -> List 결과
- LinkedList receiver -> LinkedList 결과
- generator / general iterable receiver -> array 결과

이 규칙은 **container family** 에만 관한 것이다. element type이 바뀌는 것은 허용된다.

예:

- `int[] -> map(x => x.ToString())` 의 결과는 `string[]`
- `List<int> -> scan(...)` 의 결과는 `List<S>`
- generator receiver -> `U[]` / `T[]`

## 24.3 `map`

```txt
xs.map(f)
```

시그니처 개념:

```txt
Iterable<T>.map(f: T -> U) -> Materialized<U>
```

의미:

- 각 원소에 `f`를 적용한 새 materialized collection

## 24.4 `filter`

```txt
xs.filter(pred)
```

시그니처 개념:

```txt
Iterable<T>.filter(pred: T -> bool) -> Materialized<T>
```

의미:

- predicate를 만족하는 원소만 남긴 새 materialized collection

## 24.5 `fold`

```txt
xs.fold(seed, f)
```

시그니처:

```txt
Iterable<T>.fold(seed: S, f: (S, T) -> S) -> S
```

의미:

- 왼쪽에서부터 누적
- 인자 순서는 항상 `(state, item)`

## 24.6 `scan`

```txt
xs.scan(seed, f)
```

시그니처:

```txt
Iterable<T>.scan(seed: S, f: (S, T) -> S) -> Materialized<S>
```

의미:

- 중간 누적 상태 전체를 materialize한다
- 결과는 **seed를 포함**한다
- 결과 길이는 입력 길이 + 1

## 24.7 `mapFold`

```txt
xs.mapFold(seed, f)
```

시그니처:

```txt
Iterable<T>.mapFold(seed: S, f: (S, T) -> (U, S)) -> (Materialized<U>, S)
```

의미:

- 상태를 누적하면서 mapped result도 동시에 만든다
- callback 반환 순서는 `(mappedValue, nextState)`
- 최종 반환은 `(mappedCollection, finalState)`

예:

```txt
let pref, total = arr.mapFold(0, (acc, x) => {
    let next = acc + x
    (next, next)
})
```

이 예시는 prefix sum 배열과 마지막 총합을 동시에 만든다.

## 24.8 `any`

두 overload를 가진다.

```txt
xs.any()          -> bool
xs.any(pred)      -> bool
```

의미:

- `any()` : 시퀀스가 비어 있지 않은가?
- `any(pred)` : predicate를 만족하는 원소가 하나라도 있는가?

## 24.9 `all`

```txt
xs.all(pred) -> bool
```

의미:

- 모든 원소가 predicate를 만족하는가?

## 24.10 `count`

두 overload를 가진다.

```txt
xs.count()        -> int
xs.count(pred)    -> int
```

의미:

- `count()` : 전체 원소 수
- `count(pred)` : predicate를 만족하는 원소 수

## 24.11 `find`

```txt
xs.find(pred) -> T?
```

`v0.6`에서는 정책을 **명시적으로 선택**한다.

### chosen policy

`find(pred)` 는 첫 번째 matching element를 반환하고, 없으면 `null` / `default nullable` 을 반환한다.

즉 source-level contract는 `T?` 이다.

- reference type -> nullable reference
- value type -> `Nullable<T>`

## 24.12 `findIndex`, `findLastIndex`

```txt
xs.findIndex(pred)      -> int
xs.findLastIndex(pred)  -> int
```

정책:

- 못 찾으면 `-1`

## 24.13 `sort`, `sortBy`, `sortWith`

```txt
xs.sort()
xs.sortBy(keySelector)
xs.sortWith(cmp)
```

의미:

- 모두 **non-mutating** helper다
- 정렬된 새 materialized collection을 반환한다
- 원본은 바꾸지 않는다

### selector evaluation policy

`sortBy` 의 selector 평가 횟수와 순서는 **구현 정의**다.

즉 구현은:

- selector를 key-caching 할 수도 있고
- 비교 중 여러 번 재평가할 수도 있다

따라서 selector는 pure function이어야 한다. side effect가 있는 selector에 의존하는 코드는 stable core의 권장 범위를 벗어난다.

### lowering contract

- direct array/list clone + `Array.Sort` / `List.Sort` 등 가능한 fast path 선호
- key caching 최적화는 선택적

## 24.14 `distinct`

```txt
xs.distinct()
```

의미:

- non-mutating
- 중복 제거한 새 materialized collection 반환

### equality rule

`distinct`, `groupCount`, `freq`, `index` 의 key equality는 **`EqualityComparer<T>.Default`** 를 기준으로 한다.

## 24.15 `reverse`

```txt
xs.reverse()
```

의미:

- non-mutating
- 역순 새 materialized collection 반환

## 24.16 `copy`

```txt
xs.copy()
```

의미:

- **shallow copy**
- non-mutating
- 새 materialized collection 반환

## 24.17 `groupCount` / `freq`

```txt
xs.groupCount() -> Dictionary<T, int>
xs.freq()       -> Dictionary<T, int>
```

정책:

- `freq()` 는 `groupCount()` 의 alias
- 값별 등장 횟수 map
- key equality는 `EqualityComparer<T>.Default`

## 24.18 `index`

```txt
xs.index() -> Dictionary<T, int>
```

정책:

- 각 값의 **첫 등장 위치**를 map으로 반환
- 중복이 있으면 첫 등장 위치가 보장된다
- undefined behavior가 아니다
- 좌표압축에서 `xs.sort().distinct().index()` 패턴을 지원

### lowering contract

원칙:

- shape가 단순하면 direct lowering 선호
- 복잡하거나 일반적이면 helper/runtime/library path 허용
- hot path에서 LINQ fallback 남용 금지

---

## 25. comparator와 ordering

## 25.1 `T.asc`, `T.desc`

```txt
int.asc
int.desc
string.desc
```

의미:

- default ordering 기반 ascending / descending comparer

### lowering contract

가능하면 comparer object cache 또는 direct comparer lowering.

## 25.2 `operator<=>(other)`

```txt
operator<=>(other) => this.value <=> other.value
```

의미:

- self type의 default ordering shorthand
- implicit return type은 `int`

### 중요한 계약

`v0.6`에서 이 shorthand는 **.NET ordered API와 호환되도록 lowering되어야 한다.**

즉 최소한 다음 둘 중 하나를 만족해야 한다.

1. generated `IComparable<T>.CompareTo` 구현 생성
2. ordered helper/API가 일관되게 참조하는 comparer surface 생성

reference backend는 가능하면 `IComparable<T>` 호환 lowering을 우선한다.

그렇지 않으면 `.sort()` / `Array.Sort` / `List.Sort` 와의 상호운용성이 깨질 수 있다.

---

## 26. known data structure operator rewrites

이 절은 `v0.6`에서 반드시 완전하게 문서화한다.

### exact-type rule

known DS rewrite는 **정적 타입이 정확히 알려진 BCL known type** 일 때만 적용한다.

즉 arbitrary subclass / wrapper / user-defined duck-typed structure에는 적용하지 않는다.

## 26.1 `List<T>`

```txt
list += value
```

의미:

- `list.Add(value)`

### return value

`List<T>.Add`는 `void`다.

### lowering contract

direct `.Add(value)`

---

## 26.2 `LinkedList<T>`

```txt
list += value
```

의미:

- `list.AddLast(value)`

### lowering contract

direct `.AddLast(value)`

---

## 26.3 `HashSet<T>`

```txt
set += value
set -= value
```

의미:

- `set.Add(value)`
- `set.Remove(value)`

### 중요한 점

`bool` 반환을 그대로 보존한다.

예:

```txt
if not (visited += x) then continue
```

### lowering contract

direct `.Add` / `.Remove`

---

## 26.4 `SortedSet<T>`

```txt
set += value
set -= value
```

의미:

- `set.Add(value)`
- `set.Remove(value)`

### 중요한 점

`bool` 반환을 그대로 보존한다.

### lowering contract

direct `.Add` / `.Remove`

---

## 26.5 `Dictionary<K, V>`

```txt
dict += (key, value)
dict -= key
```

의미:

- `dict.TryAdd(key, value)`
- `dict.Remove(key)`

### 중요한 점

- 두 연산 모두 `bool` 반환을 그대로 보존한다.
- ordinary update는 여전히 pass-through item assignment를 사용한다.

예:

```txt
dict[key] = value
```

이는 rewrite가 아니라 ordinary pass-through assignment다.

### lowering contract

direct `.TryAdd` / `.Remove`

---

## 26.6 `Stack<T>`

```txt
s += value
~s
--s
```

의미:

- `Push`
- `Peek`
- `Pop`

postfix `s--`는 지원하지 않는다.

---

## 26.7 `Queue<T>`

```txt
q += value
~q
--q
```

의미:

- `Enqueue`
- `Peek`
- `Dequeue`

---

## 26.8 `PriorityQueue<TElement, TPriority>`

```txt
pq += (item, priority)
~pq
--pq
```

의미:

- enqueue tuple
- peek item only
- dequeue item only

priority도 필요하면 explicit API 사용:

```txt
pq.TryPeek(out item, out priority)
pq.TryDequeue(out item, out priority)
```

### lowering contract

모든 known DS rewrite는 wrapper object 없이, **직접 underlying .NET call로 lowering** 한다.

---

## 27. `new[n]`, `new![n]`, `Array.zero`

## 27.1 `new[n]`

```txt
int[] arr = new[n]
NodeInfo[] nodes = new[n]
```

의미:

- target type의 element type을 보고 배열 할당

### lowering contract

direct array allocation

---

## 27.2 `new![n]`

```txt
List<int>[] graph = new![n]
Queue<int>[] buckets = new![m]
```

의미:

1. 배열 생성
2. 각 칸을 implicit `new()`로 초기화

### 제약

- known auto-constructible collection element type 대상

### lowering contract

- one array allocation
- one direct init loop
- LINQ initialization 금지

예:

```csharp
List<int>[] graph = new List<int>[n];
for (int i = 0; i < n; i++) graph[i] = new();
```

---

## 27.3 `Array.zero`

```txt
Array.zero(n)
```

의미:

- length `n`의 default-initialized 1D array

### lowering contract

direct array allocation

---

## 28. type declaration과 pass-through 구조

## 28.1 class / struct / record

```txt
class Node {
    int value
}

struct Edge {
    int to
    int w
}

record Point(int X, int Y)
record struct Job(int Id, int Arrival, long Time)
```

## 28.2 base / interface list

```txt
class A : B, IFoo, IBar {
    ...
}

record struct Job(...) : IComparable<Job> {
    ...
}
```

## 28.3 `this`, `base`, `new`

이들은 C#과 같은 pass-through surface다.

```txt
this
base
new T(...)
new()
```

## 28.4 access modifiers

ordinary C# modifier를 그대로 사용한다.

```txt
public int X
private int Y
protected int Z
internal int W
```

---

## 29. ordinary C# pass-through

PSCP는 다음을 적극적으로 pass-through 한다.

- `using`
- `namespace`
- `class`, `struct`, `record`
- generic constraints
- `this`, `base`
- explicit member declarations
- ordinary access modifiers
- nullable marker `?`
- switch-expression surface
- ordinary .NET API calls

### 규칙

pass-through를 PSCP sugar보다 억지로 우선시키지 않는다.  
semantic binding 결과를 기준으로 ordinary symbol resolution을 수행한다.

---

## 30. release / debug 계약

## 30.1 release build 금지 규칙

release build에서 **PSCP가 추가로 주입한 artificial `throw`는 없어야 한다.**

허용:

- user source가 직접 쓴 `throw`
- pass-through C# semantics상 원래 발생 가능한 예외

금지:

- defensive range-step throw
- helper generic fallback이 만들어내는 artificial throw
- transpiler가 편의상 심은 runtime sanity throw

### 허용 대안

- compile-time 진단
- `#if DEBUG` 검사
- `Debug.Assert`

---

## 30.2 fast I/O baseline

reference backend baseline:

- `Console.OpenStandardInput()` + `StreamReader`
- `Console.OpenStandardOutput()` + `StreamWriter`
- 충분한 buffer size
- final `flush`

이것은 `v0.6`의 baseline contract다.

---

## 31. helper emission 정책

## 31.1 원칙

generated program에는 **사용된 helper만** emit 하는 것이 기본 정책이다.

즉 giant runtime helper blob을 매번 통째로 넣지 않는다.

## 31.2 dead using 제거

사용되지 않는 `using`은 제거한다.

## 31.3 hot path와 fallback path 구분

- simple shape -> direct lowering
- rare / escaped / general shape -> helper/runtime 허용

하지만 fallback helper가 hot path 기본 경로가 되면 안 된다.

---

## 32. anti-pattern

`v0.6`에서 피해야 할 대표적인 잘못된 생성 방향:

1. simple numeric range를 helper enumerable로 낮추기
2. shorthand input을 generic `read<T>()` dispatcher로 낮추기
3. fixed-shape input을 LINQ `Select(...).ToArray()`로 읽기
4. generator-fed aggregate를 중간 materialization 후 처리하기
5. known DS rewrite를 wrapper object로 감싸기
6. `new![n]`를 LINQ initialization으로 구현하기
7. release build에 artificial `throw` 남기기
8. discard를 `_ = loopVar;` 같은 의미 없는 문장으로 남기기
9. user-defined symbol보다 intrinsic family 이름을 우선 해석하기
10. local function을 lambda thunk로 변환하기
11. `stdin.readInt()` 같은 canonical explicit API 대신 keyword-colliding source API를 노출하기

---

## 33. 예제

## 33.1 이름 충돌

```txt
int x =
rec int gcd(int a, int b) {
    if b == 0 then a
    else gcd(b, a % b)
}

= gcd(10, x)
```

여기서 `gcd`는 user-defined symbol이므로, intrinsic `gcd`보다 우선한다.

---

## 33.2 HashSet bool 반환

```txt
HashSet<int> visited;
if not (visited += x) then continue
```

---

## 33.3 List / LinkedList append sugar

```txt
List<int> list;
LinkedList<int> linked;

list += 10
linked += 20
```

의미:

- `list.Add(10)`
- `linked.AddLast(20)`

---

## 33.4 `=` 와 `:=`

```txt
rec int find(int x) {
    if x == parent[x] then x
    else parent[x] := find(parent[x])
}
```

이게 권장형이다.

반면:

```txt
rec int find(int x) {
    if x == parent[x] then x
    else parent[x] = find(parent[x])
}
```

는 assignment는 되더라도 value-yielding 의도가 아니므로 경고 대상이며,
block final implicit return 대상이 아니다.

---

## 33.5 로컬 함수 중첩

```txt
int solve(int x) {
    int inner(int y) {
        int deep(int z) {
            z + 1
        }
        deep(y) + x
    }
    inner(x)
}
```

ordinary local function nesting으로 처리해야 한다.

---

## 33.6 collection helper on generator

```txt
let squares = (0..<n -> i do i).map(x => x * x)
```

여기서 receiver는 generator이므로, 결과는 default family rule에 따라 `int[]` 이다.

---

## 34. 적합성

PSCP `v0.6` 구현은 다음을 만족해야 한다.

1. user-defined symbol이 intrinsic보다 우선하는 이름 해석을 보장한다.
2. 이 문서가 정의한 shorthand / intrinsic / helper / pass-through 의미를 보존한다.
3. known DS rewrite를 올바른 underlying .NET call로 lowering한다.
4. `=` 와 `:=`를 의미상 구분한다.
5. local function과 nested local function, contiguous `rec` group mutual recursion을 지원한다.
6. release build에 transpiler-added artificial `throw`를 남기지 않는다.
7. simple range / aggregate / shaped input을 direct lowering 우선 정책으로 처리한다.
8. explicit `stdin` API에 canonical `read*` naming을 사용한다.
9. aggregate family의 free-call/member-call 동등성을 보장한다.
10. collection helper의 시그니처와 반환 family 규칙을 지킨다.
11. `~` 와 prefix `--` 의 타입 기반 rewrite 규칙을 지킨다.
12. pipe와 space-call, 괄호 호출 상호작용 규칙을 지킨다.

---

## 35. 마무리

PSCP `v0.6`의 핵심은 이제 더 분명하다.

- 문법은 짧아야 하고
- intrinsic API는 실전적이어야 하며
- 이름 해석은 일반 언어 관습을 따라야 하고
- 트랜스파일러는 semantic binding 결과를 존중해야 하며
- generated C#은 직접적이어야 한다.

이 문서는 그 기준을 문법/API/lowering을 따로 떼지 않고 한자리에서 설명하기 위해 작성되었다.

