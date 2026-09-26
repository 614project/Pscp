# PSCP v0.7 초안 변경 해설 (v0.6 → v0.7)

이 문서는 `pscp_v_0_7_spec.md`(초안)가 v0.6 스펙의 어떤 문제를 어떻게 해결했는지 항목별로 설명한다. 각 항목은 v0.6 코드와 실제 결과, v0.7에서 바뀐 규칙과 코드를 나란히 보여 준다.

## 읽는 법

각 항목은 다음 순서로 되어 있다.

- **문제**: v0.6 스펙의 어느 부분이 모순되거나 비어 있었는가
- **v0.6 코드 / 실제 결과**: 문제를 드러내는 코드와 그 결과
- **v0.7 해결**: 바뀐 규칙
- **v0.7 코드**: 바뀐 규칙을 따른 코드
- **기존 코드 영향**: v0.6 코드가 v0.7에서 어떻게 되는가
- **스펙**: v0.7 스펙의 해당 절

**문제** 단락의 절 번호(§)는 **v0.6 스펙** 기준이고, **스펙** 줄의 절 번호는 **v0.7 초안** 기준이다.

"실제 결과"는 저장소의 트랜스파일러 **0.6.7**(커밋 `6595d78`)을 빌드해 Release 구성으로 실행하거나 변환해서 얻은 결과다. "C# 오류"라고 적은 것은 PSCP 단계는 통과했지만 생성된 C#이 컴파일되지 않았다는 뜻이다.

## 결정 기준

결정할 사항이 많아서, 다음 네 가지 기준으로 일관되게 골랐다.

1. **조용히 틀리지 않는다.** 같은 모양이 문맥에 따라 두 가지 뜻을 가지면, 하나로 정하거나 오류로 만든다.
2. **이미 합리적으로 동작하는 코드는 깨지 않는다.** 스펙과 구현이 다르고 구현 쪽이 합리적이면 스펙을 구현에 맞췄다. 저장소 테스트 코드 24개는 v0.7에서 바뀌는 기능을 하나도 쓰지 않는다.
3. **채점 환경이 기대하는 결과를 기본값으로 한다.** ASCII 문자열 순서, 지수 표기 없는 실수 출력, 정확한 정수 연산이 여기에 해당한다.
4. **비용 원칙은 유지한다.** 새로 생긴 검사는 모두 Debug 전용이고, Release 생성 코드는 v0.6과 같은 수준으로 직접적이다.

---

## 요약 표

| # | 항목 | 0.6.7에서 실제로 일어난 일 | 기존 코드 영향 |
|---|---|---|---|
| A1 | 호출로 끝나는 함수 | 스펙 예제가 PSCP 오류 | 없음 |
| A2 | pipe로 collection helper 호출 | 스펙 예제가 PSCP 오류 | 없음 |
| A3 | 인덱서 안의 `..` | `[1..3]`은 3개, `arr[1..3]`은 2개 | **있음** (오류로 바뀜) |
| A4 | `stdin`을 가리면 입력이 깨짐 | C# 오류 | 없음 |
| A5 | `rec` 상호 재귀 조건 | 스펙과 구현이 다름 | 없음 |
| A6 | 타입 멤버 접근성/가변성 | 스펙 예제가 C# 오류 | 경고 증가 가능 |
| A7 | helper 결과 컬렉션 종류 | 스펙과 구현이 다름 | 없음 |
| B1 | `readChar`, 문자 격자 | 격자를 잘못 읽음 | **있음** (올바르게 바뀜) |
| B2 | 토큰 뒤의 `readLine` | 스펙과 구현이 다름 | 없음 |
| B3 | 입력 끝 검사 | API 없음 | 없음 (추가) |
| B4 | 출력 렌더링 | `True`, `1E-05`, `a b`, 2차원 평탄화 | **있음** |
| B5 | `round` | `round 2.5` = `2` | **있음** |
| B6 | 변환 키워드 | `bool "false"` = `true` | **있음** |
| B7 | `sum` 오버플로 | 조용히 음수 | 없음 |
| B8 | 정수 `pow` | `Math.Pow`, 정밀도 손실 | **있음** (결과 타입) |
| B9 | 빈 시퀀스의 `min` | 조용히 `0` | 없음 |
| B10 | range 경계 | step 0 무한 루프, `MaxValue` 무한 루프 | 없음 |
| B11 | 순회 중 컬렉션 변경 | 스펙이 두 동작을 모두 허용 | 없음 |
| B12 | 정렬 안정성, `distinct` 순서 | 미정 | 없음 |
| B13 | 문자열 순서 | culture 순서 (`a A b B`) | **있음** (올바르게 바뀜) |
| B14 | 결과를 버리는 `arr.sort()` | 조용히 아무 일도 없음 | 경고 추가 |
| B15 | `find` 반환 타입 | 제네릭에서 성립하지 않는 규칙 | 없음 |
| B16 | `chmin`/`chmax` 반환값 | 미정 (구현은 `bool`) | 없음 |
| B17 | 최상위 변수 초기화 순서, 타입 안의 상수 | 조용히 `0`, C# 오류 | 없음 |
| C1 | 형식 문법 | 없음 | — |
| C2 | `min (a, b)` | C# 오류 | 없음 |
| C3 | `f -1` | C# 오류 | 없음 |
| C4 | `xs \|> sum == 10` | C# 오류 | 드묾 |
| C5 | `not a == b` | C# 오류 | 경고 추가 |
| C6 | `f p.1` | 동작하지만 스펙과 모순 | 없음 |
| C7 | 줄 이어짐 | 스펙에 규칙 없음, 앞 `.` 오류 | 없음 |
| C8 | 패턴 `and`/`or` | PSCP 파싱 오류 | 없음 |
| C9 | 빠진 연산자와 C# 문장 | 무엇이 되는지 알 수 없음 | 없음 |
| C10 | `[]` 리터럴 | `[]`가 `object[]` | 드묾 |
| C11 | member alias와 이름 가리기 | 동작하지만 스펙에 없음 | 없음 |
| C12 | DS `+=`를 반환값으로 | PSCP 오류 | 없음 |
| C13 | 다중 선언, `Foo bar` | 스펙에 없음 | 없음 |
| D1–D9 | API 정리, 런타임 계약 | 이름 불일치, 중복 API, 문서에 없는 동작 | 폐기 경고 |
| E | 문서 체계 | 규범 수준, 용어, 목차 | — |

---

## A. 문서 안의 모순

### A1. 호출로 끝나는 함수가 값을 반환하지 못함

**문제.** v0.6 §12.7은 "bare invocation statement"를 암묵적 반환 대상에서 뺐다. 그런데 §11.1과 §33.5의 예제는 `inner(x)`로 끝나는 함수다. 스펙 스스로의 예제가 스펙 규칙에 어긋났다. 또 `visited.Add(x)`는 반환되지 않고 `not visited.Add(x)`는 반환되는 비대칭도 있었다.

**v0.6 코드 / 실제 결과**

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
+= solve(3)
```

```
1:5: error: `solve` must end with a return value of type `int`. Use `return`, a return-eligible expression, or `:=` for value-yielding assignment.
```

**v0.7 해결.** 반환 타입이 `void`가 아닌 함수에서는 **호출 식도 반환 대상**이다. 반환 대상에서 빠지는 것은 일반 대입(`=`), 일반 복합 대입(`+=` 등), 일반 증감(`x++`)뿐이다. 또 "tail position"을 정의해서 `if`/`else` 블록의 각 분기 마지막 식도 반환되도록 했다(구현은 이미 이렇게 동작한다).

**v0.7 코드**

```txt
int solve(int x) {
    int inner(int y) { deep(y) + x }
    int deep(int z) { z + 1 }
    inner(x)                         // 반환
}

int sign(int v) {
    if v > 0 { 1 } else if v < 0 { -1 } else { 0 }
}
```

- **기존 코드 영향**: 없음. 거부되던 코드가 통과한다. `void` 함수는 바뀌지 않는다.
- **스펙**: §10.5

### A2. pipe로 collection helper를 부를 수 없음

**문제.** v0.6 §13.3은 `xs |> filter(pred)`가 `filter(xs, pred)`라고 정의했다. 그런데 §24.1은 collection helper를 member 호출 전용으로 정했으므로 `filter(xs, pred)`라는 함수는 없다. 게다가 §13.3 안에서도 "파서는 member access 대상을 호출로 바꾼다"는 문단과 "member-call 대상은 canonical이 아니다"라는 문단이 충돌했다.

**v0.6 코드 / 실제 결과**

```txt
let xs = [1, 2, 3, 4]
let ys = xs |> filter(x => x % 2 == 0)
```

```
2:16: error: Undefined name `filter`.
```

**v0.7 해결.**

- pipe 대상의 head 자리에 collection helper 이름이 오면 **receiver 삽입**으로 rewrite한다: `xs |> filter(p)`는 `xs.filter(p)`.
- member 경로 대상(`x |> Math.Abs`)은 정식으로 허용한다. 구현이 이미 이렇게 동작한다.
- 사용자가 같은 이름을 선언했으면 사용자 선언이 이긴다(일반 이름 해석).

**v0.7 코드**

```txt
let evens = xs |> filter(x => x % 2 == 0)       // xs.filter(...)
let total = xs |> map(x => x * x) |> sum        // sum (xs.map(...))
let a = -5 |> Math.Abs                          // Math.Abs(-5)
```

- **기존 코드 영향**: 없음.
- **스펙**: §13.3

### A3. 인덱서 안의 `..`가 끝을 포함하지 않음

**문제.** PSCP에서 `a..b`는 끝을 **포함**한다(§14). 그런데 §20.4는 슬라이싱을 C# 그대로 넘긴다고 했고, C#의 `a..b`는 끝을 **제외**한다. 같은 모양이 괄호 종류에 따라 반대 뜻이 되어 off-by-one 버그가 생기기 쉽다. 또 C#의 `List<T>`는 범위 인덱서를 지원하지 않아서 리스트에 쓰면 C# 오류가 났다.

**v0.6 코드 / 실제 결과**

```txt
let arr = [10, 20, 30, 40, 50]
+= [1..3]
+= arr[1..3]
```

```
1 2 3
20 30
```

**v0.7 해결.** 인덱서 안에서는 **끝 포함 여부를 반드시 명시**한다.

- `..<`(끝 제외), `..=`(끝 포함), 열린 형태 `a..`, `..`만 쓸 수 있다.
- 인덱서 안의 `a..b`, `..b`는 오류이며, 진단이 두 가지 수정안을 보여 준다.
- `List<T>`의 슬라이스는 `GetRange`로 낮춘다.

이렇게 하면 C# 관습을 기대한 사람도, PSCP 관습을 기대한 사람도 조용히 틀리지 않는다. 덕분에 range로 쓸 때는 `a..b`와 같은 뜻이라 중복처럼 보이던 `..=`에도 역할이 생겼다(D4).

**v0.7 코드**

```txt
+= arr[1..<3]      // 20 30
+= arr[1..=3]      // 20 30 40
+= text[1..<^1]    // 첫 글자와 마지막 글자를 뺀 부분
arr[1..3]          // 오류: 인덱서 안의 `..`는 끝 포함 여부가 모호합니다. arr[1..<3] 또는 arr[1..=3]
```

- **기존 코드 영향**: **있음.** 인덱서 안에서 `a..b`나 `..b`를 쓴 코드는 오류가 되므로 `..<`로 고쳐야 한다(의미 유지). 저장소 테스트 코드에는 해당 사례가 없다.
- **스펙**: §15

### A4. `stdin`이라는 이름을 선언하면 입력 shorthand가 깨짐

**문제.** v0.6 §3.3은 "입력 shorthand는 이름 가리기의 영향을 받지 않는다"고 약속했다. 하지만 생성 코드가 입출력 객체를 `stdin`, `stdout`이라는 이름의 필드로 만들기 때문에, 사용자가 같은 이름을 선언하면 생성 코드가 사용자 변수를 가리킨다.

**v0.6 코드 / 실제 결과**

```txt
let stdin = 3
int n =
+= n + stdin
```

```
error CS1061: 'int' does not contain a definition for 'readInt' ...
```

**v0.7 해결.** 생성 코드의 모든 식별자(입출력 객체, helper, 임시 변수, 진입점)는 **예약 접두사 `__pscp`/`__Pscp`**를 쓰거나 충돌하지 않음이 보장된 이름이어야 한다. 사용자가 `__pscp`로 시작하는 이름을 쓰면 오류다. 사용자 이름은 생성 코드에서 그대로 유지하고, 충돌이 불가피하면(최상위 함수 `Main` 등) 생성 쪽 이름을 바꾼다.

**v0.7 코드 (생성 C#)**

```csharp
const int stdin = 3;
int n = __pscp_stdin.readInt();
__pscp_stdout.writeln(n + stdin);
```

- **기존 코드 영향**: 없음.
- **스펙**: §3.2, §5.4, §7.5

### A5. `rec`과 상호 재귀의 조건

**문제.** v0.6 §11.2는 "로컬 함수는 block 전체에 바인딩된다"고 했고, §11.3은 "같은 block에서 **연속해서** 선언된 `rec` 함수들만 상호 재귀 그룹"이라고 했다. 두 규칙이 서로 맞지 않는다. 떨어진 `rec` 함수끼리 부르거나, `rec` 없는 함수가 뒤에 선언된 함수를 부를 때의 결과가 정의되지 않았다. 구현은 떨어진 `rec` 함수의 상호 재귀도 받아들인다.

**v0.6 코드 / 실제 결과**

```txt
rec bool isEven(int n) {
    if n == 0 then true else isOdd(n - 1)
}
int unrelated() { 42 }
rec bool isOdd(int n) {
    if n == 0 then false else isEven(n - 1)
}
```

v0.6 스펙으로는 오류여야 하지만 구현은 통과시킨다.

**v0.7 해결.**

- 함수는 선언된 **블록 전체**에서 보인다. 앞에서 불러도 된다(C#과 같다).
- `rec`은 "재귀한다"는 **검사되는 표시**다. 호출 그래프에서 순환(자기 호출 또는 강연결 요소)에 속한 함수는 모두 `rec`을 붙여야 하며, 빠뜨리면 오류다. 선언이 연속해 있을 필요는 없다.
- 재귀하지 않는 함수의 `rec`은 경고다.
- 타입 메서드는 C# 멤버 의미를 따르므로 `rec`이 필요 없다. 구현도 이미 그렇다.

**v0.7 코드**: 위 코드가 그대로 유효하다.

- **기존 코드 영향**: 없음. 필요 없는 `rec`에 경고가 생길 수 있다.
- **스펙**: §10.2, §10.3

### A6. 타입 멤버의 접근성과 가변성

**문제.**

- v0.6 §8.4는 "명시 타입 선언은 불변"이라고 했지만, §28의 `class Node { int value }`, `struct Edge { int to; int w }` 예제는 가변 필드처럼 쓰인다.
- 구현은 필드를 C# 기본값인 **private**으로 생성한다. 그래서 스펙 예제를 클래스 밖에서 쓰면 컴파일되지 않는다.
- §8.7은 불변 필드를 `const`/`readonly`로 낮춘다고 했지만 구현은 그렇게 하지 않는다.

**v0.6 코드 / 실제 결과**

```txt
class Node {
    int value
    let k = 3
}
let nd = new Node()
nd.value = 3
+= nd.value
```

```
error CS0122: 'Node.value' is inaccessible due to its protection level
```

생성된 필드는 `int value;`, `int k = 3;`이다(`k`도 `const`가 아니다).

**v0.7 해결.**

- 타입 멤버의 **기본 접근성은 `public`**이다. PS 코드에서 캡슐화 때문에 컴파일이 안 되는 것은 이득이 없다. 명시한 한정자는 보존한다.
- **필드의 가변성은 지역 선언과 같다**: `T name`/`let name`은 불변, `mut T name`/`var name`은 가변. 불변 필드를 생성자 밖에서 바꾸면 경고이며(오류가 아니다), 그 필드는 일반 필드로 생성한다.
- 불변 필드는 `const`/`static readonly`/`readonly`로 낮춘다.
- known collection 타입 필드의 auto-construction, 상수 크기 배열 필드(`Node?[26] next`)를 허용한다.

**v0.7 코드**

```txt
class Node {
    mut int value
    List<Node> children            // = new()
    let k = 3                      // const int k = 3
}

class TrieNode {
    TrieNode?[26] next
    mut bool terminal
}
```

- **기존 코드 영향**: 필드를 가변으로 쓰던 코드에 경고가 생긴다(동작은 같다). `mut`을 붙이면 사라진다. 저장소 테스트 코드는 positional `record struct`만 써서 해당하지 않는다.
- **스펙**: §28.2

### A7. collection helper 결과의 컬렉션 종류

**문제.** v0.6 §24.2는 receiver가 `List<T>`이면 결과도 `List<T>`라는 "family preservation" 규칙을 정했다. 그런데 구현은 **항상 배열**을 반환하고, 저장소 테스트 코드(`08_coordinate_compression...`)도 배열이라고 가정하고 `.Length`를 쓴다.

**v0.6 코드 / 실제 결과**

```txt
List<int> values
values += 3
let sv = values.sort()
+= sv.Length              // 스펙대로라면 List라서 .Length가 없다
```

생성 C#: `int[] sv = ...` (배열)

**v0.7 해결.** 스펙을 구현에 맞췄다. **컬렉션을 만드는 helper의 결과는 항상 `T[]`**다. 규칙이 하나라서 기억하기 쉽고, 배열이 PS 코드에서 가장 많이 쓰이는 형태다. `List<T>`가 필요하면 target-typed 컬렉션 식을 쓴다.

**v0.7 코드**

```txt
let sorted = values.sort().distinct()      // int[]
List<int> asList = [..values.sort()]      // List<int>
```

- **기존 코드 영향**: 없음 (구현과 같다).
- **스펙**: §24.2

---
## B. 정답을 좌우하는 의미

### B1. `readChar`와 문자 격자 입력

**문제.** v0.6은 `readChar()`의 의미를 정의하지 않았다. 구현은 "다음 **토큰**의 첫 글자"를 반환한다. 그래서 대표 예제인 `char[h][w] board =`가 가장 흔한 격자 입력(`#.#`처럼 공백 없는 줄)에서 칸 하나마다 토큰 하나를 통째로 소비한다.

**v0.6 코드 / 실제 결과**

```txt
// 입력:
// 2 3
// #.#
// ..#
int h, w =
char[h][w] board =
+= board[0]
+= board[1]
```

Release 결과: `board[0]`은 `['#', '.', '\0']`, `board[1]`은 `['\0', '\0', '\0']`이다. 토큰 `#.#`에서 `#`, 토큰 `..#`에서 `.`을 가져간 뒤 입력이 끝났기 때문이다. 입력이 더 있었다면 **다음 줄의 숫자까지 격자로 읽는다**(Debug에서는 입력 끝 예외).

**v0.7 해결.**

- `readChar()`는 **공백을 건너뛴 뒤 문자 하나만** 소비한다. 토큰의 나머지는 남는다.
- `char` 원소의 입력 shorthand는 문자 단위로 읽는다.

그래서 격자가 `#.#`처럼 붙어 있든 `# . #`처럼 떨어져 있든 같은 결과가 나온다. 공백 자체가 의미 있는 격자(미로에 빈칸이 있는 경우)는 줄 단위 reader `readCharGrid(h)`를 쓴다.

**v0.7 코드**

```txt
int h, w =
char[h][w] board =        // board[0] = "#.#", board[1] = "..#"
+= board                  // #.#⏎..#
```

- **기존 코드 영향**: **있음 (올바른 쪽으로).** `readChar`나 `char` 입력을 토큰 단위로 기대한 코드는 동작이 바뀐다. 한 글자 토큰(`a b c`)만 읽는 코드는 결과가 같다.
- **스펙**: §17.1, §17.3

### B2. 토큰을 읽은 뒤의 `readLine`

**문제.** v0.6 §17.5는 "`readLine()`은 다음 물리적 줄 **전체**"라고 했다. 구현은 같은 줄에 토큰이 남아 있으면 **그 나머지**를 반환한다. 스펙과 구현이 다르고, 스펙 문장만으로는 "남은 토큰을 버리는지"도 알 수 없다.

**v0.6 코드 / 실제 결과**

```txt
// 입력:
// 5 x y
// next
int k =
string s = stdin.readLine()     // 스펙: "next" / 구현: "x y"
```

**v0.7 해결.** 구현의 동작이 더 안전하므로(데이터를 버리지 않음) 이를 **cursor 모델**로 정확히 문서화했다.

- cursor가 줄 시작이면 그 줄 전체를 반환한다.
- cursor가 줄 중간이면 앞 공백을 건너뛴다. 남는 게 없으면 다음 줄 전체를, 남는 게 있으면 그 나머지를 반환한다.
- `readRestOfLine()`은 남은 부분을 **그대로**(앞 공백 포함, 빈 문자열 가능) 반환한다.
- 반환하는 줄에는 `\r`이 들어가지 않는다(Windows 줄 끝 입력 대응).

**v0.7 코드**

```txt
int k =
string rest = stdin.readLine()    // "x y"
string nxt = stdin.readLine()     // "next"
```

- **기존 코드 영향**: 없음 (구현과 같다).
- **스펙**: §17.4

### B3. 입력 끝(EOF)까지 읽는 방법이 없음

**문제.** "입력이 끝날 때까지 처리"하는 문제에 쓸 API가 없었다. 입력 끝에서 `readInt()`는 Release에서 조용히 `0`을 반환한다.

**v0.7 해결.** `stdin.hasNext()`(토큰이 남았는가)와 `stdin.hasNextLine()`(줄이 남았는가)을 추가했다.

**v0.7 코드**

```txt
mut long total = 0
while stdin.hasNext() {
    long x =
    total += x
}
+= total
```

- **기존 코드 영향**: 없음 (추가).
- **스펙**: §17.5

### B4. 출력 렌더링이 정의되지 않음

**문제.** v0.6은 "printable", "tuple은 small arity specialized rendering", "1D는 direct joined rendering"이라고만 했을 뿐 **출력 모양**을 정하지 않았다. 구현이 정한 모양 중 몇 가지는 채점에서 문제가 된다.

**v0.6 코드 / 실제 결과**

```txt
+= 3 > 2                        // True
+= 0.1 + 0.2                    // 0.30000000000000004
+= 1.0 / 100000.0               // 1E-05
+= ['a', 'b']                   // a b
+= [[1, 2], [3, 4]]             // 1 2 3 4
List<(int, int)> v = [(0, 1), (0, 2)]
+= v                            // 0 1 0 2
```

**v0.7 해결.** 렌더링 규칙을 표로 확정했다. 출력 shorthand, `stdout` API, 보간 문자열의 형식 없는 hole, `string x` 변환이 모두 같은 규칙을 쓴다.

| 값 | v0.6.7 | v0.7 |
|---|---|---|
| `bool` | `True` | `true` (PSCP 리터럴, `readBool`과 같은 철자) |
| 작은/큰 `double` | `1E-05` | `0.00001` (지수 표기 금지) |
| `char[]` | `a b` | `ab` |
| 2차원 컬렉션 | `1 2 3 4` (한 줄) | `1 2⏎3 4` (행마다 한 줄) |
| 튜플 리스트 | `0 1 0 2` (한 줄) | `0 1⏎0 2` (원소마다 한 줄) |
| 3단 이상 중첩 | 한 줄로 평탄화 | 오류 (명시적 루프 사용) |

숫자는 항상 InvariantCulture다.

**v0.7 코드**

```txt
+= [[1, 2], [3, 4]]        // 1 2
                           // 3 4
+= "abc".reverse()         // cba
```

- **기존 코드 영향**: **있음.** `bool`, 매우 작거나 큰 `double`, `char` 컬렉션, 2차원 컬렉션, 튜플/컬렉션의 컬렉션을 바로 출력하던 코드는 출력 모양이 바뀐다. 1차원 숫자 배열과 스칼라 출력은 같다.
- **스펙**: §18.3, §19

### B5. `round`의 반올림 방식

**문제.** v0.6은 `round`의 방식을 정하지 않았다. 구현은 `Math.Round`의 기본값인 **짝수 쪽 반올림**(banker's rounding)을 쓴다. PS에서 기대하는 것은 보통 사사오입이다.

**v0.6 코드 / 실제 결과**

```txt
+= round 2.5      // 2
+= round 3.5      // 4
```

**v0.7 해결.** `round`는 **0에서 먼 쪽으로** 반올림한다(`MidpointRounding.AwayFromZero`). `round(x, d)`로 소수 자리수를 지정할 수 있다.

**v0.7 코드**

```txt
+= round 2.5        // 3
+= round -2.5       // -3
+= round(1.25, 1)   // 1.3
```

- **기존 코드 영향**: **있음.** 정확히 .5인 값의 결과가 바뀐다.
- **스펙**: §23

### B6. 변환 키워드의 의미

**문제.** v0.6은 "direct cast/convert", "string → bool: empty false / non-empty true" 정도만 정했다.

- `int 3.7`이 버림(3)인지 반올림(4)인지 알 수 없다.
- `int '7'`이 코드 값(55)인지 숫자(7)인지 알 수 없다.
- `bool "false"`가 `true`가 된다. 같은 문자열을 `readBool`은 `false`로 읽으므로 서로 모순이다.

**v0.6 코드 / 실제 결과**

```txt
+= int 3.7         // 3
+= int '7'         // 55
+= bool "false"    // True
```

**v0.7 해결.** 변환 표를 확정했다.

- 실수 → 정수: 0 방향 버림(C# 캐스트와 같다). 반올림은 `long (round x)`.
- `char` → 정수: 코드 값. `char 65 = 'A'`와 대칭이다. 숫자 값은 `c - '0'`.
- `string` → `bool`: **파싱**(`true`/`false`/`1`/`0`). 다른 `string` → X 변환이 모두 파싱이므로 일관된다. 비어 있음 검사는 `s.Length > 0`.
- 모든 값 → `string`: 출력 렌더링 규칙(B4)을 쓴다.

**v0.7 코드**

```txt
+= int 3.7           // 3
+= int '7'           // 55
+= '7' - '0'         // 7
+= bool "false"      // false
+= s.Length > 0      // 문자열 비어 있음 검사
```

- **기존 코드 영향**: **있음.** `bool "문자열"`로 비어 있음을 검사하던 코드는 파싱으로 바뀐다(`"hello"`는 형식 오류). 나머지는 구현과 같다.
- **스펙**: §21

### B7. `sum`의 오버플로

**문제.** `sum`의 결과 타입과 오버플로 규칙이 정해지지 않았다. 구현은 원소 타입으로 누산하고 조용히 넘친다. PS에서 흔한 실수다.

**v0.6 코드 / 실제 결과**

```txt
let big = [2000000000, 2000000000]
+= sum big              // -294967296
```

**v0.7 해결.**

- 누산 타입은 원소 타입이다. C#/F#과 같다.
- 단, 호출이 **직접 target-typed**이고 target이 더 넓은 정수 타입이면 target 타입으로 누산한다. `long total = sum a`는 `long`으로 더한다. PSCP의 `new[n]`이 target 타입을 보는 것과 같은 방식이다. 구현은 이미 이렇게 동작하지만(`long total = sum big`은 `4000000000`), 스펙에는 없던 규칙이다.
- **Debug는 누산 오버플로를 검출한다.** Release는 wrap-around다(검사 비용 없음).

**v0.7 코드**

```txt
long total = sum big                 // 4000000000
long viaGen = sum (big -> x do long x)
let s = sum big                      // int. Debug에서 OverflowException
```

- **기존 코드 영향**: 없음 (구현과 같다). Debug에서 오버플로가 드러나게 된다.
- **스펙**: §22.6

### B8. 정수 `pow`가 실수로 계산됨

**문제.** `pow`의 타입이 정해지지 않았다. 구현은 모든 `pow`를 `Math.Pow`(double)로 낮춘다. 2⁵³을 넘는 정수 결과는 정밀도를 잃고, 출력도 지수 표기가 된다.

**v0.6 코드 / 실제 결과**

```txt
+= pow(3L, 39)       // 4.052555153018976E+18   (정답 4052555153018976267)
```

**v0.7 해결.**

- `pow(int, int) → int`, `pow(long, int) → long`: **정확한 정수 거듭제곱**. 누산 타입 규칙은 B7과 같다.
- `pow(double, double) → double`: 실수 거듭제곱.
- `pow(a, e, m) → long`: 모듈러 거듭제곱 (PS 필수).
- 정수 `pow`를 `Math.Pow`로 낮추는 것은 anti-pattern으로 명시했다.

**v0.7 코드**

```txt
+= pow(3L, 39)             // 4052555153018976267
+= pow(2, 10, 1000)        // 24
+= pow(2.0, 0.5)           // 1.4142135623730951
```

- **기존 코드 영향**: **있음.** 정수 인자의 `pow`는 결과 타입이 `double`에서 정수로 바뀐다. `double`이 필요하면 `pow(2.0, x)`처럼 실수 인자를 쓴다.
- **스펙**: §23

### B9. 빈 시퀀스의 `min` 등 전제조건 위반

**문제.** v0.6은 Release에서 인위적인 `throw`를 금지했지만, 그렇다면 빈 시퀀스의 `min`이 **무엇을 해야 하는지**는 정하지 않았다. 구현은 Release에서 조용히 기본값을 반환한다. 입력 끝의 `readInt()`, 형식이 틀린 토큰도 같다.

**v0.6 코드 / 실제 결과**

```txt
let e = [1..0]       // 빈 배열
+= e.min()           // 0
```

**v0.7 해결.** **전제조건 위반**이라는 범주를 정의했다.

- Debug: 반드시 원인이 드러나는 예외를 던진다.
- Release: 결과는 미정이다(검사 코드 없음).

해당하는 상황은 표로 모아 두었다: 빈 `min`/`max`/`minBy`/`maxBy`, 입력 끝/형식/범위, range 경계 오버플로, 누산 오버플로, `abs(int.MinValue)`, 음수 지수, 변환 실패, 순회 중 구조 변경 등. 이렇게 하면 "Release 무검사" 원칙을 지키면서도, Debug로 한 번 돌려 보면 원인이 드러난다.

**v0.7 코드**

```txt
let e = [1..0]
+= e.min()           // Debug: InvalidOperationException / Release: 미정
```

- **기존 코드 영향**: 없음.
- **스펙**: §29.2

### B10. range의 경계 사례

**문제.** 다음이 정해지지 않았다.

- step 0
- 끝이 타입 최댓값인 끝 포함 range
- step이 있는 range가 끝을 포함하는지
- `..<`와 step을 같이 쓸 수 있는지
- `char` range
- range를 값으로 저장했을 때의 타입

**v0.6 코드 / 실제 결과**

```txt
for i in 0..0..5 do += i             // 상수 step 0: 오류 없음. Release에서 무한 루프
for i in 0..2147483647 do ...        // for (int i = 0; i <= 2147483647; i++) → 오버플로 무한 루프
```

생성 C#(첫 줄): Debug 전용 `throw`만 있고 Release 조건식은 `__step > 0 ? i <= end : i >= end`이므로 step 0이면 영원히 참이다.

**v0.7 해결.**

- 상수 step 0은 **컴파일 오류**. 실행 중 step 0은 **빈 range**다. 조건식을 `s > 0 ? i <= b : s < 0 && i >= b`로 낮추면 검사 비용 없이 된다.
- 끝이 타입 최댓값이라 넘치는 경우는 **전제조건 위반**(Debug 검출)이다. 모든 `1..n` 루프에 분기를 더하지 않기 위한 선택이다.
- step range는 `a..s..b`(끝 포함), `a..s..<b`(끝 제외), `a..s..=b`로 정했다.
- `char` range(`'a'..'z'`)를 추가했다.
- range 값의 정적 타입은 `IEnumerable<T>`이며 bound는 생성할 때 평가된다.

**v0.7 코드**

```txt
for c in 'a'..'z' do = c                 // abcdefghijklmnopqrstuvwxyz
for i in 0..2..<n do ...                 // 0, 2, 4, ... (< n)
for i in 0..0..5 do ...                  // 오류: range step이 0입니다
```

- **기존 코드 영향**: 없음.
- **스펙**: §14

### B11. 순회 중 컬렉션 변경

**문제.** v0.6 §15.5는 `->` lowering으로 "array/list이면 indexed loop 선호 가능, general iterable이면 `foreach`"를 허용했다. 그런데 순회 중에 리스트를 바꾸면 두 lowering의 동작이 다르다. `foreach`는 예외를 던지고, 인덱스 루프는 조용히 계속한다. 구현이 무엇을 고르느냐에 따라 프로그램 의미가 바뀌는 셈이다. 또 `Dictionary`를 `-> i, x`로 돌 때 무엇이 묶이는지도 정해지지 않았다.

**v0.6 코드 / 실제 결과**

```txt
List<int> xs = [1, 2, 3]
xs -> x {
    if x == 1 then xs += 99
}
```

현재 구현은 `foreach`로 낮춰서 Release에서도 `InvalidOperationException`을 던진다. 스펙상으로는 인덱스 루프로 낮춰 `[1, 2, 3, 99]`를 만드는 구현도 적합하다.

**v0.7 해결.**

- 순회 중 receiver의 **구조 변경은 전제조건 위반**이다. Debug는 검출해야 한다. Release는 어떤 lowering이든 쓸 수 있다.
- 구조 분해 binding `(a, b)`를 추가했다. `dict -> (k, v)`, `edges -> (u, v)`, `pairs -> i, (a, b)`처럼 쓴다. `for`에서도 같은 binding을 쓸 수 있다.

**v0.7 코드**

```txt
edges -> (u, v) {
    g[u] += v
}
dict -> (key, count) {
    += $"{key} {count}"
}
for i, x in xs do += $"{i}: {x}"
```

- **기존 코드 영향**: 없음.
- **스펙**: §16.4, §12.3

### B12. 정렬 안정성과 `distinct` 순서

**문제.** `sort`/`sortBy`/`sortWith`가 안정 정렬인지 정해지지 않았다. v0.6이 권장한 `Array.Sort`는 불안정 정렬이다. 또 좌표 압축 패턴 `xs.sort().distinct().index()`는 `distinct`가 순서를 유지한다고 가정하는데, 스펙에는 그런 보장이 없었다.

**v0.7 해결.**

- 세 정렬 helper는 모두 **안정 정렬**이다.
- 단, 동점을 구별할 수 없는 원소 타입(정수, `char`, `bool`)의 `sort()`는 `Array.Sort`로 낮춰도 된다. 안정성이 관측되지 않기 때문에 성능 손해가 없다.
- `distinct()`는 **처음 나온 순서를 유지**한다.

**v0.7 코드**

```txt
List<(int, int)> v = [(1, 3), (0, 1), (1, 2), (0, 2)]
+= v.sortBy(p => p.1)      // 0 1⏎0 2⏎1 3⏎1 2   (같은 key는 원래 순서)
```

- **기존 코드 영향**: 없음 (구현은 이미 안정적으로 동작한다).
- **스펙**: §24.3, §24.6

### B13. 문자열 순서가 실행 환경의 culture를 따름 (v0.7 작업 중 발견)

**문제.** PSCP의 정렬과 비교는 .NET의 `Comparer<string>.Default`를 쓰는데, 이 비교자는 **현재 culture의 언어 규칙**을 따른다. 그래서 ASCII 순서가 아니라 대소문자를 섞은 사전 순서가 나오고, 결과가 실행 환경에 따라 달라질 수 있다.

**v0.6 코드 / 실제 결과**

```txt
let ws = ["b", "B", "a", "A", "_", "-x", "x"]
+= ws.sort()          // _ -x a A b B x
+= min "B" "a"        // a
```

채점 환경이 기대하는 ASCII 순서는 `-x A B _ a b x`이고, `min "B" "a"`는 `B`다.

**v0.7 해결.** PSCP가 소유한 모든 순서 연산(`<=>`, `min`/`max`, `sort*`, `lowerBound`, `T.asc`/`desc`, auto-construction한 정렬 컬렉션)의 **기본 순서**를 표로 정의했다. 문자열은 **ordinal**이다. 튜플은 원소별로 이 규칙을 쓴다.

.NET이 직접 만드는 정렬 구조(`new SortedSet<string>()`, `Array.Sort(string[])`)는 pass-through라 바꾸지 않는다. 대신 비교자 없이 쓰면 경고하고 `string.asc`를 권한다.

**v0.7 코드**

```txt
+= ws.sort()                          // -x A B _ a b x
SortedSet<string> names               // auto-construction → ordinal
SortedSet<string> other = new()       // 경고: 문자열 순서가 culture를 따릅니다. new(string.asc)
```

- **기존 코드 영향**: **있음 (올바른 쪽으로).** 대소문자나 기호가 섞인 문자열의 정렬 결과가 바뀐다.
- **스펙**: §25.1, §25.4

### B14. 결과를 버리는 `arr.sort()`

**문제.** collection helper는 원본을 바꾸지 않는다. 그런데 v0.6 §10.3의 예제는 `arr.sort()`를 **단독 문장**으로 쓴다. 이렇게 쓰면 아무 일도 일어나지 않고, 경고도 없다.

**v0.6 코드 / 실제 결과**

```txt
let arr = [3, 1, 2]
arr.sort()
+= arr                // 3 1 2
```

**v0.7 해결.** 결과를 쓰지 않는 helper 호출 문장은 **경고**다. 진단은 제자리 정렬(`Array.Sort(arr)`, `list.Sort()`) 또는 재대입을 제안한다. 스펙 예제도 고쳤다.

**v0.7 코드**

```txt
arr.sort()            // 경고: sort()는 새 배열을 반환하며 arr은 바뀌지 않습니다
Array.Sort(arr)       // 제자리 정렬
let sorted = arr.sort()
```

- **기존 코드 영향**: 경고 추가. 동작은 같다.
- **스펙**: §24.4

### B15. `find`의 반환 타입

**문제.** v0.6은 `find(pred) -> T?`이고 "값 타입이면 `Nullable<T>`"라고 했다. 그러나 C# 제네릭에서 제약 없는 `T?`는 `Nullable<T>`가 아니다. 또 원소가 이미 `int?`이면 `int??`가 된다. 사용할 때마다 `.Value`를 써야 하는 불편도 있다.

**v0.7 해결.**

- `T?` 의미를 유지하되, lowering이 값 타입과 참조 타입을 나눠 특화해야 함을 명시했다.
- `T`가 이미 nullable이면 결과도 같은 `T?`이며, 이 경우의 한계(못 찾음과 null을 구별할 수 없음)를 문서화했다.
- **`find(pred, fallback) -> T`** 오버로드를 추가했다.

**v0.7 코드**

```txt
if xs.find(x => x > limit) is int v then += v
let first = xs.find(x => x > limit, -1)
```

- **기존 코드 영향**: 없음.
- **스펙**: §24.3

### B16. `chmin`/`chmax`의 반환값

**문제.** 반환값이 정해지지 않았다. 구현은 `bool`(갱신 여부)을 반환한다.

**v0.7 해결.** `chmin(ref T target, T candidate) -> bool`로 확정했다. **엄격히** 더 좋을 때만 갱신하고 `true`를 반환한다. C++ 관용구와 같고 구현과도 같다.

**v0.7 코드**

```txt
if chmin(ref dist[v], d + w) then pq += (v, dist[v])
```

- **기존 코드 영향**: 없음.
- **스펙**: §22.5

### B17. 최상위 변수의 초기화 순서와 타입 안의 상수

**문제.** v0.6은 최상위 변수와 최상위 함수의 관계를 정하지 않았다. 구현은 함수가 참조하는 최상위 변수를 static 필드로 옮기기 때문에 다음 두 가지가 생긴다.

- 변수 선언보다 먼저 실행된 호출이 **조용히 기본값**을 읽는다.
- 타입(struct 등) 안에서 최상위 상수(`MOD`)를 쓰면 **C# 오류**가 난다. ModInt 같은 PS 코드에서 흔한 패턴이다.

**v0.6 코드 / 실제 결과**

```txt
+= solve(3)                          // 0
int solve(int x) { x * helper }
let helper = 10
+= solve(3)                          // 30
```

```txt
let MOD = 1000000007
record struct ModInt(long V) {
    ModInt add(ModInt o) { new ModInt((V + o.V) % MOD) }
}
```

```
error CS0103: The name 'MOD' does not exist in the current context
```

**v0.7 해결.**

- **프로그램 scope**를 정의했다. 최상위 함수는 선언 위치와 관계없이 모든 최상위 변수를 볼 수 있다. 선언 문장이 실행되기 전의 값은 기본값이다(구현과 같다). 최상위 문장이 직접 부른 함수가 아직 초기화되지 않은 변수를 읽으면 **경고**다.
- 타입 선언 안에서는 **컴파일 시점 상수인 최상위 `let`**을 볼 수 있다. 그 밖의 최상위 변수/함수를 참조하면 PSCP 오류다(C# 오류로 넘기지 않는다).

**v0.7 코드**

```txt
+= solve(3)          // 경고: solve가 읽는 helper는 아직 초기화되지 않았습니다 (값 0)

let MOD = 1_000_000_007
record struct ModInt(long V) {
    ModInt add(ModInt o) { new ModInt((V + o.V) % MOD) }    // OK
}
```

- **기존 코드 영향**: 없음 (경고 추가).
- **스펙**: §7.3, §7.4

---
## C. 문법 모호성

### C1. 형식 문법이 없음

**문제.** v0.6에는 EBNF 같은 형식 문법이 없었다. space-call, 줄바꿈으로 끝나는 문장, `=`로 끝나는 입력 선언, 제네릭 `<`처럼 모호해지기 쉬운 부분이 모두 산문으로만 정의되어 있었다. 아래 C2–C13의 문제 대부분이 여기서 나온다.

**v0.7 해결.**

- 부록 A에 문법 요약(EBNF)을 넣었다. 식 문법은 우선순위 표와 1:1로 대응한다.
- 문법만으로 정할 수 없는 부분(space-call head의 호출 가능 여부, `A b`가 선언인지 호출인지)은 "binding 단계에서 결정"으로 명시했다.

- **스펙**: 부록 A, §4, §11, §13.1

### C2. `min (a, b)`가 튜플 하나를 넘김

**문제.** v0.6 §10.2는 `f (a + b) x`처럼 공백 뒤의 괄호를 인자 하나로 본다. 그러면 `min (a, b)`는 튜플 `(a, b)` 하나를 받는 호출이 된다. 사용자가 뜻한 것은 거의 항상 `min(a, b)`다.

**v0.6 코드 / 실제 결과**

```txt
int a, b =
+= min (a, b)
```

생성 C#은 `__PscpSeq.min((a, b))`이고, 결과는 다음과 같다.

```
error CS0411: The type arguments for method '__PscpSeq.min<T>(IEnumerable<T>)' cannot be inferred from the usage.
```

**v0.7 해결.** **괄호 그룹의 원소는 인자 목록에 이어 붙인다.**

- 인자는 모든 group의 원소를 차례로 이은 것이다: `f (a, b)` = `f(a, b)`, `f (a + b) x` = `f(a + b, x)`, `f x (y, z)` = `f(x, y, z)`.
- 튜플 하나를 넘길 때는 `f ((a, b))`처럼 두 겹으로 쓴다.
- 공백 없이 붙은 괄호는 C# 후위 호출이다: `f(a)(b)`는 `f(a)`의 결과를 부른다.

**v0.7 코드**

```txt
+= min (a, b)          // min(a, b)
+= max (a, b) c        // max(a, b, c)
```

- **기존 코드 영향**: 없음. C# 오류가 나던 코드만 의미가 생긴다. 튜플을 넘기려고 `f (t1, t2)`를 쓰던 코드는 없다고 보는데, 있다면 `f ((t1, t2))`로 고쳐야 한다.
- **스펙**: §11.2

### C3. `f -1`

**문제.** `f -1`이 "f에 -1을 넘김"인지 "f 빼기 1"인지 정해지지 않았다. 구현은 뺄셈으로 읽고, PSCP 단계는 통과시킨 뒤 C#이 거부한다.

**v0.6 코드 / 실제 결과**

```txt
int f(int x) { x * 2 }
+= f -1
+= min a -1          // (min a) - 1 로 변환됨
```

```
error CS0019: Operator '-' cannot be applied to operands of type 'method group' and 'int'
```

**v0.7 해결.**

- space-call 인자는 **부호 연산자로 시작할 수 없다**. 공백 여부와 관계없이 `-`는 항상 이항 뺄셈이다. F#처럼 공백 위치로 뜻이 바뀌는 규칙보다 예측하기 쉽다.
- 뺄셈의 왼쪽이 함수이거나 인자가 모자란 호출이면 **PSCP 오류**를 내고 수정안을 보여 준다.

**v0.7 코드**

```txt
+= f (-1)
+= min a (-1)
+= f -1        // 오류: 함수 f에서 1을 뺄 수 없습니다. 음수 인자는 f (-1)
```

- **기존 코드 영향**: 없음 (C# 오류가 PSCP 오류로 바뀔 뿐).
- **스펙**: §11.3

### C4. pipe의 우선순위

**문제.** v0.6 표에서 pipe는 `||`보다 약했다. 그래서 `xs |> sum == 10`은 `xs |> (sum == 10)`으로 묶였다. 구현은 이를 `(sum == 10)(xs)`라는 C# 코드로 내보낸다.

**v0.6 코드 / 실제 결과**

```txt
let ok = xs |> sum == 10
```

생성 C#: `var ok = (sum == 10)(xs);`

```
error CS0103: The name 'sum' does not exist in the current context
```

**v0.7 해결.**

- pipe를 **range와 관계 연산자 사이**로 옮겼다. pipe 결과가 그대로 비교나 논리 연산의 피연산자가 된다. range보다 약하므로 `0..<n |> sum`도 자연스럽게 동작한다.
- `|>`와 `<|`를 괄호 없이 섞으면 오류다.
- pipe 대상이 호출 가능하지 않으면 PSCP 오류다.

**v0.7 코드**

```txt
let ok = xs |> sum == 10                      // (sum xs) == 10
if xs |> filter(p) |> count() > 0 and ready then ...
let s = 0..<n |> sum
```

- **기존 코드 영향**: 드묾. `a || b |> f`는 v0.6에서 `f(a || b)`였고 v0.7에서는 `a || f(b)`다. 이런 코드는 거의 없고, 필요하면 `(a || b) |> f`로 쓴다.
- **스펙**: §13.1, §13.3

### C5. `not a == b`

**문제.** `not`은 `!`와 같은 전위 단항 순위라서 `not a == b`는 `(not a) == b`다. Python 사용자는 반대로 기대한다. 스펙에 이 점이 적혀 있지 않았고 진단도 없었다.

**v0.6 코드 / 실제 결과**

```txt
int a, b =
+= not a == b
```

```
error CS0023: Operator '!' cannot be applied to operand of type 'int'
```

**v0.7 해결.** 우선순위는 그대로 둔다. C#의 `!`와 같아야 하고, `not visited.Add(x)` 같은 흔한 형태가 자연스럽기 때문이다. 대신 이 점을 명시하고, `not X`가 비교/동등/`is`의 왼쪽이 되면 **경고**하며 `not (a == b)` 또는 `a != b`를 제안한다.

- **기존 코드 영향**: 경고 추가.
- **스펙**: §13.7

### C6. space-call 인자와 후위 연산

**문제.** v0.6 우선순위 표는 member access와 space-call을 같은 "postfix" 칸에 넣어서, `f x.y`가 `(f x).y`인지 `f(x.y)`인지 알 수 없었다. 구현은 `f(x.y)`로 읽는다(합리적이다).

**v0.6 코드 / 실제 결과**

```txt
+= f p.1           // f(p.Item1)
+= f s.Length      // f(s.Length)
```

**v0.7 해결.** application을 후위 연산보다 한 단계 약한 별도 순위로 두고, 인자 원자에 공백 없이 붙은 후위 연산(`.m`, `.1`, `[i]`, `(args)`)은 그 원자에 속한다고 정했다. 구현의 동작과 같다.

- **기존 코드 영향**: 없음.
- **스펙**: §11.2, §13.1

### C7. 줄 이어짐 규칙

**문제.** v0.6은 "문장은 보통 줄바꿈으로 구분된다"고만 했다. 어느 경우에 줄이 이어지는지 규칙이 없었다. 구현은 줄 끝 연산자, 열린 괄호, `then`/`else` 뒤의 줄바꿈, 다음 줄의 `{`(Allman 스타일), 다음 줄 맨 앞의 `|>`를 받아들이지만, 다음 줄 맨 앞의 `.`는 오류다. README 예제(`if ... then` 다음 줄에 `continue`)도 스펙으로는 근거가 없었다.

**v0.6 코드 / 실제 결과**

```txt
let s = xs
    .map(x => x * 2)       // error: Unexpected token '.' in expression.
    .sum()
```

**v0.7 해결.** 이어짐 규칙을 세 가지로 정리했다.

1. 열린 `(`/`[` 안
2. 줄이 이어짐 토큰(이항 연산자, `,`, `.`, `=>`, `->`, `then`, `else`, `do`, `in` 등)으로 끝날 때
3. 다음 줄이 이어짐 토큰(`.`, `?.`, `|>`, `<|`, `&&`, `||`, `and`, `or`, `else`, 본문을 기다리는 머리 뒤의 `{`)으로 시작할 때

또 **이어짐이 아닌 것**도 명시했다. 줄 끝 `=`는 입력 shorthand이고, 줄 시작 `=`/`+=`는 출력 shorthand이며, 줄 시작 `+`/`-`는 새 문장이다. 그래서 입출력 shorthand와 충돌하지 않는다.

**v0.7 코드**

```txt
let total = xs
    .filter(x => x > 0)
    .map(x => x * 2)
    .sum()

if ok then
    += "YES"
else
    += "NO"
```

- **기존 코드 영향**: 없음 (지금 되는 것은 계속 된다).
- **스펙**: §4

### C8. 패턴 안의 `and`/`or`/`not`

**문제.** v0.6은 `is`/패턴을 pass-through라고 했지만, PSCP는 `and`/`or`/`not`을 불리언 연산자 키워드로 쓴다. C# 9 이후 패턴 결합자와 충돌한다. 구현은 관계 패턴을 아예 파싱하지 못한다.

**v0.6 코드 / 실제 결과**

```txt
let x = 5
+= x is > 0 and < 10
```

```
error: Unexpected token '>' in expression.
```

**v0.7 해결.** `is` 뒤와 switch arm은 **패턴 문맥**이다. 패턴 문맥에서는 `and`/`or`/`not`이 패턴 결합자이고 관계 패턴을 쓸 수 있다. 패턴 문맥이 끝나는 토큰(`&&`, `||`, `)`, `then`, `do`, 줄바꿈 등)도 명시했다. 패턴과 일반 불리언을 결합할 때는 `&&` 또는 괄호를 쓴다.

**v0.7 코드**

```txt
if x is > 0 and < 10 then += "digit"
if x is int v && v > 0 then ...
let kind = v switch {
    < 0 => "neg"
    0 => "zero"
    _ => "pos"
}
```

- **기존 코드 영향**: 없음.
- **스펙**: §13.9

### C9. 빠진 연산자와 C# 문장

**문제.** v0.6 우선순위 표에 `??`, `?.`, `??=`, `&=`/`|=`/`^=`/`<<=`/`>>=`, 캐스트, `as`, `with`, `switch` 식의 위치, index-from-end `^`, 람다 `=>`가 없었다. 어떤 C# 문장이 통과되는지도 적혀 있지 않았다. 구현은 `try`/`catch`와 C 스타일 `for`는 받아들이지만 `switch` 문과 `foreach`는 파싱 오류를 낸다.

**v0.6 코드 / 실제 결과**

```txt
switch (3) {
    case 3: += 3; break         // error: Unexpected token ':' in expression.
}
foreach (var v in [1, 2]) { }   // error: Unexpected token 'var' in expression.
```

**v0.7 해결.**

- 우선순위 표를 19단계로 완성했다.
- pass-through C# 문장(`try`/`catch`/`finally`/`throw`, C 스타일 `for`)과 **지원하지 않는 문장**(`foreach`, `do-while`, `switch` 문, `goto` 등)을 표로 나누고, 각각의 PSCP 대안을 적었다.
- 생성 C#의 버전 하한(C# 10)도 명시했다. 그래서 raw string, list pattern 등은 통과되지 않는다.

- **기존 코드 영향**: 없음.
- **스펙**: §6.1, §6.3, §13.1

### C10. `[]` 리터럴의 규칙

**문제.** 다음이 정해지지 않았다.

- `[0..<n]`처럼 range가 자동으로 펼쳐지는 조건 (문법 모양인가, 타입인가)
- 빈 `[]`의 타입
- 원소 타입을 합치는 방법

구현은 `let bad = []`를 `object[]`로 만든다.

**v0.6 코드 / 실제 결과**

```txt
let bad = []          // object[] bad = new object[] { };
```

**v0.7 해결.**

- **괄호 없는 range 문법**인 원소만 자동으로 펼친다. range 값을 담은 변수는 펼쳐지지 않으므로 `..r`로 펼친다.
- 빈 `[]`는 target type이 필요하다. 없으면 오류다.
- 원소 타입은 C# 숫자 승격으로 합친다(`[1, 2L]`은 `long[]`). 공통 타입이 없으면 오류다.
- target type으로 쓸 수 있는 컬렉션 목록을 적었다.

**v0.7 코드**

```txt
int[] empty = []
let r = 0..<3
let a = [0..<3]       // [0, 1, 2]
let b = [..r, 9]      // [0, 1, 2, 9]
```

- **기존 코드 영향**: 드묾. 타입 없는 `[]`를 쓰던 코드만 오류가 된다.
- **스펙**: §16.1

### C11. member alias와 이름 가리기

**문제.** v0.6은 비한정 이름의 가리기만 설명했다. 스펙의 예제 스스로 `var sum = 0`을 쓰는데, 이렇게 가린 뒤에도 `arr.sum()`을 쓸 수 있는지 적혀 있지 않았다. 사용자 타입에 `count()` 메서드가 있을 때 무엇이 이기는지도 없었다. 구현은 합리적으로 동작한다.

**v0.7 해결.** member 호출의 해석 순서(실제 멤버 → 확장 메서드 → intrinsic alias)를 정하고, **비한정 가리기는 member alias에 영향을 주지 않는다**고 명시했다. v0.6 §3.1 우선순위 목록에서 순서가 어색했던 항목(바깥 scope가 같은 block의 로컬 함수보다 앞)도 정리했다.

**v0.7 코드**

```txt
var sum = 0
for i in 0..<n do sum += a[i]
let check = a.sum()           // intrinsic sum
```

- **기존 코드 영향**: 없음.
- **스펙**: §5.1, §5.2

### C12. DS `+=`의 값을 반환하기

**문제.** `visited += x`(HashSet)는 `bool`을 내는 호출인데도, v0.6 §12.7은 "compound assignment statement"라서 암묵적 반환 대상에서 뺐다.

**v0.6 코드 / 실제 결과**

```txt
HashSet<int> visited
bool mark(int v) {
    visited += v
}
```

```
2:6: error: `mark` must end with a return value of type `bool`. ...
```

**v0.7 해결.** known DS rewrite는 대입이 아니라 **호출**이므로, 값이 있는 rewrite(`HashSet`/`SortedSet`/`Dictionary`의 `+=`/`-=`, `--q`, `~s`)는 tail position에서 반환된다.

**v0.7 코드**

```txt
bool mark(int v) {
    visited += v          // Add 결과 반환
}
int next() {
    --queue               // Dequeue 결과 반환
}
```

- **기존 코드 영향**: 없음.
- **스펙**: §10.5, §26.1

### C13. 다중 선언과 `Foo bar`

**문제.**

- `int a, b = e`(분해)와 C# 형태 `int a = 1, b = 2`의 관계가 정해지지 않았다. 구현은 후자를 받아들여 `(int a, int b) = (1, 2)`로 만든다.
- 문장 시작의 `Foo bar`가 초기화 없는 선언인지 space-call인지도 정해지지 않았다.

**v0.7 해결.**

- 첫 이름 바로 뒤에 `=`가 있으면 이름마다 독립 선언(C# 형태)이다. 없으면 `T a, b = e`는 분해, `T a, b =`(끝)은 입력이다. 세 형태는 문법으로 구별된다.
- `A b`는 binding 단계에서 `A`가 타입이면 선언, 호출 가능하면 space-call이다. `A<…> b`, `A[] b`처럼 타입 모양이 분명하면 항상 선언이다.

**v0.7 코드**

```txt
int a = 1, b = 2        // 두 선언
int x, y = (3, 4)       // 분해
int n, m =              // 입력
Node root               // Node가 타입이면 선언
log message             // log가 함수면 호출
```

- **기존 코드 영향**: 없음.
- **스펙**: §9.4, §11.5

---

## D. API 정리

### D1. 입력 API 이름

**문제.**

- 격자 reader 이름의 어순이 섞여 있었다: `readGridInt`, `readGridLong` / `readCharGrid`, `readWordGrid`.
- `readTuple2/3`, `readTuples2/3`은 이름에 원소 수를 넣었고, `readArray<(int, int)>`와 기능이 겹쳤다.
- 스펙이 "정식 이름이 아니다"라고 한 `stdin.int()`, `stdin.str()` 등의 별칭을 구현이 받아들였다.
- 구현에만 있는 `readNestedArray`가 문서에 없었다.

**v0.7 해결.**

| v0.6 | v0.7 |
|---|---|
| `readGridInt(n, m)`, `readGridLong(n, m)`, `readNestedArray<T>(n, m)` | `readGrid<T>(n, m)` |
| `readTuple2<A,B>()`, `readTuple3<A,B,C>()` | `readTuple<A,B>()`, `readTuple<A,B,C>()` (원소 7개까지) |
| `readTuples2<A,B>(n)`, `readTuples3<…>(n)` | `readArray<(A,B)>(n)` |
| `stdin.int()`, `str()`, `line()` 등 | `readInt()`, `readString()`, `readLine()` 등 |

옛 이름은 v0.7에서 **경고와 함께 동작**하고 v0.8에서 제거한다. 입력 튜플의 원소 수 상한은 3에서 7로 늘렸다(C# `ValueTuple`이 `Rest` 없이 담는 수).

`readGrid<char>(h, w)`(문자 단위, 공백 건너뜀)와 `readCharGrid(h)`(줄 단위, 공백 보존)의 차이도 명시했다.

- **스펙**: §17.2, §17.8, 부록 C

### D2. 배열 생성

**문제.**

- `int[n] arr;`, `new[n]`, `Array.zero(n)` 세 가지가 같은 일을 한다. 그중 `Array.zero`는 `System.Array`와 이름이 겹친다.
- 구현에서 동작하는 `new[n][m]`(DP 테이블에 필수)은 문서에 없었다.
- `new![n]`은 known collection에만 쓸 수 있었다.
- 값으로 채운 배열을 만드는 방법(`INF`로 초기화)이 문서에 없었다.

**v0.7 해결.**

- `new[n][m]`(여러 차원 jagged 배열)을 문서화했다.
- `new![n]`을 **매개변수 없는 생성자를 가진 모든 타입**으로 넓혔다.
- 채우기는 builder `[0..<n -> _ do INF]`가 할당 + fill loop로 낮아진다고 명시했다. 새 문법은 만들지 않았다.
- `Array.zero(n)`은 폐기 예정이다.

**v0.7 코드**

```txt
long[][] dp = new[n][m]
Node[] nodes = new![n]
long[] dist = [0..<n -> _ do INF]
int[][] memo = [0..<n -> _ do [0..<m -> _ do -1]]
```

- **스펙**: §27

### D3. `groupCount`와 `freq`

같은 기능의 두 이름 중 PS에서 더 흔한 `freq`를 남기고, `groupCount`는 폐기 예정으로 바꿨다.

- **스펙**: §24.5

### D4. `a..b`와 `a..=b`

v0.6에서 두 형태는 완전히 같은 뜻이라 중복이었다. v0.7에서는 인덱서 안에서 끝 포함 여부를 명시해야 하므로(A3), `..=`가 **인덱서에서 끝 포함을 나타내는 유일한 방법**이 되었다. range로 쓸 때는 여전히 같다.

- **스펙**: §14.1, §15.2

### D5. `T.asc` / `T.desc`의 타입

**문제.** 이 값의 타입(`IComparer<T>`인지 `Comparison<T>`인지)과 쓸 수 있는 곳이 정해지지 않았다.

**v0.7 해결.** `IComparer<T>`로 확정했다. `sortWith`, `PriorityQueue`/`SortedSet` 생성자, `Array.Sort` 등 `IComparer<T>`를 받는 곳이면 어디든 쓸 수 있다. 튜플 타입(`(int, string).asc`)도 허용하며, 기본 순서(B13)를 따른다.

**v0.7 코드**

```txt
PriorityQueue<int, int> maxHeap = new(int.desc)
let ranked = scores.sortWith(int.desc)
Array.Sort(names, string.asc)
```

- **스펙**: §25.2

### D6. `operator<=>`가 생성하는 것

**문제.** "`IComparable<T>` 호환 lowering을 우선"이라고만 했다. 기반 목록에 인터페이스를 추가하는지, `a < b`를 쓸 수 있는지 알 수 없었다. `<=>`의 결과가 `-1/0/1`인지도 정해지지 않았다.

**v0.7 해결.** 트랜스파일러는 `IComparable<T>` 구현(없으면 기반 목록에 추가), `CompareTo`, 관계 연산자 `<`, `<=`, `>`, `>=`를 생성한다. `==`와 `Equals`는 생성하지 않는다. `<=>`의 결과는 **부호만** 의미가 있다.

**v0.7 코드**

```txt
record struct Job(int Id, long Time) {
    operator<=>(other) => Time <=> other.Time
}
if a < b then ...              // 생성된 연산자
let order = jobs.sort()
```

- **스펙**: §13.8, §25.3

### D7. 새로 추가한 API

PS에서 자주 필요한데 없던 것만, 기존 이름 체계 안에서 추가했다.

| API | 이유 |
|---|---|
| `xs.lowerBound(x)`, `xs.upperBound(x)` | 이분 탐색. 좌표 압축과 함께 쓰는 경우가 많다 |
| `stdin.hasNext()`, `stdin.hasNextLine()` | 입력 끝까지 읽기 (B3) |
| `find(pred, fallback)` | nullable 없이 쓰기 (B15) |
| `floor(a, b)`, `ceil(a, b)` | 음수에서도 올바른 정수 내림/올림 나눗셈 |
| `pow(a, e, m)` | 모듈러 거듭제곱 (B8) |
| `round(x, d)` | 자리수 지정 반올림 (B5) |
| `char` range | `'a'..'z'` (B10) |
| `readGrid<T>`, `readTuple<…>` | 이름 정리 (D1) |

```txt
+= floor(-7, 2)        // -4   (C#의 -7 / 2는 -3)
+= ceil(7, 2)          // 4
```

- **스펙**: §17.5, §23, §24.3

### D8. 런타임 계약

**문제.** v0.6은 `Main`에서 flush한다고만 했다. 다음이 정해지지 않았다.

- `Console.Write`를 섞어 쓸 때의 출력 순서
- 인터랙티브 문제
- 예외나 `Environment.Exit`로 끝날 때의 출력
- 깊은 재귀 (구현에 `--large-stack` 옵션이 있지만 스펙에 없다)

**v0.7 해결.**

- `Console.Out`을 `stdout`과 같은 버퍼로 연결하는 것을 **필수**로 했다(구현은 이미 `Console.SetOut`을 한다).
- `Console.In`과 `stdin`을 섞는 것은 미정이라고 명시했다.
- 비정상 종료 시 버퍼가 사라질 수 있음을 명시하고, 함수 안에서 끝낼 때는 `stdout.flush()` 후 `Environment.Exit(0)`을 쓰도록 했다.
- 인터랙티브 문제에서는 `stdout.flush()`가 필요하다고 명시했다.
- `--large-stack`을 reference backend 옵션으로 기록했다.

- **스펙**: §30

### D9. 쓰지 않는 `using`의 제거 범위

**문제.** v0.6 §31.2는 "사용되지 않는 `using`은 제거한다"고 했다. 사용자가 쓴 `using`까지 지우면, 확장 메서드를 들여오던 `using`이 사라져 pass-through 코드가 깨질 수 있다.

**v0.7 해결.** 제거 대상은 **트랜스파일러가 생성한 `using`만**이다. 사용자 `using`은 보존하고, 필요하면 경고만 한다.

- **스펙**: §31

---

## E. 문서 체계

코드 예시가 필요 없는 편집 변경이다.

| 항목 | v0.6 | v0.7 |
|---|---|---|
| 규범 수준 | "가능하면/권장/할 수 있다"가 섞여 있어 §34 적합성을 검증할 수 없음 | §0.2에 MUST/SHOULD/MAY 대응을 정의. **의미는 MUST, lowering 모양은 SHOULD** |
| 용어 | "stable core", "reference backend"가 정의되지 않음. stable core 밖이 오류인지 미정인지 불명 | §0.4 용어 표. stable core 밖은 **오류** |
| 목차 | `## 3.1`이 `## 3.`과 같은 수준이라 목차가 무너짐 | 절 `##`, 하위 절 `###`. 모든 절 참조를 링크로 걸고 링크가 모두 실제 절을 가리키는지 검증 |
| 진단 | 경고/오류가 본문에 흩어져 있음 | 부록 B에 필수 진단 목록 |
| 폐기 | 없음 | 부록 C (v0.7 경고, v0.8 제거) |
| 변경 요약 | 없음 | 부록 D와 이 문서 |
| 작성 메모 | "이 절은 v0.6에서 매우 중요하다" 등 | 제거 |
| 설계 목표 | — | "PSCP 진단을 통과한 프로그램은 PSCP 표면 때문에 C# 오류를 내지 않는다"를 목표 9와 적합성 14로 추가. C2–C5, B17처럼 C# 오류로 새던 문제들의 공통 원인이다 |

---

## 마이그레이션 체크리스트

v0.6 코드를 v0.7로 옮길 때 확인할 것이다. **저장소 `tests/TestCodes`의 24개 프로그램은 아래 어느 항목에도 해당하지 않는다.**

| 확인할 것 | 찾는 법 | 할 일 |
|---|---|---|
| 인덱서 안의 `a..b`, `..b` | 대괄호 안의 `..` 뒤에 `<`나 `=`가 없는 곳 | `..<`로 고친다 (의미 유지) |
| 문자 입력 | `readChar`, `char[` 입력 선언 | 토큰 단위를 기대했다면 `readString()`으로 |
| `bool "…"` 변환 | `bool "`, `bool(` + 문자열 | 비어 있음 검사면 `s.Length > 0` |
| `round` | `round` | .5 경계에서 짝수 반올림이 필요했다면 `Math.Round(x)` |
| 정수 `pow` | `pow(` | `double`이 필요하면 실수 인자 |
| 출력 모양 | `bool`/`double`/`char[]`/2차원 값의 직접 출력 | 새 렌더링 표 확인 |
| 문자열 정렬 | 문자열 `sort`/`min`/`max` | 결과가 ASCII 순서로 바뀜 |
| 폐기 예정 이름 | 부록 C | 대체 이름으로 |
| 가변 필드 | 클래스/구조체 필드 대입 | `mut` 추가 (경고 제거) |

---

## 구현 작업 목록

v0.7 초안을 트랜스파일러와 언어 서버에 반영할 때의 작업을 구성 요소별로 정리했다.

**Lexer / Parser**

- 줄 이어짐 규칙 (§4.2): 다음 줄 맨 앞 `.`, `?.`, `&&`, `||`, `and`, `or`
- space-call 괄호 그룹 결합 (§11.2), 부호 연산자 원자 금지 (§11.3)
- pipe 우선순위 이동 (§13.1), `|>`/`<|` 혼용 오류
- 패턴 문맥: 관계 패턴, `and`/`or`/`not` 결합자 (§13.9)
- 인덱서 안의 `..` 금지와 `..=` 슬라이스 (§15.2)
- stepped `..<`/`..=`, `char` range (§14)
- 구조 분해 순회 binding `(a, b)` (§16.4)
- `operator<=>`의 블록 본문, `(int, string).asc` 형태

**Semantic analyzer**

- 호출 그래프 기반 `rec` 검사 (§10.3)
- tail position과 반환 대상 확장 (§10.5)
- 최상위 초기화 순서 경고, 타입 안의 상수 참조 허용 / 비상수 참조 오류 (§7.3, §7.4)
- 필드 가변성, 기본 `public` (§28.2)
- 결과를 버리는 helper 경고, `not X ==` 경고, 문자열 정렬 구조 경고
- 빈 `[]` 오류, 3단 중첩 출력 오류
- **PSCP 고유 표면의 타입 오류를 C#으로 넘기지 않기** (§34-14): `f - 1`, pipe 대상, `min((a, b))` 등

**Emitter / Runtime**

- 생성 식별자 예약 접두사 (§5.4)
- `readChar` 문자 단위, `hasNext`/`hasNextLine`, `readGrid<T>`, `readTuple<…>`, 폐기 별칭 경고
- 렌더링 표 (§18.3): `bool` 소문자, 지수 없는 `double`, `char` 컬렉션, 줄 단위 2차원, 보간 hole
- 기본 순서 비교자: 문자열 ordinal, 튜플 (§25.1)
- 정수 `pow`, `pow(a, e, m)`, `round` AwayFromZero, `floor(a, b)`/`ceil(a, b)`, `lowerBound`/`upperBound`, `find(pred, fallback)`
- `sum`/`sumBy`/`pow`의 target-typed 누산과 Debug 오버플로 검사 (§22.6)
- 실행 중 step 0에서 빈 range가 되는 조건식 (§14.6)
- `string` → `bool` 파싱 (§21)
- `operator<=>`에서 관계 연산자 생성 (§25.3)

**0.6.7 구현 버그 (스펙과 무관)**

- 중첩 builder `[0..<h -> _ do [0..<w -> _ do -1]]`가 컴파일되지 않는 C#을 만든다 (CS0029/CS0178). 0.6 스펙으로도 유효한 코드이며, 0.7 스펙 §16.2와 §27.3에 예제로 들어 있다.

**적합성 테스트**

- `tests/TestCodes/v0.7/`에 0.6.7에서 실패하고 0.7에서 통과해야 하는 프로그램 17개와 입력/기대 출력이 있다. 구현이 끝나면 테스트 하네스가 이 폴더를 실행·비교하도록 넓힌다.

**언어 서버 / VS Code 확장**

- 새 API와 폐기 이름의 완성·hover, 새 진단 표시
- `pscp_v_0_6_language_server_and_vscode_extension_guide.md`는 v0.7 기준으로 별도 갱신이 필요하다.

---

## 재검토하면 좋을 결정

모두 한쪽으로 정해 두었지만, 방향이 반대여도 스펙이 성립하는 결정들이다. 스펙에서 고칠 곳이 적은 것부터 적었다.

| 결정 | 고른 쪽 | 다른 선택지 | 고른 이유 |
|---|---|---|---|
| `bool` 출력 | `true`/`false` | C# 그대로 `True`/`False` | PSCP 리터럴, `readBool`과 철자가 같다 |
| `round` | 0에서 먼 쪽 | 짝수 반올림 유지 | PS에서 기대하는 동작 |
| `int '7'` | `55` (코드 값) | `7` (숫자 값) | `char 65 = 'A'`와 대칭, C# 관습 |
| helper 결과 | 항상 배열 | receiver 종류 유지 (v0.6 스펙) | 구현과 테스트 코드가 이미 배열 |
| 타입 멤버 기본 접근성 | `public` | C# 그대로 `private` | 스펙 예제가 컴파일되게 |
| 필드 가변성 | 지역 선언과 같게 (경고) | 필드는 C#처럼 가변 | "기본 불변" 철학과 v0.6 §8.7의 의도 |
| `f -1` | 항상 뺄셈 | F#처럼 공백 위치로 구별 | 공백에 뜻을 싣지 않기 |
| pipe 순위 | range와 비교 사이 | F#처럼 비교와 같은 순위 | `a < b \|> f`가 `a < f(b)`가 되는 쪽이 자연스럽다 |
| `readLine` | 줄 중간이면 나머지 | 항상 다음 줄 | 구현과 같고 데이터를 버리지 않는다 |
| `rec` | 필수 (검사) | 선택 (표시만) | v0.6의 의도 유지. 필수가 부담이면 경고로 낮출 수 있다 |
| range 끝 오버플로 | 전제조건 위반 (Debug 검출) | 항상 안전한 루프 | 모든 `1..n` 루프에 분기를 더하지 않기 위해 |
| 폐기 일정 | v0.7 경고 → v0.8 제거 | 즉시 제거 | 테스트 코드는 해당 없음. 외부 코드 배려 |
