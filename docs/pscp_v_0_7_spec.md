# PSCP v0.7 통합 언어 / API / 트랜스파일 사양서 (초안)

- 상태: **초안(Draft)** — 구현(트랜스파일러 0.6.7)에는 아직 반영되지 않았다.
- 기준 문서: `deprecated/pscp_v_0_6_spec.md`
- 동반 문서: `pscp_v_0_7_changes.md` (v0.6 → v0.7 변경 해설, 항목별 before/after 코드)
- 작성일: 2026-09-26

---

## 0. 문서 안내

### 0.1 목적과 범위

이 문서는 PSCP `v0.7`의 **통합 사양서**다. 다음 셋을 하나의 체계로 함께 정의한다.

1. 언어 문법
2. 내장 API (intrinsic family, helper surface)
3. 트랜스파일(lowering) 계약

PSCP에서는 이 셋이 강하게 결합되어 있다. `int n =` 는 문법이면서 `stdin.readInt()` 의미를 갖고, `list += x` 는 연산자이면서 `List<T>.Add(x)` 로 낮아지는 API 계약이다. 그래서 이 문서는 한 기능을 가능한 한 다음 순서로 함께 설명한다.

1. source surface (쓰는 법)
2. source meaning (의미)
3. API semantics (시그니처, 반환값)
4. lowering contract (생성 C#)
5. 진단 / edge case

### 0.2 규범 용어

이 문서의 문장은 다음 강도를 갖는다.

| 표현 | 의미 |
|---|---|
| **해야 한다 / 반드시** | 적합한 구현의 필수 조건 (MUST) |
| **해서는 안 된다 / 금지** | 적합한 구현이 하면 안 되는 것 (MUST NOT) |
| **권장한다 / 우선한다** | 특별한 이유가 없으면 따라야 하는 품질 기준 (SHOULD) |
| **할 수 있다** | 허용되는 선택 (MAY) |

**의미(semantics) 규칙은 MUST다.** 생성 C#의 모양(예: "direct `for`로 낮춘다")은 별도 표시가 없으면 **SHOULD**다. 즉 lowering 규칙은 성능과 가독성 품질 기준이며, 관측 가능한 의미를 바꾸지 않는 한 다른 모양을 생성해도 적합하다.

### 0.3 진단 등급

- **오류(error)**: 프로그램을 거부한다. C# 코드를 생성하지 않는다.
- **경고(warning)**: 프로그램을 받아들이되 사용자에게 알린다.

필수 진단 목록은 [부록 B](#부록-b-진단-목록)에 있다.

### 0.4 용어

| 용어 | 정의 |
|---|---|
| **stable core** | 이 문서가 의미를 확정한 기능 집합. stable core 밖의 사용은 **오류로 진단**한다. "stable core 밖이지만 동작할 수도 있음" 같은 회색 지대는 두지 않는다. |
| **reference backend** | 저장소의 `Pscp.Transpiler` 구현. 이 문서에서 "reference backend는 …한다"는 설명은 참고 정보이며 규범이 아니다. |
| **생성 C# 버전** | 생성 코드는 **C# 10** 문법 범위 안에 있어야 한다(net6.0 이상에서 컴파일). pass-through 가능한 C# 기능도 이 범위로 제한된다. |
| **Debug / Release** | 생성 프로젝트의 빌드 구성. 두 구성의 차이는 [§29](#29-빌드-모드와-전제조건-위반)에서 정의한다. |
| **전제조건 위반** | 프로그램이 API나 연산의 전제를 어긴 상태(빈 시퀀스의 `min`, 입력 끝에서 `readInt` 등). Debug에서는 반드시 예외로 드러나고, Release에서는 결과가 미정이다. [§29.2](#292-전제조건-위반) 참조. |
| **known DS** | [§26](#26-자료구조-연산자-rewrite)에서 연산자 rewrite 대상으로 지정한 .NET 컬렉션 타입. |
| **tail position** | 함수 본문에서 값이 암묵적으로 반환될 수 있는 위치. [§10.5](#105-꼬리-위치와-암묵적-반환) 참조. |

---

## 1. 설계 목표

1. 문제 풀이 코드를 짧게 쓴다.
2. 의도를 빠르게 드러낸다.
3. 반복되는 입출력 보일러플레이트를 줄인다.
4. range / aggregate / collection / data structure 조작을 압축해 표현한다.
5. C#/.NET 생태계를 유지한다.
6. 문법 설탕의 비용은 런타임이 아니라 트랜스파일러가 감당한다.
7. 생성 C#은 직접적이고 읽을 수 있어야 한다.
8. 이름 해석은 일반 언어 관습을 따른다.
9. **PSCP 진단을 통과한 프로그램은, PSCP 고유 표면 때문에 C# 컴파일 오류를 내서는 안 된다.** 잘못된 코드는 PSCP 위치(줄:열)에서 진단한다. (v0.7 신설, [§34](#34-적합성))

## 2. 비목표

1. 일반 목적 애플리케이션 프레임워크 제공
2. .NET 전체 API를 새 이름으로 재포장하기
3. 모든 C# 기능을 새 PSCP 문법으로 바꾸기
4. 모든 코드를 함수형 스타일로 강제하기
5. 모든 intrinsic을 runtime wrapper abstraction으로 감싸기

따라서 다음 같은 C#/.NET 표면은 PSCP에서도 자연스럽다.

```txt
using System.Collections.Generic
Dictionary<int, List<int>> graph = new()
Math.Max(a, b)
PriorityQueue<int, int> pq = new()
```

---

## 3. 어휘 구조

### 3.1 소스 텍스트와 주석

- 소스는 UTF-8 텍스트다. 줄 끝은 `\n` 또는 `\r\n`이다.
- 주석은 C#과 같다: `// 한 줄`, `/* 여러 줄 */`. 주석은 공백으로 취급한다. 줄 끝 주석은 문장 종료 판정([§4](#4-문장-줄바꿈-블록))에 영향을 주지 않는다.

### 3.2 식별자

- 식별자 규칙은 C#과 같다(유니코드 문자, `_`, 숫자). `@`가 붙은 verbatim 식별자(`@int`)는 **허용하지 않는다**.
- `_` 단독은 discard 토큰이다([§9.7](#97-discard)).
- **`__pscp` 또는 `__Pscp`로 시작하는 식별자는 구현이 생성 코드용으로 예약한다.** 사용자 선언에 쓰면 오류다.

### 3.3 키워드

**예약 키워드** (식별자로 쓸 수 없다):

```txt
let var mut rec
if then else for in do while
break continue return
true false null
and or xor not
class struct record interface enum
ref out in params
new this base
namespace using is as
public private protected internal static readonly const
void int long double decimal bool char string
try catch finally throw
operator where default typeof
```

**문맥 키워드** (특정 위치에서만 키워드, 그 밖에서는 식별자): `when`, `with`, `switch`, `get`, `set`, `init`, `value`.

`match`는 v0.6부터 예약어가 아니다.

### 3.4 리터럴

- 숫자 리터럴은 C# 표기를 따른다: `0x` 16진, `0b` 2진, `_` 자릿수 구분자(`1_000_000`), 정수 접미사 `u`/`l`/`ul`, 실수 접미사 `f`/`d`/`m`. 접미사 없는 정수 리터럴의 타입도 C#과 같다(`int`에 안 들어가면 `uint` → `long` → `ulong`).
- 문자 리터럴 `'a'`, `'\n'`, 문자열 리터럴 `"..."`, verbatim 문자열 `@"..."`, 보간 문자열 `$"..."` / `$@"..."`는 C#과 같다. raw string(`"""`)은 C# 11 기능이므로 지원하지 않는다.
- `..`는 소수점보다 우선한다. 따라서 `1..10`은 `1` `..` `10`이다.

### 3.5 튜플 projection 뒤의 숫자

튜플 projection(`.1`, `.2`, …) 바로 뒤의 숫자는 정수로만 읽는다. 따라서 `p.1.2`는 `(p.1).2`이며 실수 `1.2`가 아니다.

---

## 4. 문장, 줄바꿈, 블록

### 4.1 기본 규칙

- 문장은 줄바꿈으로 끝난다.
- `;`는 선택이다. 한 줄에 여러 문장을 쓸 때 구분자로 쓴다. 문장 끝의 `;`는 있어도 없어도 같다.

```txt
var x = 0; x += 1; += x
Queue<int> q          // `Queue<int> q;` 와 같다
```

### 4.2 줄 이어짐

다음 경우에는 줄바꿈이 문장을 끝내지 **않는다**.

1. **열린 괄호 안**: 닫히지 않은 `(` 또는 `[` 안의 줄바꿈. (`{`는 블록이므로 해당하지 않는다. 블록 안에서는 줄바꿈이 다시 문장 구분자다.)
2. **줄이 이어짐 토큰으로 끝날 때**: 줄의 마지막 토큰이 다음 중 하나이면 다음 줄로 이어진다. 토큰이 **그 역할로** 쓰였을 때만 해당한다. 예를 들어 제네릭 인자 목록을 닫는 `>`, nullable 표시 `?`는 이항 연산자가 아니므로 해당하지 않는다.
   - `=`를 제외한 이항 연산자: `+ - * / % << >> & | ^ && || and or xor == != < <= > >= <=> ?? |> <| .. ..< ..=`
   - 대입 연산자 중 `:=`, `+=`, `-=`, `*=`, `/=`, `%=`, `&=`, `|=`, `^=`, `<<=`, `>>=`, `??=`
   - `,` `.` `?.` `=>` `->`, 조건 연산자의 `?`와 `:`
   - 키워드 `then`, `else`, `do`, `in`
3. **다음 줄이 이어짐 토큰으로 시작할 때**: 다음 비어 있지 않은 줄의 첫 토큰이 다음 중 하나이면 앞 문장에 이어진다.
   - `.` `?.` `|>` `<|` `&&` `||` `and` `or`
   - `else` (직전 `if`에 이어진다)
   - `{` (직전 문장이 본문을 기다리는 머리일 때만: `if cond`, `while cond`, `for x in src`, 함수 선언 머리, 타입 선언 머리)

```txt
let total = xs
    .filter(x => x > 0)
    .map(x => x * 2)
    .sum()

let best = scores
    |> filter(s => s >= limit)
    |> max

if b == 0 then a
else gcd(b, a % b)

if ok then
    += "YES"
else
    += "NO"

if s > 0
{
    += s
}
```

### 4.3 줄 이어짐이 아닌 것

다음은 이어짐으로 해석하지 않는다. 문법적으로 다른 의미가 있기 때문이다.

- **줄 끝의 `=`**: 선언에서는 입력 shorthand([§17.1](#171-선언-기반-입력-shorthand))다. 선언이 아닌 곳에서 줄 끝 `=`는 오류다("대입의 오른쪽이 없다. 줄을 나누려면 괄호로 감싼다").
- **줄 시작의 `=`, `+=`**: 출력 shorthand([§18.1](#181-출력-shorthand))다.
- **줄 시작의 `+`, `-`, `*` 등 그 밖의 이항 연산자**: 새 문장의 시작이다. 따라서 `- b`는 앞 줄에 이어지지 않는다. 결과를 쓰지 않는 단항식 문장이 되므로 경고 대상이다.

### 4.4 블록

PSCP는 들여쓰기가 아니라 **중괄호**를 쓴다. 들여쓰기는 의미가 없다.

```txt
if x < 0 {
    x = -x
}
```

lowering: 중괄호 블록은 가능한 한 C# 블록으로 그대로 보존한다.

---

## 5. 이름 해석

### 5.1 비한정 이름의 우선순위

비한정 이름(unqualified name)은 다음 순서로 찾는다. 앞 단계에서 찾으면 뒤 단계는 보지 않는다.

1. **lexical scope**: 현재 블록부터 바깥 블록 방향으로, 가장 가까운 scope에 선언된 이름. 한 scope에 선언된 변수, 매개변수, 로컬 함수, 패턴 변수가 모두 이 단계에 속한다.
2. **현재 타입의 멤버**: 타입 선언 안이라면 그 타입과 기반 타입의 멤버.
3. **프로그램 scope**: 최상위 함수, 최상위 변수, 최상위 타입 ([§7.3](#73-프로그램-scope)).
4. **외부 이름**: `using`, `using static`, namespace를 통해 들어온 이름.
5. **PSCP intrinsic**: `stdin`, `stdout`, aggregate family, math family.

같은 scope 안에서 변수와 함수가 같은 이름을 가지면 오류다.

트랜스파일러는 이 순서를 임의로 바꿔서는 안 된다.

### 5.2 멤버 이름의 해석

`recv.name(args)` 형태의 멤버 호출은 다음 순서로 찾는다.

1. `recv`의 정적 타입(과 기반 타입)의 실제 멤버
2. scope 안의 확장 메서드 (사용자 선언 또는 `using`)
3. PSCP intrinsic member alias (aggregate member-style alias, collection helper)

따라서 사용자 타입이 `count()`라는 메서드를 가지면 `obj.count()`는 그 메서드를 부른다.

**비한정 이름의 shadowing은 member alias에 영향을 주지 않는다.** 다음 코드에서 `var sum = 0`은 비한정 `sum`만 가리고, `arr.sum()`은 여전히 intrinsic이다.

```txt
var sum = 0
for i in 0..<n do sum += a[i]
let check = a.sum()     // intrinsic sum. 지역 변수 sum과 무관
```

### 5.3 intrinsic shadowing

사용자 선언이 intrinsic 이름을 가리면, 그 scope에서 비한정 intrinsic은 보이지 않는다.

```txt
rec int gcd(int a, int b) {
    if b == 0 then a
    else gcd(b, a % b)
}

= gcd(10, x)    // 사용자 gcd. intrinsic gcd가 아니다
```

올바른 lowering은 `gcd(...)`이다. `__PscpMath.gcd(...)`는 잘못된 lowering이다.

### 5.4 문법이 소유한 표면과 생성 이름

선언 기반 입력 shorthand(`int n =`)와 문장 기반 출력 shorthand(`= x`, `+= x`)는 identifier가 아니라 **문법**이다. 그러므로 사용자가 `stdin`, `stdout`이라는 이름을 선언해도 shorthand의 의미는 바뀌지 않는다.

이를 보장하기 위해 트랜스파일러는 다음을 지켜야 한다.

- 생성 코드가 쓰는 모든 식별자(입출력 객체, helper 클래스, 임시 변수, 진입점 클래스)는 **예약 접두사 `__pscp` / `__Pscp`를 쓰거나, 사용자 이름과 충돌하지 않음이 보장된 이름**이어야 한다.
- 사용자 선언 이름은 생성 C#에서 **그대로 유지**해야 한다(가독성). 사용자 이름이 C# 진입점 요구 사항과 충돌하면(예: 최상위 함수 `Main`), 바꾸는 쪽은 생성 코드다.

```txt
let stdin = 3        // 사용자 변수
int n =              // 여전히 입력. 사용자 stdin과 무관
+= n + stdin         // 사용자 stdin(3)을 더한다
```

가능한 생성 C#:

```csharp
const int stdin = 3;
int n = __pscp_stdin.readInt();
__pscp_stdout.writeln(n + stdin);
```

### 5.5 트랜스파일러 의무

트랜스파일러는 "이 이름은 intrinsic이니까 special lowering"을 해서는 안 된다. 반드시 **semantic binding 결과**를 먼저 보고, 그 binding이 intrinsic symbol일 때만 intrinsic lowering을 적용해야 한다.

---

## 6. pass-through와 PSCP 고유 표면

### 6.1 pass-through

PSCP가 C# 의미를 그대로 유지하는 표면이다. 생성 C# 버전(C# 10) 범위로 제한된다.

- `using`, `using static`, `namespace`
- `class`, `struct`, `record`, `record struct`, `interface`, `enum`
- generic 선언과 constraint (`where T : IComparable<T>`)
- `new()`, `new T(...)`, object/collection initializer, `this`, `base`
- nullable marker `?`, null 연산자 `?.`, `?[]`, `??`, `??=`, null-forgiving `x!`
- `is` / `as` / 캐스트 `(T)x` / 패턴 (문맥 규칙은 [§13.9](#139-패턴-문맥))
- switch 식 (`x switch { ... }`, arm은 `,` 또는 줄바꿈으로 구분. 패턴은 C# 그대로, `when` guard와 arm 결과는 PSCP 식)
- `with` 식
- `try` / `catch` / `finally` / `throw`
- C 스타일 `for (init; cond; step)`
- 일반 .NET 멤버 접근과 호출, 접근 한정자

### 6.2 PSCP 고유 표면

- `let`, `var`, `mut`, `rec`
- 선언 기반 입력 shorthand, 문장 기반 출력 shorthand
- `[]` materialized collection, `()` generator, `->` fast iteration
- range (`..`, `..<`, `..=`, stepped)
- `:=`
- `new[n]`, `new[n][m]`, `new![n]`
- space-call, pipe (`|>`, `<|`)
- aggregate family, math family, collection helper, conversion keyword
- comparator sugar (`T.asc`, `T.desc`, `operator<=>`, `<=>`)
- known DS operator rewrite
- tuple projection `.1`, `.2`, …
- 암묵적 반환

### 6.3 지원하지 않는 C# 문장

다음 C# 문장은 PSCP 키워드와 충돌하거나 PSCP에 같은 기능의 표면이 있으므로 **지원하지 않는다**(오류). 진단 메시지는 대안을 제시해야 한다.

| C# 문장 | 이유 | PSCP 대안 |
|---|---|---|
| `foreach (var x in xs)` | `for ... in`과 중복 | `for x in xs { }` 또는 `xs -> x { }` |
| `do { } while (c);` | `do`는 PSCP 키워드 | `while true { ...; if not c then break }` |
| `switch (x) { case ...: }` 문장 | `:` 레이블 문법 충돌 | switch 식, 또는 `if` / `else if` 체인 |
| `goto`, 레이블 | stable core 밖 | 함수 분리, `break` / `continue` |
| `using var`, `lock`, `unsafe`, `fixed`, `async`/`await`, `yield` | PS 문맥 밖 | — |

---

## 7. 프로그램 구조

### 7.1 최상위 구성 요소

하나의 PSCP 파일에는 다음이 섞여 있을 수 있다.

- `using`, `namespace`
- 타입 선언
- 최상위 함수 선언
- 최상위 문장 (변수 선언 포함)

### 7.2 실행 모델

최상위 문장은 **파일에 쓰인 순서대로** 한 번 실행된다. 최상위 함수와 타입 선언은 실행되는 문장이 아니라 선언이다.

최상위 `return`은 프로그램을 정상 종료한다(출력 flush가 일어난다, [§30.3](#303-출력-버퍼와-종료)).

### 7.3 프로그램 scope

최상위에 선언된 함수, 변수, 타입은 **프로그램 scope**에 속한다.

- 최상위 함수는 선언 위치와 관계없이 파일 어디서나 부를 수 있다(전방 참조 허용).
- 최상위 함수 본문은 **선언 위치와 관계없이** 모든 최상위 변수를 참조할 수 있다.
- 최상위 변수의 값은 그 선언 문장이 실행될 때 초기화된다. **선언 문장이 실행되기 전에 읽으면 그 타입의 기본값**(`0`, `false`, `null`)이다.
- 트랜스파일러는 최상위 문장이 (직접) 부른 함수가 아직 선언 문장이 실행되지 않은 최상위 변수를 읽는 경우를 **경고해야 한다**.

```txt
+= solve(3)              // 경고: solve가 읽는 helper는 아직 초기화되지 않았다 (값 0)
int solve(int x) { x * helper }
let helper = 10
```

최상위 문장 안의 블록(예: `for` 본문)에 선언된 변수와 함수는 프로그램 scope가 아니라 그 블록의 lexical scope에 속한다.

### 7.4 타입 선언에서의 가시성

타입 선언(class, struct, record) 안의 코드는 프로그램 scope 중 다음만 볼 수 있다.

- 최상위 타입
- **컴파일 시점 상수인 최상위 `let` 바인딩** (예: `let MOD = 1_000_000_007`)

컴파일 시점 상수가 아닌 최상위 변수나 최상위 함수를 타입 안에서 참조하면 오류다.

```txt
let MOD = 1_000_000_007

record struct ModInt(long V) {
    ModInt add(ModInt o) { new ModInt((V + o.V) % MOD) }   // OK: MOD는 상수
}
```

### 7.5 lowering contract

reference backend는 synthetic program class 하나를 만든다.

- 최상위 문장은 `Run()` 메서드 본문이 된다.
- 최상위 함수는 program class의 static 메서드가 된다.
- 최상위 함수가 참조하는 최상위 변수는 program class의 static 필드가 된다. 그렇지 않은 최상위 변수는 `Run()`의 지역 변수로 남는다.
- 타입에서 참조하는 최상위 상수는 타입에서 접근 가능한 `const`로 생성한다.
- 진입점은 다음 형태를 권장한다.

```csharp
public static void Main()
{
    Run();
    __pscp_stdout.flush();
}
```

`try/finally`로 flush를 강제하는 형태는 기본 생성으로 쓰지 않는다([§30.3](#303-출력-버퍼와-종료)).

생성 이름은 [§5.4](#54-문법이-소유한-표면과-생성-이름)를 따른다.

---
## 8. 타입

### 8.1 기본 타입

`int`, `long`, `double`, `decimal`, `bool`, `char`, `string`.

C#의 다른 숫자 타입(`byte`, `short`, `uint`, `ulong`, `float` 등)도 pass-through로 쓸 수 있다. 다만 입력 shorthand, 출력 렌더링, aggregate/math family가 보장하는 대상은 위 일곱 타입이다.

산술 규칙은 C#과 같다.

- 정수 나눗셈은 0 방향으로 버린다. `%`의 부호는 피제수를 따른다. 내림/올림 나눗셈은 `floor(a, b)` / `ceil(a, b)`를 쓴다([§23](#23-math-family)).
- 정수 오버플로는 wrap-around(unchecked)다. PSCP는 일반 산술에 검사를 추가하지 않는다. PSCP 고유 연산의 오버플로 규칙은 해당 절에서 정한다.
- `char + int`의 결과는 `int`다. `char`가 필요하면 `char (c + 1)`로 변환한다([§21](#21-변환-키워드)).

### 8.2 튜플, 배열, generic, nullable

```txt
(int, int)
(long, int, string)
int[]
int[][]
(string, int)[]
List<int>
Dictionary<int, string>
string?
int?
```

- 튜플 타입, 배열 타입, generic 타입과 constraint, nullable marker `?`는 C# surface를 그대로 따른다.
- 다차원 배열 `int[,]`는 pass-through로 쓸 수 있지만 입력 shorthand, `new[n]`, 출력 렌더링의 대상은 jagged 배열(`int[][]`)이다.

---

## 9. 선언과 가변성

### 9.1 바인딩 형태

| 형태 | 타입 | 가변 |
|---|---|---|
| `let x = e` | 추론 | 불변 |
| `var x = e` | 추론 | 가변 |
| `T x = e` | 명시 | 불변 |
| `mut T x = e` | 명시 | 가변 |

```txt
let MOD = 1_000_000_007
var count = 0
int n = 3
mut long ans = 0
```

### 9.2 불변성의 의미와 진단

- 불변 바인딩은 **다시 대입하지 않겠다는 선언**이다. 불변성은 얕다. 바인딩이 가리키는 배열의 원소나 컬렉션의 내용을 바꾸는 것은 불변성 위반이 아니다.

  ```txt
  let arr = new int[3]
  arr[0] = 5         // OK: 원소 변경
  arr = new int[4]   // 경고: 불변 바인딩 재대입
  ```

- 불변 바인딩을 수정하면(대입, 복합 대입, `++`/`--`, `ref`/`out` 전달) **경고**다. 오류가 아니다.
- 경고가 난 바인딩은 생성 C#에서 일반 가변 변수로 낮춘다. `const`나 `readonly`로 낮춰서는 안 된다. 그래야 생성 C#이 컴파일된다.
- 입력 shorthand로 선언한 바인딩(`int n =`)도 같은 규칙을 따른다. 입력받은 값을 고칠 계획이면 `mut int n =`으로 선언한다.

### 9.3 초기화 없는 선언

| 형태 | 의미 | 생성 C# |
|---|---|---|
| `mut T x` | 기본값으로 초기화된 가변 변수 | `T x = default;` |
| `T[n] a` | 길이 `n`, 기본값으로 채운 배열 | `T[] a = new T[n];` |
| `T[n][m] g` | `n × m` jagged 배열 | 할당 + 행 할당 loop |
| `C x` (C가 known auto-constructible 타입) | 빈 컬렉션 | `C x = new();` |

known auto-constructible 타입은 다음과 같다: `List<T>`, `LinkedList<T>`, `Queue<T>`, `Stack<T>`, `HashSet<T>`, `SortedSet<T>`, `Dictionary<K, V>`, `SortedDictionary<K, V>`, `PriorityQueue<TElement, TPriority>`.

순서를 쓰는 컬렉션(`SortedSet`, `SortedDictionary`, `PriorityQueue`)은 `new()` 대신 PSCP 기본 순서의 비교자를 넘겨 생성한다([§25.4](#254-net-정렬-컬렉션과-문자열)).

위에 해당하지 않는 초기화 없는 불변 선언(`int x`, `string s`)은 **오류**다. 값을 한 번도 줄 수 없기 때문이다.

```txt
mut int best
int[n] dist
bool[n][m] seen
List<int> order
PriorityQueue<int, long> pq
```

문장 끝의 `;`는 선택이다. `List<int> list;`와 `List<int> list`는 같다.

### 9.4 다중 이름 선언

| 형태 | 의미 |
|---|---|
| `T a, b, c =` | 입력 shorthand. `T` 값 3개를 차례로 읽는다 ([§17.1](#171-선언-기반-입력-shorthand)) |
| `T a, b = e` | `e`를 분해해 각각 `T` 타입으로 선언한다. `e`는 원소 2개로 분해 가능해야 한다 |
| `let a, b = e` | `e`를 분해해 타입을 추론해 선언한다 |
| `var a, b = e` / `mut T a, b = e` | 가변 버전 |

```txt
int n, m =
let suffix, rank = makeSuffix(s)
let pref, total = arr.mapFold(0, (acc, x) => (acc + x, acc + x))
```

C# 형태 `int a = 1, b = 2`처럼 **이름마다 `=`가 있으면** 각각 독립된 선언이다(`int a = 1`, `int b = 2`). 첫 이름 바로 뒤에 `=`가 오는지로 구별하므로 분해 선언 `int a, b = e`와 모호하지 않다. 이름마다 초기화하는 형태에서 일부 이름만 초기화식이 없으면(`int a = 1, b`) [§9.3](#93-초기화-없는-선언) 규칙을 따른다.

### 9.5 구조 분해

| 형태 | 의미 |
|---|---|
| `(int x, int y) = e` | 구조 분해 선언 (C#과 같음) |
| `(x, y) = e` | 기존 변수에 구조 분해 대입 |
| `(a, b) = (b, a)` | swap |
| `(arr[i], arr[j]) = (arr[j], arr[i])` | 원소 swap |

한 괄호 안에서 선언과 기존 변수를 섞은 `(int x, y) = e`는 오류다.

lowering: 가능하면 C# deconstruction으로 그대로 보존한다. 불가능하면 임시값을 만든 뒤 원소 접근으로 분해할 수 있다.

### 9.6 const / readonly lowering

불변 바인딩이 수정되지 않고 초기화식이 컴파일 시점 상수면, 트랜스파일러는 다음을 생성해야 한다.

| 위치 | 조건 | 생성 C# |
|---|---|---|
| 지역 | 컴파일 시점 상수이고 타입이 숫자/`char`/`bool`/`string` | `const` |
| 타입 멤버 | 컴파일 시점 상수 | `const` |
| 타입 멤버, static | 상수가 아님 | `static readonly` |
| 타입 멤버, instance | 생성자나 초기화식에서만 대입 | `readonly` |

```txt
let MOD = 1000000007
let NL = '\n'
```

```csharp
const int MOD = 1000000007;
const char NL = '\n';
```

### 9.7 discard

`_`는 discard다. 다음 위치에 쓸 수 있다.

- 선언/대입 대상: `_ = f()`, `let _ = g()`
- 구조 분해 대상: `(a, _, c) = foo()`
- 람다 매개변수: `_ => 1`, `(a, _) => a`
- 순회 binding: `0..<t -> _ { ... }`, `for _ in 0..<m { ... }`
- `out _`

lowering: 순회 binding의 discard는 이름 없는 synthetic loop 변수로 낮춘다. `_ = __item0;` 같은 의미 없는 문장을 만들어서는 안 된다.

---

## 10. 함수

### 10.1 선언 형태

```txt
int add(int a, int b) {
    a + b
}

int sq(int x) => x * x

void log(string msg) {
    += msg
}

T id<T>(T x) {
    x
}

T maxOfTwo<T>(T a, T b) where T : IComparable<T> {
    if a.CompareTo(b) >= 0 then a else b
}

rec int fact(int n) {
    if n <= 1 then 1 else n * fact(n - 1)
}
```

- 반환 타입이 앞에 오는 C# 형태를 쓴다. `rec`은 반환 타입 앞에 붙인다.
- 식 본문(`=> expr`)을 쓸 수 있다.
- 매개변수 한정자 `ref`/`out`/`in`/`params`와 기본값은 C#과 같다.

### 10.2 함수의 가시성

- 한 블록에 선언된 함수는 **그 블록 전체**에서 보인다. 선언보다 앞에서 불러도 된다.
- 최상위 함수는 프로그램 scope에 속한다([§7.3](#73-프로그램-scope)).
- 로컬 함수 본문은 바깥 scope의 변수를 capture할 수 있다. 단, **로컬 함수 선언보다 앞에 선언된 변수만** 참조할 수 있다(C#과 같다). 최상위 함수가 최상위 변수를 참조하는 경우는 [§7.3](#73-프로그램-scope)을 따른다.
- 로컬 함수는 어느 블록에나 선언할 수 있다: 일반 블록, `if`/`else` 블록, `while`/`for` 본문, `->` 본문, 다른 함수 본문(중첩).

```txt
for i in 0..<n {
    int helper(int x) {
        x + i
    }
    += helper(a[i])
}
```

### 10.3 `rec`

`rec`은 "이 함수는 재귀한다"는 표시이며, 트랜스파일러가 검사한다.

- 문장 문맥의 함수(최상위 함수, 로컬 함수)끼리의 **호출 그래프에서 순환에 속하는 함수**는 모두 `rec`을 붙여야 한다. 자기 자신을 직접 부르는 함수와, 상호 재귀 그룹(호출 그래프의 강연결 요소)의 모든 함수가 여기에 해당한다. 빠뜨리면 **오류**다.
- 상호 재귀 그룹은 호출 그래프에서 자동으로 결정된다. 선언이 연속해 있을 필요가 없다.
- 재귀하지 않는 함수에 `rec`을 붙이면 **경고**다.
- 타입 멤버(메서드)는 C# 멤버 의미를 따르며 `rec` 없이 재귀할 수 있다. 타입 멤버에 `rec`을 붙이면 경고(효과 없음)다.
- delegate나 람다를 거친 간접 호출은 호출 그래프에 넣지 않는다.

```txt
rec bool isEven(int n) {
    if n == 0 then true else isOdd(n - 1)
}

int unrelated() { 42 }

rec bool isOdd(int n) {        // 떨어져 있어도 같은 상호 재귀 그룹
    if n == 0 then false else isEven(n - 1)
}
```

`rec`은 lowering에 영향을 주지 않는다.

### 10.4 lowering contract

트랜스파일러는 로컬 함수를 가능한 한 **C# local function**으로 그대로 보존해야 한다(SHOULD). 다음은 금지다.

- 일반 로컬 함수를 lambda delegate로 바꾸기
- expression thunk helper로 감싸기
- capture 의미를 깨는 hoisting

### 10.5 꼬리 위치와 암묵적 반환

#### tail position

반환 타입이 `void`가 아닌 함수, 로컬 함수, 값을 반환하는 블록 람다의 본문에서 다음 위치를 **tail position**이라 한다.

1. 본문 블록의 마지막 문장
2. tail position에 있는 중첩 블록 `{ ... }`의 마지막 문장
3. tail position에 있고 `else`가 있는 `if` 문의 각 분기(블록이면 그 마지막 문장, `then`/`else` 형태면 그 문장). `else if` 체인도 같다.

`while`, `for`, `->`, `try` 등은 tail position을 전파하지 않는다.

#### 반환 대상 식

tail position의 **식 문장**은 다음을 제외하면 모두 암묵적으로 반환된다.

- 일반 대입 `x = e`
- 일반 복합 대입 `x += e`, `x -= e` 등 (단, known DS rewrite는 호출이므로 제외되지 않는다)
- 일반 증감 `x++`, `++x`, `x--`, `--x` (단, known DS의 `--q`는 호출이므로 제외되지 않는다)

**호출 식도 반환 대상이다.** `foo(x)`, `obj.method(y)`, `inner(x)`가 tail position에 있으면 그 값이 반환된다.

```txt
int solve(int x) {
    int inner(int y) {
        int deep(int z) { z + 1 }
        deep(y) + x
    }
    inner(x)                 // 반환된다
}

int sign(int v) {
    if v > 0 {
        1
    } else if v < 0 {
        -1
    } else {
        0
    }
}

bool mark(int v) {
    visited += v             // HashSet.Add 호출 → bool 반환
}

int next() {
    --queue                  // Queue.Dequeue 호출 → 값 반환
}

rec int find(int x) {
    if x == parent[x] then x
    else parent[x] := find(parent[x])   // := 는 값을 내는 대입
}
```

#### 진단

- 반환 타입이 `void`가 아닌 함수에서, 어떤 실행 경로의 끝이 `return`도 아니고 반환 대상 식도 아니면 **오류**다.
- 그 끝이 `x = e` 형태면 오류 메시지에 `:=` 사용을 제안해야 한다.
- tail position의 식 타입이 `void`면 오류다(예: `List<T>`의 `list += x`).
- `void` 함수의 마지막 식 문장은 그냥 실행되고 값은 버려진다.

#### lowering contract

암묵적 반환은 생성 C#에서 명시적인 `return expr;`로 낮춘다.

### 10.6 람다

```txt
x => x + 1
(acc, x) => acc + x
(int a, int b) => a * b
_ => 1
(acc, x) => {
    let y = acc + x
    y % mod
}
```

- 블록 람다의 반환은 [§10.5](#105-꼬리-위치와-암묵적-반환)를 따른다.
- 람다는 space-call 인자로 쓸 때 괄호로 감싸야 한다: `sumBy arr (x => x * x)`.

---

## 11. 호출

### 11.1 괄호 호출

```txt
f(x)
g(x, y)
obj.method(a, b)
```

C# 호출과 같다.

### 11.2 space-call

PSCP는 괄호 없는 호출(application)을 허용한다.

```txt
f x
g x y
sum arr
min a b
f (a + b) x
sum (0..<n -> i do score(i))
```

#### 형태

```txt
head group1 group2 ... groupK      (K ≥ 1, 각 group 사이는 공백)
```

- **head**: 이름, 멤버 접근 경로(`a.b.f`), 변환 키워드(`int`, `long`, …), 또는 괄호로 감싼 식. head는 **호출 가능**해야 한다(함수, 메서드 그룹, delegate 타입 값, intrinsic family, 변환 키워드). 호출할 수 없는 값이 head 자리에 오면 오류다.
- **group**은 둘 중 하나다.
  1. **원자(atom)**: 리터럴, 이름, `this`, `[...]` 컬렉션 식, 보간 문자열, `new` 식, generator `(src -> x do e)`, 그리고 여기에 공백 없이 붙은 후위 연산(`.m`, `.1`, `[i]`, `(args)`, `?.m`, `!`). 앞에 `ref`/`out`/`in`을 붙일 수 있다.
  2. **괄호 그룹**: `(e1, e2, ..., en)` (n ≥ 1).

#### 인자 결합 규칙

호출의 인자 목록은 **모든 group의 원소를 차례로 이어 붙인 것**이다. 괄호 그룹은 그 안의 원소들, 원자는 원소 하나를 기여한다.

| source | 의미 |
|---|---|
| `f x y` | `f(x, y)` |
| `f (a, b)` | `f(a, b)` |
| `min (a, b)` | `min(a, b)` |
| `f (a + b) x` | `f(a + b, x)` |
| `f x (y, z)` | `f(x, y, z)` |
| `f ((a, b))` | `f((a, b))` — 튜플 하나를 넘길 때는 괄호를 두 겹으로 쓴다 |
| `f g x` | `f(g, x)` — 중첩 호출은 `f (g x)` |
| `f p.1` | `f(p.1)` — 후위 연산은 원자에 붙는다 |
| `f s.Length` | `f(s.Length)` |

공백 없이 붙은 괄호는 C# 후위 호출이다. `f(a)(b)`는 `f(a)`의 결과를 `b`로 호출한다. 반면 `f (a) (b)`는 `f(a, b)`다.

#### chain의 끝

chain은 줄바꿈이나, group을 시작할 수 없는 토큰에서 끝난다: 이항 연산자, `)`, `]`, `}`, `,`, `;`, `=`, `:`, `?`, `=>`, `->`, `then`, `else`, `do`, `and`, `or`, `xor`, `|>`, `<|`, range 연산자.

#### 결합 세기

application은 모든 단항/이항 연산자보다 강하게 결합한다.

| source | 의미 |
|---|---|
| `f x + 1` | `(f x) + 1` |
| `-f x` | `-(f x)` |
| `not f x` | `not (f x)` |
| `f x == g y` | `(f x) == (g y)` |

### 11.3 부호와 space-call

원자는 `-`, `+`, `!`, `not`, `~`, `++`, `--`로 **시작할 수 없다**. 따라서 공백 뒤의 `-`는 항상 이항 뺄셈이다.

| source | 의미 |
|---|---|
| `f -1` | `f - 1` |
| `f - 1` | `f - 1` |
| `min a -1` | `(min a) - 1` |
| `f (-1)` | `f(-1)` |
| `min a (-1)` | `min(a, -1)` |

뺄셈의 왼쪽이 함수(메서드 그룹)이거나, 인자가 모자란 application이면 **오류**이며, 진단은 `f (-1)` 형태를 제안해야 한다.

```txt
+= f -1        // 오류: 함수 f에서 1을 뺄 수 없습니다. 음수 인자는 f (-1)
```

### 11.4 `ref`, `out`, `in`

```txt
foo(ref x, out y, in z)
foo(out int a, out int b)
foo(out _, ref arr[i])

foo ref x out y in z
foo out int a out int b
chmin ref best cand
```

lowering: C# modified argument를 그대로 보존한다.

### 11.5 선언인가 호출인가

문장 시작의 `A b` 형태(이름 두 개)는 binding 단계에서 결정한다.

- `A`가 타입이면 **선언**이다 (`Node root` → 초기화 없는 선언 규칙 [§9.3](#93-초기화-없는-선언)).
- `A`가 호출 가능하면 **space-call 식 문장**이다.
- 둘 다 아니면 오류다.

`A<...> b`, `A[] b`, `A[n] b`, `(A, B) b`처럼 타입 문법이 분명한 형태는 항상 선언이다.

---

## 12. 제어 흐름

### 12.1 `if`

```txt
if cond {
    ...
} else if other {
    ...
} else {
    ...
}

if bad then continue
if ok then return ans else return -1

if ok then
    += "YES"
else
    += "NO"
```

- `then`, `else`, `do`는 **바로 다음 문장 하나**에 결합한다. 그 문장은 같은 줄에 있어도 되고 다음 줄에 있어도 된다([§4.2](#42-줄-이어짐)). 여러 문장이 필요하면 중괄호를 쓴다.
- `else`는 다음 줄 맨 앞에 올 수 있다.
- dangling else: `else`는 항상 **가장 가까운 짝 없는 `if`**에 결합한다. `if a then if b then c else d`는 `if a then (if b then c else d)`다.

#### `if` 식

식 위치에서 `if c then e1 else e2`는 값을 낸다. 식 위치에서는 `else`가 필수다.

```txt
let label = if score >= 90 then "A" else if score >= 80 then "B" else "C"
```

lowering: 조건 연산자 `c ? e1 : e2`로 낮춘다.

### 12.2 `while`

```txt
while cond {
    ...
}
while cond do x += 1
```

### 12.3 `for ... in ...`

```txt
for i in 0..<n {
    ...
}
for i in 0..<n do sum += a[i]
for x in xs do += x
for (u, v) in edges do addEdge(u, v)
```

binding 형태와 의미는 fast iteration([§16.4](#164-fast-iteration))과 같다.

### 12.4 C 스타일 `for`

```txt
for (int i = 0; i * i <= n; i++) {
    ...
}
```

C#과 같다(pass-through). 괄호가 필요하다.

`for (`로 시작하는 두 형태는 다음으로 구별한다. 괄호 안이 binding 목록(`(u, v)`)이고 `)` 바로 뒤에 `in`이 오면 구조 분해 `for ... in`([§12.3](#123-for--in-))이다. 그 밖은 C 스타일 `for`다.

### 12.5 `break`, `continue`, `return`

```txt
break
continue
return x
return
```

### 12.6 `try` / `catch` / `finally` / `throw`

C#과 같다(pass-through).

```txt
try {
    ...
} catch (FormatException e) {
    ...
} finally {
    ...
}
throw new InvalidOperationException("bad state")
```

---

## 13. 연산자

### 13.1 우선순위 표 (강함 → 약함)

| 순위 | 분류 | 연산자 | 결합 |
|---|---|---|---|
| 1 | 후위 | `.` `?.` `[]` `?[]` 튜플 projection `.1` 호출 `f(x)` 후위 `++` `--` null-forgiving `!` | 왼쪽 |
| 2 | application | space-call ([§11.2](#112-space-call)) | 왼쪽 |
| 3 | 전위 | 단항 `+` `-`, `!`, `not`, `~`, 전위 `++` `--`, 캐스트 `(T)x`, index-from-end `^` (인덱서 안에서만) | 오른쪽 |
| 4 | switch / with | `x switch { ... }`, `x with { ... }` | 왼쪽 |
| 5 | 곱셈 | `*` `/` `%` | 왼쪽 |
| 6 | 덧셈 | `+` `-` | 왼쪽 |
| 7 | shift | `<<` `>>` | 왼쪽 |
| 8 | range | `..` `..<` `..=`, stepped `a..s..b` | 없음 |
| 9 | **pipe** | `\|>` (왼쪽), `<\|` (오른쪽) | 아래 참조 |
| 10 | 관계 / 타입 검사 | `<` `<=` `>` `>=` `<=>` `is` `is not` `as` | 왼쪽 |
| 11 | 동등 | `==` `!=` | 왼쪽 |
| 12 | 비트 AND | `&` | 왼쪽 |
| 13 | XOR | `^` `xor` | 왼쪽 |
| 14 | 비트 OR | `\|` | 왼쪽 |
| 15 | 논리 AND | `&&` `and` | 왼쪽 |
| 16 | 논리 OR | `\|\|` `or` | 왼쪽 |
| 17 | null 병합 | `??` | 오른쪽 |
| 18 | 조건 | `c ? a : b` | 오른쪽 |
| 19 | 대입 / 람다 | `=` `:=` `+=` `-=` `*=` `/=` `%=` `&=` `\|=` `^=` `<<=` `>>=` `??=` `=>` | 오른쪽 |

주의할 점:

- range(8)는 덧셈보다 약하다. 그래서 `0..<n-1`은 `0..<(n-1)`이다. C#의 range는 곱셈보다 강하지만 PSCP는 다르다.
- `&`, `^`, `|`가 `==`보다 약한 것은 C#과 같다. `x & mask == 0`은 `x & (mask == 0)`이다(C#과 같은 함정).
- `+=` 등 대입 연산자가 known DS rewrite인지는 우선순위가 아니라 semantic binding이 정한다([§26](#26-자료구조-연산자-rewrite)).

### 13.2 대입 연산자의 결합

대입 계열은 오른쪽 결합이다. `a = b := c`는 `a = (b := c)`다.

### 13.3 pipe

#### 위치

pipe는 **range보다 약하고 관계 연산자보다 강하다**. 그래서 pipe의 결과는 그대로 비교, 논리 연산의 피연산자가 된다.

| source | 의미 |
|---|---|
| `xs \|> sum == 10` | `(sum xs) == 10` |
| `xs \|> sum < 3 and ok` | `((sum xs) < 3) and ok` |
| `a + b \|> abs` | `abs(a + b)` |
| `0..<n \|> sum` | `sum (0..<n)` |
| `x == y \|> f` | `x == (f y)` |

`|>`는 왼쪽 결합, `<|`는 오른쪽 결합이다. 괄호 없이 한 chain에 `|>`와 `<|`를 섞으면 **오류**다.

#### 대상 형태와 rewrite

`lhs |> T`에서 `T`는 다음 중 하나이며, `lhs`는 **첫 번째 인자**로 삽입된다.

| 대상 `T` | rewrite |
|---|---|
| 이름 `f` | `f(lhs)` |
| 멤버 경로 `a.b.f` | `a.b.f(lhs)` |
| 호출 `f(x, y)` / `a.f(x, y)` | `f(lhs, x, y)` / `a.f(lhs, x, y)` |
| application `f x y` | `f lhs x y` |
| 변환 키워드 `long` | `long lhs` |
| 괄호 식 `(e)` (람다 포함) | `(e)(lhs)` — delegate 호출 |

`rhs`를 **마지막 인자**로 넣는 `<|`는 대칭이다: `f x y <| rhs`는 `f x y rhs`, `f(x, y) <| rhs`는 `f(x, y, rhs)`.

#### collection helper로의 pipe

collection helper([§24](#24-collection-helper))는 member 호출 전용이다. 그래도 pipe 대상의 **head 자리**에서는 collection helper 이름을 쓸 수 있고, 이때는 receiver 삽입으로 rewrite한다.

| source | rewrite |
|---|---|
| `xs \|> filter(p)` | `xs.filter(p)` |
| `xs \|> map(f) \|> sum` | `sum (xs.map(f))` |
| `xs \|> sort` | `xs.sort()` |
| `xs \|> count(p) > 0` | `xs.count(p) > 0` |

head 이름이 사용자 선언에 가려져 있으면 사용자 선언이 이긴다([§5](#5-이름-해석)).

#### 진단

- 대상이 호출 가능하지 않으면 오류다.
- rewrite 결과는 직접 쓴 호출과 똑같이 분석되고 lowering된다.

### 13.4 `=`와 `:=`

#### `=`

- 문장으로 쓰면 일반 대입이다.
- 식 안에서 쓰면 **호환용 대입식**으로 받아들이되 **경고**한다: `f(x = 5)`, `a = b = c`.
- tail position에서 `=`는 반환 대상이 아니다([§10.5](#105-꼬리-위치와-암묵적-반환)).

#### `:=`

`lhs := rhs`는 대입한 뒤 **대입한 값을 결과로** 낸다. 값이 필요한 대입의 정식 형태다.

```txt
parent[x] := find(parent[x])
a = b := c
```

lowering: C# 대입식으로 보존한다(`return parent[x] = find(parent[x]);`).

### 13.5 `~`와 전위 `--`

- `~expr`는 기본적으로 비트 보수다. 피연산자의 **정적 타입이 정확히** `Stack<T>`, `Queue<T>`, `PriorityQueue<E, P>`이면 Peek rewrite다.
- 전위 `--expr`는 기본적으로 감소 연산이다. 같은 타입이면 Pop/Dequeue rewrite다.
- 후위 `q--`는 rewrite 대상이 아니며, known DS에 쓰면 오류다.

자세한 표는 [§26](#26-자료구조-연산자-rewrite)에 있다.

### 13.6 `^`, `xor`, index-from-end

- 이항 `^`는 C#과 같다. 정수에서는 비트 XOR, `bool`에서는 논리 XOR이다. `xor`는 `^`의 별칭이다.
- 전위 `^k`는 index-from-end이며 **인덱서 안에서만** 쓸 수 있다([§15](#15-인덱싱과-슬라이싱)).

### 13.7 `not`과 `!`

`not`은 `!`의 별칭이며 **전위 단항 연산자 순위**를 갖는다. 즉 `not a == b`는 `(not a) == b`다(Python과 다르다).

트랜스파일러는 `not X`가 관계/동등/`is` 연산자의 왼쪽 피연산자가 되면 **경고**해야 한다. 진단은 `not (a == b)` 또는 `a != b`를 제안한다. `not visited.Add(x)`나 `not (visited += x)`처럼 후위식에 붙는 흔한 형태는 경고하지 않는다.

### 13.8 `<=>`

`a <=> b`는 두 값을 기본 순서([§25.1](#251-기본-순서))로 비교해 `int`를 낸다.

- 음수: `a < b`, 0: 같음, 양수: `a > b`
- **부호만 의미가 있다.** `-1/0/1`로 정규화된다고 가정하면 안 된다.

### 13.9 패턴 문맥

`is` 뒤와 switch 식의 arm 패턴은 **패턴 문맥**이다. 패턴 문맥에서는 C# 10 패턴 문법을 그대로 쓴다.

- 관계 패턴: `> 0`, `<= 10`
- 패턴 결합자: `and`, `or`, `not`
- 타입/선언/`var`/속성/위치 패턴

```txt
if x is > 0 and < 10 then += "digit"
if node is not null then visit(node)
let kind = v switch {
    < 0 => "neg"
    0 => "zero"
    _ => "pos"
}
```

패턴 문맥에서 `and`/`or`/`not`은 **패턴 결합자**다. 패턴 문맥은 다음 토큰에서 끝난다: `&&`, `||`, `?`, `)`, `]`, `,`, `=>`, `when`, `then`, `do`, `else`, 줄바꿈, `;`.

따라서 패턴과 일반 불리언을 결합할 때는 `&&`/`||`를 쓰거나 괄호로 감싼다.

```txt
if x is int v && v > 0 then ...        // OK
if (x is int v) and v > 0 then ...     // OK
if x is int v and v > 0 then ...       // 오류: `v > 0`을 패턴으로 읽는다
```

`if`/`while` 머리에서 패턴 뒤의 `{`는 블록 시작이다. 속성 패턴으로 머리를 끝내려면 괄호로 감싼다: `if (p is Point { X: 0 }) { ... }`.

---
## 14. range

### 14.1 형태

| 형태 | 의미 |
|---|---|
| `a..b` | `a`부터 `b`까지, 끝 포함, step `+1` |
| `a..=b` | `a..b`와 같다 (끝 포함을 명시) |
| `a..<b` | `a`부터 `b` 직전까지, 끝 제외, step `+1` |
| `a..s..b` | step `s`, 끝 포함 |
| `a..s..=b` | step `s`, 끝 포함 (명시) |
| `a..s..<b` | step `s`, 끝 제외 |

```txt
1..10          // 1, 2, ..., 10
0..<n          // 0, 1, ..., n-1
10..-1..0      // 10, 9, ..., 0
0..2..<n       // 0, 2, 4, ... (< n)
'a'..'z'       // 'a', 'b', ..., 'z'
```

`a..b`와 `a..=b`는 range로 쓸 때 같다. 둘이 구별되는 곳은 인덱서 안이다([§15.2](#152-슬라이스)).

### 14.2 원소 타입

- 모든 bound와 step이 `int`면 `int`
- bound나 step 중 하나라도 `long`(또는 `int`에 안 들어가는 리터럴)이면 `long`
- 두 bound가 모두 `char`면 `char`. step은 `int`다.
- `char`와 정수를 bound로 섞으면 오류다.

### 14.3 의미

- 수열은 `a, a+s, a+2s, …`이다. `s > 0`이면 끝을 넘지 않는 동안, `s < 0`이면 끝 아래로 내려가지 않는 동안 계속된다. 끝 포함 형태는 끝과 같은 값을 포함하고, 끝 제외 형태는 포함하지 않는다.
- step을 쓰지 않은 range는 **역방향 step을 추론하지 않는다.** 시작이 끝보다 뒤에 있으면(`5..1`, `a..<b`이고 `a >= b`) **빈 range**다. 오류가 아니다. 내려가는 수열에는 음수 step을 명시한다.
- bound와 step은 **range 식이 평가될 때 정확히 한 번**, `a`, `s`, `b` 순서로 평가된다. 루프 본문이 `q.Count` 같은 bound를 바꿔도 반복 횟수는 바뀌지 않는다.
- **step 0**
  - 컴파일 시점 상수 0이면 **오류**다.
  - 실행 중 0이 되면 **빈 range**다.

### 14.4 경계 오버플로

수열의 다음 값이 원소 타입의 범위를 넘어가야 하는 경우는 **전제조건 위반**이다([§29.2](#292-전제조건-위반)). 예를 들어 끝 포함 range의 끝이 `int.MaxValue`인 경우다.

```txt
for i in 0..int.MaxValue { ... }    // 전제조건 위반. 0..<int.MaxValue 또는 long range를 쓴다
```

Debug는 이를 검출해야 한다. Release는 검사하지 않는다. 대부분의 루프가 `i <= b` 비교 하나로 끝나도록 하기 위한 선택이다.

### 14.5 range 값

range가 `for`, `->`, `[]`, 인덱서, aggregate 인자 이외의 위치에서 값으로 쓰이면 정적 타입은 `IEnumerable<T>`이다. 나열할 때마다 같은 수열을 다시 만든다(bound는 생성 시 평가된 값).

```txt
let r = 0..<n
+= r.count()
+= sum r
```

### 14.6 lowering contract

단순 숫자 range는 direct `for`로 낮춘다.

```csharp
for (int i = 0; i < n; i++)          // 0..<n
for (int i = 1; i <= n; i++)         // 1..n
for (int i = m - 1; i >= 0; i--)     // m-1..-1..0
```

- bound를 `for` 조건식에 그대로 두는 것은 재평가해도 값이 같을 때(리터럴, 재대입되지 않는 이름)만 허용한다. 그 밖의 bound(`q.Count`, `a[i]`, 호출, 재대입되는 변수)는 루프 전에 임시 변수로 한 번 읽는다.
- step이 컴파일 시점 상수면 조건식은 비교 하나다. step이 실행 중 값이면 `s > 0 ? i <= b : s < 0 && i >= b` 형태로 낮춘다. 이 형태는 step 0에서 빈 range가 된다.
- `long` range는 `long` 카운터를 쓴다.
- materialize된 range의 길이는 음수가 되지 않도록 `0`으로 clamp한다.

금지:

- 단순 range를 helper enumerable로 낮추기
- compile-time known step에 대해 dead `throw`를 남기기
- `a..b`에서 내려가는 step을 추론하기

---

## 15. 인덱싱과 슬라이싱

### 15.1 인덱스

```txt
arr[i]
text[^1]        // 끝에서 첫 번째
grid[r][c]
```

`^k`는 C#과 같은 index-from-end다(`^1`이 마지막 원소). 인덱서 안에서만 쓸 수 있다.

### 15.2 슬라이스

인덱서 안에서 range로 부분을 잘라낸다. **인덱서 안에서는 끝 포함 여부를 반드시 명시해야 한다.**

| 형태 | 의미 |
|---|---|
| `xs[a..<b]` | `a`부터 `b` 직전까지 |
| `xs[a..=b]` | `a`부터 `b`까지 (끝 포함) |
| `xs[a..]` | `a`부터 끝까지 |
| `xs[..<b]` | 처음부터 `b` 직전까지 |
| `xs[..=b]` | 처음부터 `b`까지 |
| `xs[..]` | 전체 복사 |

bound에는 `^k`를 쓸 수 있다: `text[1..<^1]`은 첫 글자와 마지막 글자를 뺀 부분이다.

다음은 **오류**이며, 진단은 `..<` 또는 `..=`로 고치는 수정안을 제시해야 한다.

```txt
arr[1..3]       // 오류: 인덱서 안의 `..`는 끝 포함 여부가 모호합니다. arr[1..<3] 또는 arr[1..=3]
arr[..n]        // 오류: arr[..<n] 또는 arr[..=n]
```

이 규칙이 있는 이유: PSCP의 `a..b`는 끝을 포함하지만 C#의 `a..b` 슬라이스는 끝을 포함하지 않는다. 인덱서 안에서 `..`를 금지하면 두 관습 중 어느 쪽을 기대한 코드도 조용히 틀리지 않는다.

stepped range는 인덱서 안에서 쓸 수 없다.

### 15.3 슬라이스 대상과 lowering

| 대상 타입 | 결과 | lowering |
|---|---|---|
| `T[]` | 새 `T[]` | C# range 인덱서 (`arr[a..b]`) |
| `string` | 새 `string` | C# range 인덱서 (`Substring`과 같음) |
| `List<T>` | 새 `List<T>` | `GetRange(start, count)` |
| 그 밖 | 오류 | — |

- 슬라이스는 **복사**다. 비용은 잘라낸 길이에 비례한다.
- `a..=b`는 C# `a..(b+1)`로 낮춘다. `^k` 끝은 C# `^(k-1)`로 낮춘다.
- 범위를 벗어난 슬라이스는 .NET 예외다(pass-through 의미).

---

## 16. 컬렉션 식과 순회

### 16.1 materialized collection

```txt
[1, 2, 3]
[0..<n]
[1, 2, ..rest, 9]
['a'..'z']
```

#### 원소

`[]` 안의 원소는 셋 중 하나다.

1. **식**: 원소 하나
2. **spread `..e`**: iterable `e`의 원소를 모두 펼친다
3. **range 문법**: 괄호 없이 쓴 range 식(`a..b`, `a..<b`, stepped 포함)은 **자동으로 펼친다**

자동 펼침은 **문법으로 결정한다**. range 값을 담은 변수는 펼쳐지지 않는다.

```txt
let r = 0..<3
[0..<3]      // [0, 1, 2]
[..r]        // [0, 1, 2]
[r]          // IEnumerable<int> 하나를 담은 배열 (원소가 range 값)
```

`[]` 안에서 시작이 없는 `..<b` 형태는 spread와 구별할 수 없으므로 오류다.

#### 타입

- target type이 없으면 결과는 배열 `T[]`다.
- target type으로 다음을 쓸 수 있다: `T[]`, `List<T>`, `LinkedList<T>`, `HashSet<T>`, `SortedSet<T>`, `Queue<T>`, `Stack<T>` (`Stack<T>`은 쓴 순서대로 push하므로 마지막 원소가 top이다).
- 원소 타입은 원소들의 공통 타입이다. 숫자는 C# 암묵 변환 규칙으로 넓힌다(`[1, 2L]`은 `long[]`). 공통 타입이 없으면 오류다.
- **빈 `[]`는 target type이 있어야 한다.** 없으면 오류다.

```txt
List<int> xs = [3, 1, 2]
int[] empty = []
let bad = []          // 오류: 빈 컬렉션의 타입을 알 수 없습니다
```

#### lowering

- 크기를 알 수 있으면 한 번 할당하고 직접 채운다.
- spread가 있으면 크기를 싸게 알 수 있을 때 미리 할당하고, 아니면 growable builder를 쓴 뒤 finalize한다.
- range 원소는 helper enumerable 없이 직접 채운다.

### 16.2 builder

```txt
[0..<n -> i do i * i]
[arr -> x do f(x)]
[0..<n -> _ do INF]                          // INF로 채운 배열
[0..<h -> _ do [0..<w -> _ do -1]]           // h × w, -1로 채운 jagged 배열
```

- `src`를 나열하며 각 원소에 대해 `e`를 평가한 결과를 **즉시** 모은다.
- 결과 타입 규칙은 [§16.1](#161-materialized-collection)과 같다.
- binding 형태는 fast iteration과 같다([§16.4](#164-fast-iteration)).

lowering: 크기를 알 수 있으면 할당 + fill loop로 낮춘다.

### 16.3 generator

```txt
(0..<n -> i do i * i)
(edges -> (u, v) do w[u] + w[v])
```

- **lazy iterable**이다. 중간 컬렉션을 만들지 않는다.
- `src` 식은 generator 식이 평가될 때 한 번 평가된다. 원소 식 `e`는 **나열할 때마다** 다시 평가된다. 두 번 나열하면 `e`도 두 번 평가되고, 그 사이에 capture한 변수가 바뀌었으면 결과도 바뀐다.
- 정적 타입은 `IEnumerable<T>`다.

lowering:

- aggregate나 collection helper가 **즉시 소비**하면 direct fused loop로 낮춰야 한다(SHOULD). 중간 materialization은 금지다.
- 값으로 저장, 반환, 전달되면 helper iterable로 낮춰도 된다.

### 16.4 fast iteration

```txt
xs -> x {
    ...
}

xs -> i, x {
    ...
}

edges -> (u, v) {
    ...
}

pairs -> i, (a, b) {
    ...
}

dict -> (key, value) {
    ...
}
```

#### binding 형태

| binding | 의미 |
|---|---|
| `x` | 원소 |
| `i, x` | 0부터 시작하는 순번과 원소 |
| `(a, b)` | 원소를 구조 분해 (튜플, `KeyValuePair`, `Deconstruct`가 있는 타입) |
| `i, (a, b)` | 순번과 구조 분해한 원소 |
| `_` | 버림 |

같은 binding 형태를 `for ... in`에도 쓸 수 있다: `for (u, v) in edges { ... }`, `for i, x in xs { ... }`.

#### 의미

- 원소는 receiver의 나열 순서대로 방문한다.
- binding 변수는 불변이다. 원소를 바꾸려면 인덱스로 접근한다.
- **순회 중에 receiver를 구조적으로 바꾸는 것**(원소 추가/삭제)은 **전제조건 위반**이다. 배열의 원소 대입은 구조 변경이 아니다. Debug는 `List<T>`, `Dictionary<K,V>`, `HashSet<T>` 등의 구조 변경을 검출해야 한다.
- receiver가 range면 bound는 한 번 평가된다([§14.3](#143-의미)).

#### lowering

- range → direct `for`
- 배열 → 인덱스 `for`
- `List<T>` → Release에서는 인덱스 `for`를 쓸 수 있다. Debug에서는 구조 변경을 검출할 수 있는 형태(`foreach` 등)를 쓴다.
- 그 밖 → `foreach`
- discard binding은 의미 없는 대입 문장을 만들지 않는다.

위 규칙 덕분에 lowering을 어느 쪽으로 해도 관측 가능한 의미는 같다.

---

## 17. 입력

### 17.1 선언 기반 입력 shorthand

```txt
int n =
long a, b, c =
string s =
char c =
int[n] arr =
int[n][m] grid =
char[h][w] board =
(int, int) p =
(int, int, long)[m] edges =
mut int k =
```

#### 인식 규칙

**명시 타입 선언**(`mut` 가능)이고, `=`가 **문장의 마지막 토큰**이면(뒤에 줄바꿈, `;`, `}`, 파일 끝, 주석만 있으면) 입력 shorthand다. 타입이 없는 `let x =`, `var x =`는 오류다.

#### 의미

- 선언된 순서대로, 배열은 행 우선 순서로 값을 읽는다.
- 원소 타입은 token-readable이어야 한다([§17.6](#176-token-readable-타입)).
- 배열 크기 식은 읽기 전에 왼쪽부터 평가된다.
- 원소 하나를 읽는 방법은 타입별로 [§17.3](#173-토큰-모델)의 reader와 같다. 특히 `char` 원소는 **문자 단위**로 읽는다(`readChar`).

| 선언 | 읽는 방법 |
|---|---|
| `int n =` | `readInt()` |
| `string s =` | `readString()` — 토큰 하나 |
| `char c =` | `readChar()` — 공백을 건너뛴 문자 하나 |
| `char[n] s =` | 문자 `n`개 (공백은 건너뜀) |
| `char[h][w] board =` | 문자 `h × w`개 (공백과 줄바꿈은 건너뜀) |
| `(int, long) p =` | `readInt()`, `readLong()` 순서 |

따라서 격자 입력은 줄 사이에 공백이 있든 없든 같은 결과를 낸다.

```txt
// 입력:
// 2 3
// #.#
// ..#
int h, w =
char[h][w] board =      // board[1][2] == '#'
```

격자 안의 **공백 자체가 의미 있는 문자**라면 줄 단위 reader `stdin.readCharGrid(h)`를 쓴다.

#### lowering contract

shorthand는 generic runtime dispatcher가 아니라 **원소별 직접 read**로 낮춘다.

```csharp
int[] arr = new int[n];
for (int i = 0; i < n; i++)
    arr[i] = __pscp_stdin.readInt();
```

### 17.2 `stdin` API

| API | 반환 | 분류 |
|---|---|---|
| `readInt()` | `int` | 토큰 |
| `readLong()` | `long` | 토큰 |
| `readDouble()` | `double` | 토큰 |
| `readDecimal()` | `decimal` | 토큰 |
| `readBool()` | `bool` | 토큰 |
| `readString()` | `string` | 토큰 |
| `readChar()` | `char` | 문자 |
| `readArray<T>(n)` | `T[]` | 토큰 |
| `readList<T>(n)` | `List<T>` | 토큰 |
| `readLinkedList<T>(n)` | `LinkedList<T>` | 토큰 |
| `readTuple<T1, T2>()` … `readTuple<T1, …, T7>()` | `(T1, …)` | 토큰 |
| `readGrid<T>(n, m)` | `T[][]` | 토큰 (`T = char`면 문자) |
| `readLine()` | `string` | 줄 |
| `readLines(n)` | `string[]` | 줄 |
| `readRestOfLine()` | `string` | 줄 |
| `readWords()` | `string[]` | 줄 |
| `readChars()` | `char[]` | 줄 |
| `readCharGrid(n)` | `char[][]` | 줄 |
| `readWordGrid(n)` | `string[][]` | 줄 |
| `hasNext()` | `bool` | EOF 검사 |
| `hasNextLine()` | `bool` | EOF 검사 |

튜플 배열은 `readArray<(int, int)>(m)`처럼 읽는다.

### 17.3 토큰 모델

- **공백**은 스페이스, 탭, `\r`, `\n`, `\f`, `\v`다.
- **토큰**은 공백이 아닌 문자가 최대한 이어진 것이다. 토큰 reader는 앞 공백을 건너뛴 뒤 토큰 하나를 소비한다. 줄 경계는 신경 쓰지 않는다.
- `readInt()` / `readLong()`: 선택적 부호(`+`, `-`) 뒤에 10진 숫자. 값이 타입 범위 안에 있어야 한다.
- `readDouble()` / `readDecimal()`: InvariantCulture로 해석한다(소수점은 `.`). `readDouble()`은 지수 표기(`1e-9`)를 허용한다.
- `readString()`: 다음 토큰 전체.
- `readChar()`: 공백을 건너뛴 뒤 **문자 하나만** 소비한다. 토큰의 나머지는 남는다.

  ```txt
  // 입력: "ab c"
  char x = stdin.readChar()    // 'a'
  char y = stdin.readChar()    // 'b'
  char z = stdin.readChar()    // 'c'
  ```

- `readBool()`: 토큰이 대소문자 무관 `true` 또는 `1`이면 `true`, 대소문자 무관 `false` 또는 `0`이면 `false`. 그 밖은 형식 오류다.

### 17.4 줄 모델

reader는 **cursor**를 가진다. cursor는 줄 시작에 있거나(처음 상태, 줄 reader 직후), 줄 중간에 있다(토큰/문자 reader 직후).

- **`readLine()`**
  1. cursor가 줄 시작이면: 그 줄 전체를 반환한다.
  2. cursor가 줄 중간이면: 현재 줄의 나머지에서 앞 공백을 건너뛴다. 남는 게 없으면 **다음 줄 전체**를 반환하고, 남는 게 있으면 **그 나머지**를 반환한다.

  반환 후 cursor는 다음 줄의 시작으로 간다.
- **`readRestOfLine()`**: 현재 줄의 나머지를 **그대로**(앞 공백 포함, 비어 있을 수도 있음) 반환하고 cursor를 다음 줄 시작으로 옮긴다. cursor가 줄 시작이면 그 줄 전체를 반환한다.
- `readLines(n)`: `readLine()`을 `n`번 호출한다.
- `readWords()`: `readLine()`의 결과를 공백으로 나눈다. 빈 줄이면 빈 배열이다.
- `readChars()`: `readLine()`의 결과를 `char[]`로 반환한다.
- `readCharGrid(n)` / `readWordGrid(n)`: `readChars()` / `readWords()`를 `n`번 호출한다.
- 줄 끝은 `\n` 또는 `\r\n`이다. **반환되는 줄에는 `\r`, `\n`이 포함되지 않는다.**
- 파일 끝의 마지막 줄바꿈은 빈 줄을 새로 만들지 않는다.

```txt
// 입력:
// 3
// hello world
int n =
string s = stdin.readLine()       // "hello world"

// 입력:
// 5 x y
// next
int k =
string rest = stdin.readLine()    // "x y"
string nxt = stdin.readLine()     // "next"
```

### 17.5 입력 끝(EOF) 검사

- **`hasNext()`**: 토큰이 하나 이상 남았으면 `true`다. 공백을 건너뛰어 cursor를 다음 토큰 앞으로 옮길 수 있다. 그 뒤의 `readLine()`은 그 토큰부터 줄 끝까지를 반환한다.
- **`hasNextLine()`**: 다음 `readLine()`이 줄을 반환할 수 있으면 `true`다. `readLine()`이 할 줄 이동과 같은 이동만 할 수 있다.

```txt
mut long total = 0
while stdin.hasNext() {
    long x =
    total += x
}
+= total

while stdin.hasNextLine() {
    let line = stdin.readLine()
    += line.Length
}
```

### 17.6 token-readable 타입

1. 스칼라: `int`, `long`, `double`, `decimal`, `bool`, `char`, `string`
2. 위 스칼라로만 이뤄진 **평평한 튜플**, 원소 2개 이상 7개 이하

그 밖의 타입(사용자 class/struct, 중첩 튜플, 컬렉션)을 입력 shorthand나 `readArray<T>` 등의 `T`로 쓰면 **오류**다.

### 17.7 입력의 전제조건 위반

다음은 전제조건 위반이다([§29.2](#292-전제조건-위반)).

- 입력 끝에서 토큰/문자/줄을 읽으려 함
- 형식에 맞지 않는 토큰 (`readInt()`인데 `abc`)
- 타입 범위를 넘는 값

Debug는 각각 `EndOfStreamException`, `FormatException`, `OverflowException`을 던져야 한다. Release는 검사하지 않으며 결과는 미정이다.

`hasNext()` / `hasNextLine()`으로 먼저 검사하면 입력 끝에서의 전제조건 위반을 피할 수 있다.

### 17.8 폐기 예정 이름

다음 이름은 v0.7에서 **경고와 함께** 계속 동작하며, v0.8에서 제거한다. 진단은 대체 이름을 제시해야 한다.

| 폐기 이름 | 대체 |
|---|---|
| `stdin.int()`, `long()`, `double()`, `decimal()`, `bool()`, `char()`, `str()` | `readInt()`, `readLong()`, … `readString()` |
| `stdin.line()`, `lines(n)`, `words()`, `chars()` | `readLine()`, `readLines(n)`, `readWords()`, `readChars()` |
| `stdin.array<T>(n)`, `list<T>(n)`, `linkedList<T>(n)` | `readArray<T>(n)`, `readList<T>(n)`, `readLinkedList<T>(n)` |
| `readTuple2<A,B>()`, `readTuple3<A,B,C>()` (및 `tuple2`, `tuple3`) | `readTuple<A,B>()`, `readTuple<A,B,C>()` |
| `readTuples2<A,B>(n)`, `readTuples3<A,B,C>(n)` (및 `tuples2`, `tuples3`) | `readArray<(A,B)>(n)`, `readArray<(A,B,C)>(n)` |
| `readGridInt(n, m)`, `readGridLong(n, m)` (및 `gridInt`, `gridLong`) | `readGrid<int>(n, m)`, `readGrid<long>(n, m)` |
| `readNestedArray<T>(n, m)` (및 `nestedArray`) | `readGrid<T>(n, m)` |
| `charGrid(n)`, `wordGrid(n)` | `readCharGrid(n)`, `readWordGrid(n)` |

### 17.9 lowering contract

- reference backend baseline: `Console.OpenStandardInput()` + 충분한 크기의 버퍼, token scanner와 줄 cursor 처리.
- 숫자 토큰은 문자열을 만들지 않고 직접 파싱하는 것을 권장한다.
- `stdin.read<T>()` 같은 generic dispatcher를 shorthand의 기본 lowering 대상으로 써서는 안 된다.
- Debug 전용 검사는 `#if DEBUG`로 감싼다.

---

## 18. 출력

### 18.1 출력 shorthand

```txt
= expr       // 줄바꿈 없이 출력
+= expr      // 출력 후 줄바꿈
```

- **문장 시작**에서만 shorthand다. 문장 시작에는 `then`, `else`, `do` 바로 뒤와 `;` 바로 뒤가 포함된다.
- 문장 시작의 `=`/`+=`에는 왼쪽 피연산자가 없으므로 대입으로 해석될 여지가 없다.

```txt
+= ans
if found then += "YES" else += "NO"
for x in xs do = $"{x} "
```

lowering:

```csharp
__pscp_stdout.write(expr);
__pscp_stdout.writeln(expr);
```

### 18.2 `stdout` API

| API | 의미 |
|---|---|
| `stdout.write(x)` | `x`를 렌더링해 출력 |
| `stdout.writeln(x)` | `x`를 렌더링해 출력하고 줄바꿈 |
| `stdout.writeln()` | 줄바꿈만 출력 |
| `stdout.flush()` | 버퍼를 비운다 |
| `stdout.lines(xs)` | `xs`의 각 원소를 한 줄씩 출력 |
| `stdout.grid(g)` | `g`의 각 행을 한 줄씩 출력 (`+= g`와 같다) |
| `stdout.join(sep, xs)` | `xs`의 원소를 `sep`로 이어 출력. 줄바꿈 없음. `sep`는 `string` 또는 `char` |

모두 `void`를 반환한다.

### 18.3 렌더링 규칙

출력 shorthand, `stdout` API, 보간 문자열의 형식 지정자 없는 hole, `string x` 변환은 모두 **같은 렌더링 규칙**을 쓴다.

| 값 | 렌더링 | 예 |
|---|---|---|
| 정수 (`int`, `long`, …), `decimal` | 10진 표기 | `-42`, `3.50` |
| `double`, `float` | **지수 표기 없이** round-trip 가능한 가장 짧은 10진 표기 | `0.30000000000000004`, `0.00001`, `100000000000000000000` |
| `double` 특수값 | `NaN`, `Infinity`, `-Infinity`. 음의 0은 `0` | |
| `bool` | `true` / `false` (소문자) | `true` |
| `char` | 문자 그대로 | `a` |
| `string` | 그대로 | `hello` |
| `null` (참조, 값 없는 nullable) | 아무것도 출력하지 않음 | |
| 튜플, `KeyValuePair` | 원소를 공백 하나로 이음 | `1 a` |
| 1차원 컬렉션, 원소가 `char` | 구분자 없이 이음 | `abc` |
| 1차원 컬렉션, 원소가 스칼라 | 공백 하나로 이음 | `1 2 3` |
| 컬렉션, 원소가 튜플이나 컬렉션 | **원소 하나당 한 줄**. 각 원소는 위 규칙으로 렌더링 | `1 2⏎3 4` |
| 그 밖의 객체 | `ToString()` | |

- "컬렉션"은 `string`을 제외한 모든 `IEnumerable<T>`다(배열, `List<T>`, generator, `Dictionary<K, V>` 등).
- 여러 줄로 렌더링된 값에서 줄 사이는 `\n`이다. `=`는 마지막 줄 뒤에 줄바꿈을 붙이지 않고, `+=`는 붙인다.
- **3단 이상 중첩된 컬렉션**(예: `int[][][]`)은 정적 타입으로 알 수 있으면 오류다. 명시적 루프로 출력한다.

```txt
+= 1.0 / 100000.0          // 0.00001
+= 3 > 2                   // true
+= ['a', 'b', 'c']         // abc
+= [[1, 2], [3, 4]]        // 1 2⏎3 4
+= (1, "a")                // 1 a
List<(int, int)> es = [(1, 2), (3, 4)]
+= es                      // 1 2⏎3 4
```

### 18.4 culture

모든 숫자 렌더링과 숫자 입력 파싱은 **InvariantCulture**를 써야 한다. 실행 환경의 로캘에 따라 결과가 달라져서는 안 된다.

### 18.5 lowering contract

- 스칼라 출력은 직접 writer 호출을 우선한다.
- 튜플은 작은 arity 전용 렌더링을 우선한다.
- 1차원 스칼라 배열/리스트는 직접 join 렌더링을 우선한다.
- 정적 타입으로 렌더링 방법이 정해지면 런타임 타입 검사를 하는 fallback renderer를 쓰지 않는다.

버퍼와 flush 계약은 [§30.3](#303-출력-버퍼와-종료)에 있다.

---
## 19. 보간 문자열

```txt
$"answer = {ans}"
$"{min a b} {sum (0..<n -> i do a[i])}"
$"{value:F2}"
$"{(ok ? 1 : 0)}"
```

- C# 보간 문자열과 호환된다. hole 안에는 PSCP 식(space-call 포함)을 쓸 수 있다.
- hole 최상위의 `:`는 형식 지정자의 시작이다(C#과 같다). 조건 연산자는 괄호로 감싼다.
- **형식 지정자가 없는 hole은 [§18.3](#183-렌더링-규칙) 렌더링 규칙을 따른다.** 그래서 `$"{flag}"`는 `true`, `$"{0.00001}"`는 `0.00001`이다.
- 형식 지정자가 있으면 C# 형식을 **InvariantCulture**로 적용한다. `$"{x:F2}"`의 소수점은 항상 `.`이다.
- 정렬(`{x,5}`)은 C#과 같다.

lowering: C# 보간 문자열로 보존한다. InvariantCulture 문자열 생성(`string.Create(CultureInfo.InvariantCulture, $"...")`)을 쓰고, C# 기본 서식이 PSCP 렌더링과 다른 hole(`double`, `float`, `bool`, 튜플, 컬렉션)만 렌더링 helper로 감싼다.

---

## 20. 튜플

```txt
(1, 2)
(a, b, c)
p.1
p.2
(a, b) = (b, a)
```

- tuple projection `.1`, `.2`, …는 1부터 시작하는 원소 접근이다. C# `.Item1`, `.Item2`, …로 낮춘다. 8번째 이후는 `.Rest.Item1` 등으로 낮춘다.
- 이름 있는 튜플 원소(`(int x, int y)`, `p.x`)는 C#과 같다.
- 튜플 대입과 swap은 C# 튜플 대입으로 보존한다.

---

## 21. 변환 키워드

타입 키워드 `int`, `long`, `double`, `decimal`, `bool`, `char`, `string`은 식 위치에서 변환 함수처럼 쓸 수 있다. 괄호 호출과 space-call을 모두 쓸 수 있다.

```txt
int "123"
long(x)
double n
char (c + 1)
string 0.5
```

### 21.1 변환 표

| 원본 → 대상 | 규칙 | 예 |
|---|---|---|
| `string` → 숫자 | InvariantCulture 파싱 | `int "42"` → `42` |
| `string` → `bool` | `readBool`과 같은 파싱: `true`/`false`/`1`/`0` (대소문자 무관) | `bool "false"` → `false` |
| `string` → `char` | 길이 1인 문자열의 그 문자 | `char "x"` → `'x'` |
| 실수 → 정수 | **0 방향 버림** (C# 캐스트와 같다) | `int 3.7` → `3`, `int -3.7` → `-3` |
| 정수 → 정수 | C# 캐스트 (좁히면 wrap) | `int 5000000000L` → wrap |
| `char` → 정수 | **코드 값** | `int 'A'` → `65`, `int '7'` → `55` |
| 정수 → `char` | 코드 값의 문자 | `char 65` → `'A'` |
| `bool` → 숫자 | `true` → `1`, `false` → `0` | `int true` → `1` |
| 숫자 → `bool` | `!= 0` | `bool 0` → `false` |
| 모든 값 → `string` | [§18.3](#183-렌더링-규칙) 렌더링 | `string true` → `"true"`, `string ['a','b']` → `"ab"` |

- 숫자 문자 하나의 값을 얻으려면 `c - '0'`을 쓴다.
- 반올림이 필요하면 `round`를 먼저 쓴다: `long (round x)`.
- 문자열의 "비어 있음" 검사는 변환이 아니라 `s.Length > 0` 또는 `s != ""`로 쓴다.

### 21.2 전제조건 위반

파싱할 수 없는 문자열, 대상 범위를 벗어나는 실수→정수 변환, 길이가 1이 아닌 문자열의 `char` 변환은 전제조건 위반이다([§29.2](#292-전제조건-위반)).

### 21.3 lowering

- `string` → 숫자: `int.Parse(s, CultureInfo.InvariantCulture)` 등 직접 파싱
- 숫자 → 숫자, `char` ↔ 정수: 직접 캐스트
- `bool` → 숫자: `b ? 1 : 0`
- 숫자 → `bool`: `x != 0`
- 컴파일 시점 상수 변환은 folding할 수 있다.

변환 키워드는 키워드가 소유한 표면이므로 이름 충돌이 없다.

---

## 22. aggregate family

`min`, `max`, `sum`, `sumBy`, `minBy`, `maxBy`, `chmin`, `chmax`

### 22.1 호출 형태

aggregate family는 세 가지 호출 형태를 가지며, 모두 의미가 같다.

```txt
sum arr
sum(arr)
arr.sum()

sumBy arr (x => x * x)
sumBy(arr, x => x * x)
arr.sumBy(x => x * x)
```

`chmin`/`chmax`는 member 형태가 없다.

### 22.2 `min`, `max`

| 형태 | 의미 |
|---|---|
| `min a b`, `min a b c`, `min(a, b, …)` | 인자 2개 이상: 그중 최소 |
| `min xs`, `min(xs)`, `xs.min()` | iterable 하나: 원소 중 최소 |

- 비교는 기본 순서([§25.1](#251-기본-순서))를 쓴다. 숫자 인자의 타입이 섞이면 C# 숫자 승격을 따른다(`min a b`에서 `a: int`, `b: long`이면 `long`).
- 스칼라 인자 하나(`min x`)는 오류다.
- 동점이면 먼저 나온 값을 반환한다.
- **빈 iterable은 전제조건 위반**이다.

### 22.3 `sum`, `sumBy`

```txt
sum arr
sum (0..<n -> i do score(i))
sumBy edges (e => e.W)
```

- 빈 iterable의 합은 `0`이다.
- 누산 타입은 [§22.6](#226-정수-누산-타입) 규칙을 따른다.
- `sumBy`는 원소마다 selector를 정확히 한 번 평가한다.

### 22.4 `minBy`, `maxBy`

```txt
minBy points (p => p.x)
points.maxBy(p => p.score)
```

- key가 가장 작은/큰 **원소**를 반환한다.
- key는 원소마다 정확히 한 번 평가하고, 기본 순서로 비교한다.
- 동점이면 먼저 나온 원소를 반환한다.
- 빈 iterable은 전제조건 위반이다.

### 22.5 `chmin`, `chmax`

```txt
chmin ref best cand
if chmax(ref ans, value) then bestIndex = i
```

- 시그니처: `chmin(ref T target, T candidate) -> bool`
- `candidate`가 `target`보다 **엄격히** 작으면(`chmax`는 크면) `target`을 갱신하고 `true`를 반환한다. 아니면 `false`를 반환한다.
- 문장으로 쓰면 반환값은 버려진다.

lowering: 직접 비교-갱신으로 낮춘다.

### 22.6 정수 누산 타입

`sum`, `sumBy`, 정수 `pow`의 결과 타입은 원소(피연산자) 타입이다. 단, 그 호출이 **직접 target-typed** 위치에 있고 target이 더 넓은 정수 타입이면, 계산 자체를 target 타입으로 한다.

직접 target-typed 위치는 호출이 **식 전체**인 다음 위치다.

- 명시 타입 선언의 초기화식: `long total = sum arr`
- 타입이 정해진 변수로의 대입: `total = sum arr`
- 매개변수 타입이 정해진 인자
- 반환 타입이 정해진 함수의 `return` 또는 tail value

```txt
int[n] a =
let s1 = sum a                    // int 누산 (넘칠 수 있다)
long s2 = sum a                   // long 누산
long s3 = sum a + 1               // int 누산 후 +1 (호출이 식 전체가 아니다)
long s4 = sum (a -> x do long x)  // long 누산
```

- Release에서 정수 누산 오버플로는 wrap-around다.
- **Debug는 `sum`, `sumBy`, 정수 `pow`의 오버플로를 검출해야 한다.**

### 22.7 lowering contract

- 작은 fixed-arity `min`/`max` → 비교 트리
- iterable `min`/`max`/`sum` → direct loop
- generator 인자 → fused loop (중간 materialization 금지)
- `minBy`/`maxBy` → best 원소와 best key 추적
- `chmin`/`chmax` → 직접 비교-갱신

---

## 23. math family

### 23.1 목록

| 이름 | 시그니처 | 의미 |
|---|---|---|
| `abs(x)` | `int`→`int`, `long`→`long`, `double`→`double`, `decimal`→`decimal` | 절댓값. `abs(int.MinValue)` 등은 전제조건 위반 |
| `sqrt(x)` | 숫자 → `double` | 제곱근 |
| `clamp(x, lo, hi)` | `T` | `lo > hi`는 전제조건 위반 |
| `gcd(a, b)` | `int`, `long` | 음이 아닌 최대공약수. `gcd(0, 0) = 0` |
| `lcm(a, b)` | `int`, `long` | 음이 아닌 최소공배수. 하나라도 0이면 0 |
| `floor(x)` / `ceil(x)` | `double`→`double`, `decimal`→`decimal` | 내림 / 올림 |
| `floor(a, b)` / `ceil(a, b)` | `int`, `long` | 정수 나눗셈의 내림 / 올림: `floor(-7, 2) = -4`, `ceil(7, 2) = 4`. `b = 0`이면 .NET `DivideByZeroException` |
| `round(x)` | `double`→`double`, `decimal`→`decimal` | **0에서 먼 쪽으로 반올림**: `round 2.5 = 3`, `round -2.5 = -3` |
| `round(x, d)` | 위와 같음 | 소수 `d`자리에서 같은 규칙으로 반올림 |
| `pow(a, e)` | `int, int`→`int`, `long, int`→`long` | **정확한 정수 거듭제곱**. `e < 0`은 전제조건 위반. 누산 타입은 [§22.6](#226-정수-누산-타입) |
| `pow(a, e)` | `double, double`→`double` | 실수 거듭제곱 |
| `pow(a, e, m)` | 정수 → `long` | `a^e mod m`, 결과는 `[0, m)`. `m ≥ 1`, `e ≥ 0`이 전제조건 |
| `popcount(x)` | `int`/`long` → `int` | 1인 비트 수 (2의 보수 표현) |
| `bitLength(x)` | `int`/`long` → `int` | `x`를 나타내는 데 필요한 비트 수. `bitLength(0) = 0`, `bitLength(5) = 3`. 음수는 전제조건 위반 |

```txt
+= pow(3L, 39)            // 4052555153018976267 (정확)
+= pow(2, 10, 1000)       // 24
+= floor(-7, 2)           // -4
+= round 2.5              // 3
```

### 23.2 호출 형태

괄호 호출과 space-call을 쓸 수 있다(`abs x`, `gcd a b`). member 형태는 없다.

### 23.3 lowering contract

- `abs` → `Math.Abs` 또는 직접 식
- `sqrt` → `Math.Sqrt`
- `round` → `Math.Round(x, MidpointRounding.AwayFromZero)`
- 정수 `pow` → 제곱 반복 helper 또는 작은 상수 지수의 곱셈 전개. `Math.Pow`를 써서는 **안 된다**(정밀도 손실).
- `gcd`/`lcm`/`popcount`/`bitLength` → 특화 helper 또는 inline (`BitOperations.PopCount` 등)
- hot path에서 generic/dynamic dispatch 금지

사용자가 `gcd`를 선언하면 intrinsic `gcd`는 가려진다([§5.3](#53-intrinsic-shadowing)).

---

## 24. collection helper

### 24.1 호출 형태

collection helper는 **member 호출**이 정식 형태다.

```txt
xs.map(f)
xs.filter(pred)
xs.fold(seed, f)
```

pipe의 head 자리에서는 이름만 쓸 수 있다: `xs |> filter(pred)`는 `xs.filter(pred)`다([§13.3](#133-pipe)).

`map xs f` 같은 free-call 형태는 없다. space-call과 섞였을 때의 모호성이 크고, helper는 "receiver 변환"이라는 역할이 분명하기 때문이다.

### 24.2 receiver와 결과 타입

- receiver는 모든 `IEnumerable<T>`다: 배열, `List<T>`, generator, range 값, `string`(`char` 원소) 등.
- **컬렉션을 만들어 내는 helper의 결과는 항상 배열 `T[]`이다.** receiver가 `List<T>`여도 결과는 배열이다.
- helper는 receiver를 **바꾸지 않는다**.

```txt
List<int> values
...
let sorted = values.sort().distinct()    // int[]
+= sorted.Length
```

`List<T>`가 필요하면 `List<int> xs = [..values.sort()]`처럼 target-typed 컬렉션 식을 쓴다.

### 24.3 목록

| helper | 시그니처 | 의미 |
|---|---|---|
| `map(f)` | `(T → U) → U[]` | 각 원소에 `f` 적용 |
| `filter(pred)` | `(T → bool) → T[]` | 조건을 만족하는 원소 |
| `fold(seed, f)` | `(S, (S, T) → S) → S` | 왼쪽부터 누적. 인자 순서는 항상 `(state, item)` |
| `scan(seed, f)` | `(S, (S, T) → S) → S[]` | 누적 상태 전체. **seed를 포함**하며 길이는 입력 길이 + 1 |
| `mapFold(seed, f)` | `(S, (S, T) → (U, S)) → (U[], S)` | 상태를 누적하면서 map. callback은 `(mapped, nextState)` 반환 |
| `any()` / `any(pred)` | `→ bool` | 원소가 있는가 / 만족하는 원소가 있는가. **단락 평가** |
| `all(pred)` | `→ bool` | 모든 원소가 만족하는가. **단락 평가**. 빈 시퀀스는 `true` |
| `count()` / `count(pred)` | `→ int` | 원소 수 / 만족하는 원소 수 |
| `find(pred)` | `→ T?` | 처음 만족하는 원소. 없으면 `null` |
| `find(pred, fallback)` | `→ T` | 처음 만족하는 원소. 없으면 `fallback` |
| `findIndex(pred)` | `→ int` | 처음 만족하는 위치. 없으면 `-1` |
| `findLastIndex(pred)` | `→ int` | 마지막으로 만족하는 위치. 없으면 `-1` |
| `sort()` | `→ T[]` | 기본 순서로 **안정** 정렬 |
| `sortBy(key)` | `(T → K) → T[]` | key의 기본 순서로 **안정** 정렬 |
| `sortWith(cmp)` | `IComparer<T>` 또는 `(T, T) → int` → `T[]` | 비교자로 **안정** 정렬 |
| `distinct()` | `→ T[]` | 중복 제거. **처음 나온 순서를 유지** |
| `reverse()` | `→ T[]` | 역순 |
| `copy()` | `→ T[]` | 얕은 복사 |
| `freq()` | `→ Dictionary<T, int>` | 값별 등장 횟수 |
| `index()` | `→ Dictionary<T, int>` | 값별 **첫 등장 위치** |
| `lowerBound(x)` / `lowerBound(x, cmp)` | `→ int` | 정렬된 receiver에서 `x` 이상인 첫 위치 (`0`..`n`) |
| `upperBound(x)` / `upperBound(x, cmp)` | `→ int` | 정렬된 receiver에서 `x` 초과인 첫 위치 (`0`..`n`) |

#### 세부 규칙

- **`find`**: `T`가 값 타입이면 `T?`는 `Nullable<T>`, 참조 타입이면 nullable 참조다. `T`가 이미 nullable(`int?`)이면 결과도 `int?`이며, 이 경우 "못 찾음"과 "찾은 값이 null"을 구별할 수 없다. 이럴 때는 `findIndex`를 쓴다.

  ```txt
  if xs.find(x => x > limit) is int v then += v
  let first = xs.find(x => x > limit, -1)
  ```

- **`sortBy` / `minBy` / `maxBy`의 key**는 순수 함수여야 한다. `sortBy`에서 key 평가 횟수와 순서는 구현 정의다.
- **`lowerBound` / `upperBound`**: receiver는 `T[]` 또는 `List<T>`이며, 기본 순서(또는 `cmp`)로 오름차순 정렬돼 있어야 한다. 정렬돼 있지 않으면 전제조건 위반이다(Release 결과 미정).
- **동등성**: `distinct`, `freq`, `index`의 key 동등성은 `EqualityComparer<T>.Default`다.
- **좌표 압축**: `xs.sort().distinct().index()`는 값 → 압축 좌표 사전을 만든다.

```txt
let pref, total = arr.mapFold(0, (acc, x) => {
    let next = acc + x
    (next, next)
})

let sorted = xs.sort()
let lo = sorted.lowerBound(l)
let hi = sorted.upperBound(r)
+= hi - lo                       // [l, r] 구간의 원소 수
```

### 24.4 결과를 버리는 호출

collection helper는 receiver를 바꾸지 않는다. 따라서 **결과를 쓰지 않는 helper 호출 문장은 경고**다. 진단은 제자리 정렬(`Array.Sort(arr)`, `list.Sort()`) 또는 재대입(`arr = arr.sort()`)을 제안해야 한다.

```txt
arr.sort()           // 경고: sort()는 새 배열을 반환하며 arr은 바뀌지 않습니다
Array.Sort(arr)      // 제자리 정렬 (pass-through .NET)
```

### 24.5 폐기 예정

`groupCount()`는 `freq()`의 별칭이며 폐기 예정이다(v0.7 경고, v0.8 제거).

### 24.6 lowering contract

- 단순한 형태는 direct loop로 낮춘다.
- 복잡하거나 일반적인 형태는 helper를 쓸 수 있다.
- hot path에서 LINQ fallback 남용 금지
- `sort()`: 원소 타입이 기본 순서에서 서로 구별되지 않는 동점이 없는 타입(정수, `char`, `bool`)이면 불안정 정렬(`Array.Sort`)로 낮춰도 된다. 동점을 구별할 수 없으므로 안정성이 관측되지 않기 때문이다. 그 밖(`sortBy`, `sortWith`, `string`, 튜플, 사용자 타입의 `sort`)은 안정 정렬을 보장해야 한다(원래 위치를 보조 key로 쓰는 정렬 등).

---

## 25. 순서와 비교

### 25.1 기본 순서

PSCP가 소유한 모든 순서 연산은 다음 **기본 순서**를 쓴다: `<=>`, `min`/`max`, `minBy`/`maxBy`의 key, `chmin`/`chmax`, `sort`/`sortBy`, `lowerBound`/`upperBound`, `T.asc`/`T.desc`, known collection auto-construction.

| 타입 | 기본 순서 |
|---|---|
| 정수, `decimal` | 수의 크기 |
| `double`, `float` | 수의 크기. `NaN`은 모든 값보다 작다 (.NET 기본과 같음) |
| `char` | 코드 값 |
| `string` | **ordinal**: UTF-16 코드 단위의 사전식. 대소문자를 구별한다. `"B" < "a"` |
| `bool` | `false < true` |
| 튜플 | 원소별 사전식. 각 원소는 기본 순서로 비교 |
| `IComparable<T>` 구현 타입 (`operator<=>` 포함) | `CompareTo` |
| enum | 기반 값 |

기본 순서가 없는 타입에 순서 연산을 쓰면 오류다.

**문자열이 ordinal인 이유**: .NET의 기본 문자열 비교(`Comparer<string>.Default`, `string.CompareTo`)는 현재 culture의 언어 규칙을 따른다. 그러면 `["b", "B", "a", "A"]`가 `a A b B`로 정렬되고, 결과가 실행 환경에 따라 달라진다. 채점 환경이 기대하는 것은 ASCII 순서 `A B a b`다.

### 25.2 `T.asc`, `T.desc`

```txt
int.asc
long.desc
string.asc
Point.desc
(int, string).asc
```

- 타입이 `IComparer<T>`인 값이다. `asc`는 기본 순서, `desc`는 그 역순이다.
- `IComparer<T>`를 받는 곳이면 어디든 쓸 수 있다.

```txt
let ranked = scores.sortWith(int.desc)
PriorityQueue<int, int> maxHeap = new(int.desc)
SortedSet<string> names = new(string.asc)
Array.Sort(arr, long.desc)
```

lowering: 비교자 인스턴스를 static readonly로 캐시하거나 직접 비교자로 낮춘다.

### 25.3 `operator<=>`

```txt
record struct Job(int Id, long Time) {
    operator<=>(other) => Time <=> other.Time
}
```

- 타입 선언 안에서 그 타입의 기본 순서를 정의한다. 매개변수 타입은 둘러싼 타입이고, 반환 타입은 암묵적으로 `int`다. 블록 본문도 쓸 수 있다.
- 트랜스파일러는 다음을 생성해야 한다.
  1. `IComparable<T>` 구현. 기반 목록에 없으면 추가한다.
  2. `CompareTo(T other)`
  3. 관계 연산자 `<`, `<=`, `>`, `>=` (사용자가 직접 선언하지 않은 경우)
- `==`, `!=`, `Equals`는 생성하지 않는다. 동등성은 record 규칙 또는 사용자 정의를 따른다.

그래서 `operator<=>`를 정의한 타입은 `sort()`, `min`/`max`, `Array.Sort`, `SortedSet<T>`, `a < b`에서 모두 같은 순서를 쓴다.

### 25.4 .NET 정렬 컬렉션과 문자열

.NET이 직접 만드는 정렬 구조(`new SortedSet<string>()`, `new SortedDictionary<string, V>()`, `PriorityQueue<E, string>`, `Array.Sort(string[])`, `List<string>.Sort()`)는 pass-through이므로 PSCP가 비교자를 바꾸지 않는다. 이들은 culture 순서를 쓴다.

- **auto-construction**(`SortedSet<string> names`처럼 초기화 없는 선언, [§9.3](#93-초기화-없는-선언))과 `new![n]`은 PSCP 표면이므로 **PSCP 기본 순서**의 비교자를 넘겨 생성한다.
- 문자열(또는 문자열을 포함한 튜플)이 key인 정렬 구조를 비교자 없이 `new()`로 만들거나, 그런 배열/리스트를 비교자 없이 `Array.Sort`/`Sort()`하면 **경고**한다. 진단은 `string.asc` 또는 `StringComparer.Ordinal`을 넘기도록 제안한다.

---

## 26. 자료구조 연산자 rewrite

### 26.1 적용 조건

- 피연산자의 **정적 타입이 정확히** 아래 표의 BCL 타입일 때만 적용한다. 하위 클래스, wrapper, 사용자 타입에는 적용하지 않는다.
- rewrite는 **호출**이다. 반환 타입은 기반 .NET 메서드의 반환 타입이다. 값이 있는 rewrite는 식 안에서 쓸 수 있고 tail position에서 반환된다([§10.5](#105-꼬리-위치와-암묵적-반환)).

### 26.2 표

| 타입 | `x += v` | `x -= v` | `~x` | `--x` |
|---|---|---|---|---|
| `List<T>` | `Add(v)` → `void` | — | — | — |
| `LinkedList<T>` | `AddLast(v)` → `void` | — | — | — |
| `HashSet<T>` | `Add(v)` → `bool` | `Remove(v)` → `bool` | — | — |
| `SortedSet<T>` | `Add(v)` → `bool` | `Remove(v)` → `bool` | — | — |
| `Dictionary<K, V>` | `x += (k, v)`: `TryAdd(k, v)` → `bool` | `x -= k`: `Remove(k)` → `bool` | — | — |
| `Stack<T>` | `Push(v)` → `void` | — | `Peek()` → `T` | `Pop()` → `T` |
| `Queue<T>` | `Enqueue(v)` → `void` | — | `Peek()` → `T` | `Dequeue()` → `T` |
| `PriorityQueue<E, P>` | `x += (e, p)`: `Enqueue(e, p)` → `void` | — | `Peek()` → `E` | `Dequeue()` → `E` |

- 표에서 "—"인 칸의 연산자는 일반 의미로 해석한다. 그 결과 타입 오류가 되면 PSCP가 진단한다.
- `v`의 타입이 원소 타입과 맞지 않으면 오류다. 예를 들어 `List<int>`에 `List<int>`를 `+=` 하면 오류이며, 진단은 `AddRange`나 spread(`[..a, ..b]`)를 제안한다.
- `Dictionary`의 `+=`는 **이미 있는 key를 덮어쓰지 않는다**(`TryAdd`). 갱신은 `dict[key] = value`(pass-through)를 쓴다.
- 비어 있는 `Stack`/`Queue`/`PriorityQueue`에 `~`/`--`를 쓰면 .NET `InvalidOperationException`이 난다(pass-through 의미).
- 후위 `x--`, `x++`는 known DS에 쓸 수 없다(오류).
- 우선순위(priority)도 필요하면 명시적 API를 쓴다: `pq.TryPeek(out item, out priority)`, `pq.TryDequeue(out item, out priority)`.

```txt
HashSet<int> visited
if not (visited += x) then continue

Queue<int> q
q += start
while q.Count > 0 {
    let v = --q
    ...
}

PriorityQueue<int, long> pq
pq += (node, dist)
let top = ~pq
```

### 26.3 lowering contract

모든 rewrite는 wrapper 객체 없이 **기반 .NET 메서드 직접 호출**로 낮춘다.

---

## 27. 배열 생성

### 27.1 `new[n]`, `new[n][m]`

```txt
int[] arr = new[n]
long[][] dp = new[n][m]
bool[][][] seen = new[a][b][c]
NodeInfo[] nodes = new[n]
```

- target type을 보고 배열을 할당한다. 원소는 기본값이다.
- 여러 차원을 쓰면 **모든 층을 할당한 jagged 배열**이다.
- target type이 없으면 오류다.

lowering: 한 번의 할당 + 행 할당 loop. LINQ 금지.

### 27.2 `new![n]`

```txt
List<int>[] graph = new![n]
Queue<int>[] buckets = new![m]
Node[] nodes = new![n]
```

- 배열을 할당하고 **각 칸을 `new()`로 초기화**한다.
- 원소 타입은 접근 가능한 매개변수 없는 생성자를 가져야 한다. 원소 타입이 배열이면 오류다(`new[n][m]`을 쓴다).
- known auto-constructible 타입은 [§25.4](#254-net-정렬-컬렉션과-문자열)에 따라 PSCP 기본 순서의 비교자로 생성한다.

lowering:

```csharp
List<int>[] graph = new List<int>[n];
for (int i = 0; i < n; i++) graph[i] = new();
```

### 27.3 값으로 채운 배열

별도 문법 없이 builder([§16.2](#162-builder))를 쓴다. 트랜스파일러는 이를 할당 + fill loop로 낮춘다.

```txt
long[] dist = [0..<n -> _ do INF]
int[][] memo = [0..<n -> _ do [0..<m -> _ do -1]]
```

이미 있는 배열은 `Array.Fill(arr, value)`(pass-through)로 채운다.

### 27.4 폐기 예정

`Array.zero(n)`은 `new[n]`과 같은 기능이므로 폐기 예정이다(v0.7 경고, v0.8 제거).

---
## 28. 타입 선언

### 28.1 선언

```txt
class Node {
    mut int value
    List<Node> children
    Node(int v) { value = v }
}

struct Edge {
    mut int to
    mut long w
}

record Point(int X, int Y)
record struct Job(int Id, int Arrival, long Time)

class A : B, IFoo {
    ...
}
```

`class`, `struct`, `record`, `record struct`, `interface`, `enum`, 기반/인터페이스 목록, generic, constraint는 C#과 같다(pass-through). C# 10 범위를 따르므로 class의 primary constructor는 쓸 수 없다. positional record는 쓸 수 있다.

### 28.2 멤버

- **기본 접근성은 `public`이다.** 접근 한정자를 쓰지 않은 멤버(필드, 메서드, 생성자, 중첩 타입)는 `public`으로 생성한다. 명시한 한정자(`private` 등)는 그대로 보존한다.
- **필드의 가변성은 지역 선언과 같다**([§9.1](#91-바인딩-형태)).

  | 필드 선언 | 가변 |
  |---|---|
  | `T name`, `T name = e`, `let name = e` | 불변 |
  | `mut T name`, `mut T name = e`, `var name = e` | 가변 |

- 초기화식 없는 불변 필드는 생성자에서 대입해야 한다. 생성자 밖에서 대입하면 [§9.2](#92-불변성의-의미와-진단)의 불변 위반 경고를 내고, 그 필드는 일반 필드로 생성한다. 어디서도 대입하지 않으면 경고다(항상 기본값).
- known auto-constructible 타입의 필드(`List<Node> children`)는 `= new()`로 초기화한다([§9.3](#93-초기화-없는-선언)).
- 크기가 상수인 배열 필드(`Node?[26] next`)는 그 크기로 할당한다.
- 불변 필드는 [§9.6](#96-const--readonly-lowering)에 따라 `const` / `static readonly` / `readonly`로 생성한다.
- 메서드는 [§10](#10-함수)을 따른다. 암묵적 반환이 적용되며, 재귀에 `rec`이 필요 없다([§10.3](#103-rec)).
- 생성자, 속성(`{ get; set; }`), 연산자 선언, `this`/`base`는 C# 문법을 따른다.
- `operator<=>`는 [§25.3](#253-operator)을 따른다.
- 타입 안에서 보이는 최상위 이름은 [§7.4](#74-타입-선언에서의-가시성)를 따른다.

```txt
class TrieNode {
    TrieNode?[26] next
    mut bool terminal
}

let e = new Edge { to = 3, w = 10 }
```

---

## 29. 빌드 모드와 전제조건 위반

### 29.1 Debug와 Release

- **Release 빌드에는 PSCP가 추가로 넣은 검사나 `throw`가 없어야 한다.**
  - 허용: 사용자가 직접 쓴 `throw`, pass-through .NET API가 원래 던지는 예외
  - 금지: 방어적 range step 검사, generic fallback helper의 인위적 throw, 트랜스파일러가 편의상 넣은 sanity check
- Debug 전용 검사는 `#if DEBUG` 또는 `Debug.Assert`로 감싼다.
- 컴파일 시점에 알 수 있는 위반(상수 step 0 등)은 빌드 모드와 관계없이 **진단**한다.

### 29.2 전제조건 위반

전제조건 위반이 일어나면:

- **Debug**: 아래 표의 예외를 던져야 한다(MUST). 메시지는 원인을 알 수 있어야 한다.
- **Release**: 결과는 **미정**이다. 임의의 값을 돌려줄 수 있고, .NET 예외가 날 수도 있다. 프로그램은 이 결과에 의존해서는 안 된다.

| 상황 | 절 | Debug 예외 |
|---|---|---|
| 빈 iterable의 `min`/`max`/`minBy`/`maxBy` | [§22](#22-aggregate-family) | `InvalidOperationException` |
| 입력 끝에서 읽기 | [§17.7](#177-입력의-전제조건-위반) | `EndOfStreamException` |
| 형식에 맞지 않는 입력 토큰 | [§17.7](#177-입력의-전제조건-위반) | `FormatException` |
| 범위를 넘는 입력 값 | [§17.7](#177-입력의-전제조건-위반) | `OverflowException` |
| range 경계 오버플로 | [§14.4](#144-경계-오버플로) | `OverflowException` |
| `sum`/`sumBy`/정수 `pow` 오버플로 | [§22.6](#226-정수-누산-타입) | `OverflowException` |
| `abs(int.MinValue)`, 음수 지수, `pow(a, e, m)`의 `m < 1`, 음수 `bitLength`, `lo > hi`인 `clamp` | [§23](#23-math-family) | `ArgumentException` 또는 `OverflowException` |
| 변환 키워드의 파싱 실패, 범위 초과 | [§21.2](#212-전제조건-위반) | `FormatException` / `OverflowException` |
| 순회 중 receiver 구조 변경 | [§16.4](#164-fast-iteration) | `InvalidOperationException` |
| 정렬되지 않은 receiver의 `lowerBound`/`upperBound` | [§24.3](#243-목록) | (Debug 검사 선택) |

---

## 30. 런타임 계약

### 30.1 fast I/O baseline

- 입력: `Console.OpenStandardInput()` + 버퍼 reader (64 KiB 이상 권장)
- 출력: `Console.OpenStandardOutput()` + 버퍼 writer (64 KiB 이상 권장), BOM 없는 UTF-8
- 숫자 입출력은 InvariantCulture ([§18.4](#184-culture))

### 30.2 생성 C#과 대상 런타임

- 생성 C#은 C# 10 문법 범위 안에 있어야 한다.
- reference backend는 기본적으로 최신 .NET을 대상으로 하고, `--older` 옵션으로 net6.0을 대상으로 할 수 있다.

### 30.3 출력 버퍼와 종료

- `stdout`은 버퍼를 쓴다. **정상 종료** 시(최상위 문장의 끝, 최상위 `return`) 반드시 flush한다.
- `Console.Out`은 `stdout`과 **같은 버퍼**로 연결해야 한다. 그래서 pass-through `Console.Write`와 `+=`를 섞어도 출력 순서가 유지된다.
- `Console.In` / `Console.ReadLine()`을 `stdin`과 섞어 쓰면 결과는 미정이다. 입력은 `stdin`만 쓴다.
- **비정상 종료**(잡히지 않은 예외, `Environment.Exit`) 시에는 버퍼에 남은 출력이 사라질 수 있다. 트랜스파일러는 이를 막기 위한 `try/finally`를 기본으로 생성하지 않는다. 함수 안에서 프로그램을 끝내야 하면 `stdout.flush()` 후 `Environment.Exit(0)`을 호출한다.
- **인터랙티브 문제**에서는 응답을 기다리기 전에 `stdout.flush()`를 호출해야 한다.

### 30.4 재귀 깊이

언어는 재귀 깊이를 제한하지 않는다. 기본 스레드 스택으로 부족한 깊은 재귀를 위해, reference backend는 opt-in 옵션 `--large-stack`(256 MB 스택 스레드에서 실행)을 제공한다. 이 옵션은 언어 의미를 바꾸지 않는다.

---

## 31. helper 생성 정책

- 생성 프로그램에는 **실제로 쓴 helper만** 넣는다. 거대한 runtime helper 묶음을 매번 통째로 넣지 않는다.
- 제거 대상인 "쓰지 않는 `using`"은 **트랜스파일러가 생성한 `using`만**이다. 사용자가 쓴 `using`은 그대로 보존한다. 확장 메서드를 들여오는 `using`을 지우면 코드가 깨질 수 있기 때문이다. 쓰이지 않는 사용자 `using`은 경고할 수 있다.
- simple shape → direct lowering, rare / escaped / general shape → helper 허용. 단 fallback helper가 hot path의 기본 경로가 되어서는 안 된다.

---

## 32. anti-pattern

v0.7에서 피해야 할 대표적인 잘못된 생성 방향:

1. simple numeric range를 helper enumerable로 낮추기
2. shorthand input을 generic `read<T>()` dispatcher로 낮추기
3. fixed-shape input을 LINQ `Select(...).ToArray()`로 읽기
4. generator를 받은 aggregate를 중간 materialization 후 처리하기
5. known DS rewrite를 wrapper object로 감싸기
6. `new![n]`을 LINQ initialization으로 구현하기
7. Release 빌드에 인위적인 `throw`/검사 남기기
8. discard를 `_ = loopVar;` 같은 의미 없는 문장으로 남기기
9. user-defined symbol보다 intrinsic 이름을 우선 해석하기
10. local function을 lambda thunk로 변환하기
11. 생성 식별자를 사용자 이름 공간에 두기 (`stdin`, `stdout`을 그대로 필드 이름으로 쓰기)
12. 정수 `pow`를 `Math.Pow`로 낮추기
13. culture에 의존하는 문자열 비교나 숫자 서식을 쓰기
14. PSCP 단계에서 진단해야 할 오류를 **컴파일되지 않는 C#**으로 내보내기

---

## 33. 예제

### 33.1 격자 BFS

```txt
int h, w =
char[h][w] board =                    // 줄마다 공백이 없어도, 있어도 된다

int[][] dist = [0..<h -> _ do [0..<w -> _ do -1]]
Queue<(int, int)> q
dist[0][0] = 0
q += (0, 0)

let dr = [1, -1, 0, 0]
let dc = [0, 0, 1, -1]

while q.Count > 0 {
    let r, c = --q
    0..<4 -> k {
        let nr = r + dr[k]
        let nc = c + dc[k]
        if nr < 0 or nr >= h or nc < 0 or nc >= w then continue
        if board[nr][nc] == '#' or dist[nr][nc] >= 0 then continue
        dist[nr][nc] = dist[r][c] + 1
        q += (nr, nc)
    }
}

+= dist[h - 1][w - 1]
```

### 33.2 다익스트라

```txt
int n, m =
List<(int, long)>[] g = new![n]
for _ in 0..<m {
    int u, v =
    long w =
    g[u - 1] += (v - 1, w)
}

let INF = long.MaxValue
long[] dist = [0..<n -> _ do INF]
PriorityQueue<int, long> pq
dist[0] = 0
pq += (0, 0L)

while pq.TryDequeue(out int u, out long d) {
    if d > dist[u] then continue
    g[u] -> (v, w) {
        if chmin(ref dist[v], d + w) then pq += (v, dist[v])
    }
}

+= [dist -> x do if x == INF then -1 else x]
```

### 33.3 좌표 압축과 구간 개수

```txt
int n, q =
int[n] a =
let sorted = a.sort()

for _ in 0..<q {
    int l, r =
    += sorted.upperBound(r) - sorted.lowerBound(l)
}
```

### 33.4 입력 끝까지 읽기

```txt
mut long total = 0
while stdin.hasNext() {
    long x =
    total += x
}
+= total
```

### 33.5 이름 충돌과 `:=`

```txt
int n =
int[] parent = [0..<n]

rec int find(int x) {
    if x == parent[x] then x
    else parent[x] := find(parent[x])
}

rec int gcd(int a, int b) {          // 사용자 gcd가 intrinsic을 가린다
    if b == 0 then a else gcd(b, a % b)
}

+= gcd(find(0) + 12, 18)
```

### 33.6 사용자 순서와 정렬

```txt
record struct Job(int Id, long Time) {
    operator<=>(other) => Time <=> other.Time
}

int n =
Job[] jobs = [0..<n -> i do new Job(i, stdin.readLong())]
let order = jobs.sort()                  // 안정 정렬, Time 오름차순
stdout.lines([order -> j do j.Id + 1])
+= jobs.min().Id + 1
```

---

## 34. 적합성

PSCP `v0.7` 구현은 다음을 만족해야 한다.

1. [§5](#5-이름-해석)의 이름 해석 순서를 지킨다. user-defined symbol이 intrinsic보다 우선한다.
2. 생성 식별자가 사용자 이름과 충돌하지 않는다([§5.4](#54-문법이-소유한-표면과-생성-이름)).
3. 이 문서가 정의한 shorthand / intrinsic / helper / pass-through 의미를 보존한다.
4. known DS rewrite를 올바른 .NET 호출로 낮춘다.
5. `=`와 `:=`를 의미상 구분한다.
6. 블록 전체 가시성의 로컬 함수, 중첩 로컬 함수, 호출 그래프 기반 `rec` 검사를 지원한다.
7. [§10.5](#105-꼬리-위치와-암묵적-반환)의 tail position과 암묵적 반환 규칙을 지킨다.
8. [§11](#11-호출)의 space-call 인자 결합 규칙과 [§13.1](#131-우선순위-표-강함--약함)의 우선순위를 지킨다.
9. [§17](#17-입력)의 토큰/문자/줄 모델과 [§18.3](#183-렌더링-규칙)의 렌더링 규칙을 지킨다. 숫자 입출력은 InvariantCulture다.
10. [§25.1](#251-기본-순서)의 기본 순서(문자열 ordinal 포함)를 PSCP 소유 순서 연산 전체에 쓴다.
11. Release 빌드에 트랜스파일러가 추가한 인위적인 `throw`를 남기지 않고, Debug 빌드에서 [§29.2](#292-전제조건-위반)의 전제조건 위반을 검출한다.
12. simple range / aggregate / shaped input을 direct lowering 우선 정책으로 처리한다.
13. [부록 B](#부록-b-진단-목록)의 필수 진단을 낸다.
14. **PSCP 진단을 통과한 프로그램은 PSCP 고유 표면 때문에 C# 컴파일 오류를 내지 않는다.** pass-through .NET 코드 자체의 오류(존재하지 않는 .NET 멤버 등)는 C# 컴파일러가 보고해도 된다.

---

## 35. 마무리

`v0.7`의 방향은 v0.6과 같다. 문법은 짧아야 하고, intrinsic API는 실전적이어야 하며, 이름 해석은 일반 언어 관습을 따라야 하고, 생성 C#은 직접적이어야 한다.

v0.7이 더한 것은 **"조용히 틀리지 않는다"**는 원칙이다. 같은 모양이 문맥에 따라 다른 뜻이 되는 곳(인덱서 안의 `..`, `min (a, b)`, `f -1`)은 뜻을 하나로 정하거나 오류로 만들었다. 입력·출력·수치·순서처럼 정답을 좌우하는 의미는 모두 표로 확정했다. 그리고 PSCP 단계에서 잡을 수 있는 오류는 C# 컴파일러에 떠넘기지 않는다.

---

## 부록 A. 문법 요약

EBNF 비슷한 표기로 쓴 요약이다. 정확한 해석은 본문 규칙(특히 [§4](#4-문장-줄바꿈-블록) 줄 이어짐, [§11](#11-호출) space-call, [§13.9](#139-패턴-문맥) 패턴 문맥)을 따른다. 타입, 패턴, 생성자, 속성, switch 식 arm 등 C# pass-through 문법은 C# 10 문법을 참조한다.

```ebnf
program        = { top-item } ;
top-item       = using-directive | namespace-decl | type-decl | function-decl | statement ;

(* ---------- 문장 ---------- *)
statement      = block
               | input-decl | output-stmt | decl-stmt
               | if-stmt | while-stmt | for-in-stmt | c-for-stmt | iterate-stmt
               | try-stmt | "throw" expr
               | "break" | "continue" | "return" [ expr ]
               | function-decl
               | expr ;
block          = "{" { statement END } "}" ;
END            = NEWLINE | ";" ;                    (* §4.2 이어짐 규칙 적용 *)

input-decl     = [ "mut" ] type IDENT { "," IDENT } "=" STMT-END
               | [ "mut" ] sized-type IDENT "=" STMT-END ;
output-stmt    = ( "=" | "+=" ) expr ;              (* 문장 시작에서만 *)
decl-stmt      = ( "let" | "var" ) IDENT { "," IDENT } "=" expr
               | [ "mut" ] type IDENT { "," IDENT } [ "=" expr ]
               | [ "mut" ] type IDENT "=" expr { "," IDENT [ "=" expr ] }   (* 이름마다 초기화 *)
               | [ "mut" ] sized-type IDENT
               | "(" typed-ident { "," typed-ident } ")" "=" expr ;
sized-type     = type "[" expr "]" { "[" expr "]" } ;

if-stmt        = "if" expr ( block | "then" statement )
                 [ "else" ( block | if-stmt | statement ) ] ;
while-stmt     = "while" expr ( block | "do" statement ) ;
for-in-stmt    = "for" iter-binding "in" expr ( block | "do" statement ) ;
c-for-stmt     = "for" "(" [ stmt-list ] ";" [ expr ] ";" [ expr-list ] ")" ( block | statement ) ;
iterate-stmt   = expr "->" iter-binding block ;
iter-binding   = item-binding | IDENT "," item-binding ;
item-binding   = IDENT | "_" | "(" item-binding { "," item-binding } ")" ;

function-decl  = [ "rec" ] { modifier } type IDENT [ type-params ] "(" [ params ] ")"
                 [ constraints ] ( block | "=>" expr ) ;

(* ---------- 식 (약함 → 강함) ---------- *)
expr           = lambda | assignment ;
assignment     = conditional [ assign-op expr ] ;
assign-op      = "=" | ":=" | "+=" | "-=" | "*=" | "/=" | "%=" | "&=" | "|=" | "^="
               | "<<=" | ">>=" | "??=" ;
conditional    = coalesce [ "?" expr ":" expr ] ;
coalesce       = or-expr [ "??" coalesce ] ;
or-expr        = and-expr { ( "||" | "or" ) and-expr } ;
and-expr       = bor-expr { ( "&&" | "and" ) bor-expr } ;
bor-expr       = xor-expr { "|" xor-expr } ;
xor-expr       = band-expr { ( "^" | "xor" ) band-expr } ;
band-expr      = eq-expr { "&" eq-expr } ;
eq-expr        = rel-expr { ( "==" | "!=" ) rel-expr } ;
rel-expr       = pipe-expr { rel-op pipe-expr | "is" pattern | "as" type } ;
rel-op         = "<" | "<=" | ">" | ">=" | "<=>" ;
pipe-expr      = range-expr { "|>" pipe-target }
               | pipe-target "<|" pipe-expr ;       (* 한 사슬에 |>와 <| 혼용 금지 *)
pipe-target    = application | "(" expr ")" ;
range-expr     = shift-expr [ range-op shift-expr [ range-op shift-expr ] ] ;
range-op       = ".." | "..<" | "..=" ;
shift-expr     = add-expr { ( "<<" | ">>" ) add-expr } ;
add-expr       = mul-expr { ( "+" | "-" ) mul-expr } ;
mul-expr       = sw-expr { ( "*" | "/" | "%" ) sw-expr } ;
sw-expr        = unary [ "switch" switch-body | "with" init-body ] ;
unary          = ( "+" | "-" | "!" | "not" | "~" | "++" | "--" | cast ) unary
               | application ;
application    = postfix { WS group } ;             (* head가 호출 가능할 때만 group이 붙는다 *)
group          = [ "ref" | "out" | "in" ] postfix   (* postfix는 부호 연산자로 시작할 수 없다 *)
               | "(" expr { "," expr } ")" ;
postfix        = primary { postfix-op } ;           (* 후위 연산은 공백 없이 붙는다 *)
postfix-op     = "." IDENT | "?." IDENT | "." INT-LITERAL | "[" index-args "]" | "?[" expr "]"
               | "(" [ args ] ")" | "++" | "--" | "!" ;
primary        = literal | IDENT | "_" | "this" | "base" | type-keyword
               | "(" expr ")" | "(" expr "," expr { "," expr } ")"
               | collection | generator | interpolated
               | new-expr | if-expr | comparer ;
if-expr        = "if" expr "then" expr "else" expr ;
comparer       = type "." ( "asc" | "desc" ) ;
collection     = "[" [ coll-elem { "," coll-elem } ] "]"
               | "[" expr "->" iter-binding "do" expr "]" ;
coll-elem      = ".." expr | expr ;                 (* range 문법 원소는 자동 펼침 *)
generator      = "(" expr "->" iter-binding "do" expr ")" ;
new-expr       = "new" "[" expr "]" { "[" expr "]" }
               | "new!" "[" expr "]"
               | "new" [ type ] "(" [ args ] ")" [ init-body ] ;
index-args     = index | slice ;
index          = [ "^" ] expr ;
slice          = [ index ] ( "..<" | "..=" ) index | index ".." | ".." ;
```

---

## 부록 B. 진단 목록

### B.1 오류

| 조건 | 절 |
|---|---|
| `__pscp`/`__Pscp`로 시작하는 사용자 식별자 | §3.2 |
| 선언이 아닌 곳의 줄 끝 `=` | §4.3 |
| 같은 scope에서 변수와 함수의 이름 충돌 | §5.1 |
| 지원하지 않는 C# 문장 (`foreach`, `do-while`, `switch` 문, `goto` 등) | §6.3 |
| 타입 안에서 상수가 아닌 최상위 변수나 최상위 함수를 참조 | §7.4 |
| 초기화 없는 불변 지역 선언 (known collection, 크기 있는 배열 제외) | §9.3 |
| 한 괄호 안에서 선언과 기존 변수를 섞은 구조 분해 | §9.5 |
| 재귀 순환에 속한 함수의 `rec` 누락 | §10.3 |
| 값 반환 함수의 경로 끝이 값이 아님, tail 식 타입이 `void` | §10.5 |
| space-call head가 호출 불가 | §11.2 |
| 함수(메서드 그룹)에서의 뺄셈 (`f -1`), 인자가 모자란 application 뒤의 뺄셈 | §11.3 |
| 문장 시작 `A b`에서 `A`가 타입도 호출 가능한 것도 아님 | §11.5 |
| 식 위치 `if`에 `else` 없음 | §12.1 |
| pipe 대상이 호출 불가, `\|>`와 `<\|` 혼용 | §13.3 |
| known DS에 후위 `++`/`--` | §13.5, §26 |
| 상수 step 0, `char`와 정수 bound 혼합 | §14 |
| 인덱서 안의 `a..b`, `..b`, stepped range | §15.2 |
| 지원하지 않는 슬라이스 대상 | §15.3 |
| target type 없는 빈 `[]`, 공통 원소 타입 없음, `[]` 안의 `..<b` | §16.1 |
| 타입 없는 입력 shorthand, token-readable이 아닌 입력 타입 | §17.1, §17.6 |
| 3단 이상 중첩 컬렉션의 자동 렌더링 | §18.3 |
| 스칼라 하나만 받은 `min`/`max` | §22.2 |
| 기본 순서가 없는 타입의 순서 연산 | §25.1 |
| known DS `+=`/`-=`의 피연산자 타입 불일치 | §26.2 |
| target type 없는 `new[n]`, 매개변수 없는 생성자가 없는 `new![n]` 원소 타입 | §27 |
| 그 밖에 PSCP 고유 표면의 타입 오류 (C#으로 넘기지 않는다) | §34 |

### B.2 경고

| 조건 | 절 |
|---|---|
| 결과를 쓰지 않는 단항식 문장 (`- b`) | §4.3 |
| 초기화 전 최상위 변수를 읽을 수 있는 최상위 호출 | §7.3 |
| 불변 바인딩/필드 수정 | §9.2, §28.2 |
| 재귀하지 않는 함수의 `rec`, 타입 멤버의 `rec` | §10.3 |
| 식 안의 `=`, 연쇄 `=` | §13.4 |
| 관계/동등 연산자 왼쪽의 `not X` | §13.7 |
| 결과를 버리는 collection helper 호출 | §24.4 |
| 폐기 예정 이름 사용 | §17.8, §24.5, §27.4 |
| 비교자 없이 만든 문자열 key 정렬 구조, 비교자 없는 문자열 배열/리스트의 `Array.Sort`/`Sort()` | §25.4 |
| 어디서도 대입하지 않는 불변 필드 | §28.2 |

---

## 부록 C. 폐기 예정 목록

v0.7에서는 경고와 함께 동작하고, v0.8에서 제거한다.

| 폐기 이름 | 대체 | 절 |
|---|---|---|
| `stdin.int()` 등 짧은 별칭 | `stdin.readInt()` 등 | §17.8 |
| `readTuple2`, `readTuple3` | `readTuple<…>()` | §17.8 |
| `readTuples2`, `readTuples3` | `readArray<(…)>(n)` | §17.8 |
| `readGridInt`, `readGridLong`, `readNestedArray` | `readGrid<T>(n, m)` | §17.8 |
| `groupCount()` | `freq()` | §24.5 |
| `Array.zero(n)` | `new[n]` | §27.4 |

---

## 부록 D. v0.6 대비 변경 요약

항목별 before/after 코드와 이유는 동반 문서 `pscp_v_0_7_changes.md`에 있다.

| 영역 | v0.6 | v0.7 | 기존 코드 영향 |
|---|---|---|---|
| 암묵적 반환 | 호출 식은 반환 대상 아님 | 호출 식도 반환, `if`/`else` 분기 전파 | 없음 (거부되던 코드가 통과) |
| pipe 우선순위 | `\|\|`보다 약함 | range와 관계 연산자 사이 | 드묾 |
| pipe → helper | 정의 모순 | `xs \|> filter(p)` = `xs.filter(p)` | 없음 |
| 인덱서 안 `..` | C# 의미(끝 제외) pass-through | `..<`/`..=` 필수 | **있음**: `arr[a..b]`는 오류 → 수정 필요 |
| 생성 이름 | `stdin`/`stdout` 그대로 | 예약 접두사 | 없음 |
| `rec` | 연속 선언만 상호 재귀 | 호출 그래프 기반 | 없음 |
| 타입 멤버 | C# 기본 private, 가변성 미정 | 기본 public, 지역 선언과 같은 가변성 | 경고 증가 가능 |
| `readChar` | 미정 (구현: 토큰 첫 글자) | 문자 하나 | **있음**: 동작 변경 (올바른 쪽으로) |
| `readLine` | "다음 줄 전체" | cursor 모델 (구현과 일치) | 없음 |
| EOF 검사 | 없음 | `hasNext`, `hasNextLine` | 없음 |
| 출력 렌더링 | 미정 | 표로 확정 (`bool` 소문자, 지수 표기 금지, `char[]` 붙여 쓰기, 2차원은 줄 단위) | **있음**: 출력 모양 변경 |
| `round` | 미정 (구현: 짝수 반올림) | 0에서 먼 쪽 | **있음** |
| `bool "문자열"` | 비어 있지 않으면 `true` | `readBool`과 같은 파싱 | **있음** |
| `sum` 누산 | 미정 | 원소 타입, target-typed 확장, Debug 검출 | 없음 |
| 정수 `pow` | 미정 (구현: `Math.Pow`) | 정확한 정수, 모듈러 거듭제곱 | **있음**: 결과 타입 변경 |
| 빈 `min` 등 | 미정 | 전제조건 위반 (Debug 예외) | 없음 |
| range | step 0, 오버플로, `char` 미정 | 확정, `char` range 추가 | 없음 |
| space-call | `min (a, b)`는 튜플 | 괄호 그룹 인자 결합 | 없음 (C# 오류이던 코드) |
| `f -1` | C# 오류 생성 | PSCP 오류 + 수정 제안 | 없음 |
| 패턴 `and`/`or` | 파싱 오류 | C# 패턴 결합자 | 없음 |
| 문자열 순서 | culture | ordinal | **있음**: 정렬 결과 변경 (올바른 쪽으로) |
| helper 결과 | 스펙: receiver family 유지 / 구현: 배열 | 항상 배열 | 없음 (구현과 일치) |
| 정렬 안정성 | 미정 | 안정 | 없음 |
| 추가 API | — | `lowerBound`, `upperBound`, `find(pred, fallback)`, `readGrid<T>`, `readTuple<…>`, `floor(a,b)`/`ceil(a,b)`, `pow(a,e,m)` | 없음 |
