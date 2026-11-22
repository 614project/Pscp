# PSCP

PSCP 언어를 제공하기 위한 도구들(트랜스파일러, 언어 서버, 확장 등)이 준비되어 있는 저장소입니다.

현재 언어 버전은 **0.7**이고 도구 버전은 **0.7.1**입니다. 규범 문서는 [`docs/pscp_v_0_7_spec.md`](docs/pscp_v_0_7_spec.md)이며, 0.6에서 무엇이 바뀌었는지는 [`docs/pscp_v_0_7_changes.md`](docs/pscp_v_0_7_changes.md)에 있습니다.

## 🚀 PSCP 언어 소개

**PSCP**는 알고리즘 문제 해결(PS)과 경쟁 프로그래밍(CP)에 최적화된 새로운 프로그래밍 언어입니다.
적게 쓰고 많이 표현하며, 더이상의 장황함은 산출물인 C# 코드에서만 남기도록합니다.

F#은 매우 뛰어난 표현력을 가집니다. 특히 컬렉션을 다루는 편리함은 압도적입니다. 하지만 때로는 `break`, `return` 등 함수형의 철학을 무시하거나, C#의 문법 설탕이 그리운 상황이 오기도 합니다. 또한 두 언어 모두 입출력에서 매우 장황하다는 단점을 공유하고 있습니다.  
이를 해소하기 위해 PSCP라는 언어를 새로 고안하게 되었습니다. 두 언어의 장점을 더 간결한 표현으로 가져올수 있게 합니다.

---

## ✨ 핵심 철학

* **반복 작업의 최소화:** 입력 파싱, 출력 형식, 배열 생성, 범위 순회 등 PS에서 매번 작성하는 귀찮은 보일러플레이트 코드를 언어 차원의 문법 설탕(Syntax Sugar)으로 대폭 줄였습니다.
* **짧고 간결한 문법:** 간결한 키워드와 헬퍼 매서드를 적극 제공하여, 코드 길이를 단축하고 의도하던 로직에 빠르게 도달할수 있게 합니다.
* **릴리즈 제로 코스트 추상화:** 문법 설탕이 런타임 오버헤드를 발생시키지 않도록 합니다. 불필요한 배열 생성이나 LINQ 체인 대신, 트랜스파일러가 직접적이고 빠른 C# 코드로 변환하며, 안정성을 위한 코드들은 모두 전처리기 지시문으로 디버깅에서만 동작하게 하여 문제를 조기에 확인할수 있게 합니다.
* **기본 불변성(Default Immutability):** 변수는 기본적으로 불변(`let`)이며, 필요할 때만 가변(`var`, `mut`)으로 선언하여 사이드 이펙트를 통제합니다. 하지만 이는 강제가 아니며, 트랜스파일러 차원에서 경고하며 의도적인 위반에 간섭하지 않는 융통성을 챙깁니다.

---

## 💡 주요 기능 및 특징

### 1. 짧고 간결한 입출력
변수 선언과 동시에 토큰을 읽어옵니다.
```pscp
int n =             // 정수 하나 입력
long a, b =         // 정수 두 개 입력
int[n] arr =        // 크기가 n인 배열 입력
int[n][m] grid =    // 2차원 배열 입력

= arr               // Console.Write(string.Join(' ', arr))
+= (a, b)           // Console.WriteLine($"{a} {b}")
```

### 2. 강력한 범위(Range)와 컬렉션 빌더
F#에 깊은 영감을 받았습니다. `[]`는 실제로 힙에 할당되는 컬렉션, `()`는 지연 평가되는 제너레이터를 의미합니다.

```pscp
int[] parent = [0..<n]                   // 0부터 n-1까지의 배열 생성
let squares = [1..10 -> i do i * i]      // 1부터 10까지 제곱수 배열 생성
let best = min (0..<n -> i do arr[i])    // 중간 배열 생성 없이 바로 최솟값 계산
```

### 3. 자료구조 특화 연산자 (Operator Rewrites)
.NET의 기본 자료구조들을 더 짧고 직관적으로 다룰 수 있도록 트랜스파일러 수준에서 특별한 연산자를 지원합니다.

- List / LinkedList: `list += x` (요소 추가)
- HashSet / SortedSet: `visited += x` (요소 추가 및 `bool` 반환), `visited -= x` (요소 제거 및 `bool` 반환)
- Dictionary: `dict += (key, value)` (`TryAdd`), `dict -= key` (`Remove`)
- Stack / Queue: `+= x` (Push/Enqueue), `~s` (Peek), `--s` (Pop/Dequeue)
- PriorityQueue: `pq += (item, priority)` (튜플 형태 추가)

이러한 특수 연산자의 활용을 가장 잘 보여주는 BFS 예시입니다.
```pscp
int n,m =
List<int>[] graph = new![n]
0..<m -> _ {
    int a, b =
    graph[a] += b
    graph[b] += a
}

Queue<int> queue
queue += 0
HashSet<int> visited

while queue.Count > 0 {
    let me = --queue
    if not (visited += me) then
        continue
        
    graph[me] -> other {
        queue += other
    }
}
```

### 4. 실전 압축 문법
* **`:=` 대입 연산자:** 대입과 동시에 해당 값을 반환합니다. (예: 경로 압축에 유용 `parent[x] := find(parent[x])`)
* **조건부 할당 `>=`, `<=`:** 문장으로 쓴 `low >= x`는 `low = min low x`, `high <= x`는 `high = max high x`입니다. (예: `dist[v] >= dist[u] + w`) 식 안에서는 그대로 비교입니다.
* **자동 객체 생성 `new![n]`:** 컬렉션 배열을 선언할 때 내부 요소들까지 `new()`로 자동 초기화합니다. (예: `List<int>[] graph = new![n]`)
* **내장 집계 함수 (Aggregate Intrinsic):** `min`, `max`, `sum`, `sumBy`, `minBy`, `maxBy` 등을 별도의 라이브러리 임포트 없이 즉시 사용합니다. (`chmin`, `chmax`는 0.7.1부터 폐기 예정이며 조건부 할당으로 대체합니다.)

* **암묵적 반환 (Implicit Return):** 블록의 마지막 표현식은 자동으로 반환값이 되어 코드가 한결 깔끔해집니다.

---

## 📖 Union-Find 예제

PSCP는 알고리즘을 최대한 직관적으로 표현하기 위해 치밀하게 설계되었습니다.

```pscp
// 1. 빠른 입력
int n =
(int, int)[n] query =

// 2. 간결한 배열 초기화
int[] parent = [0..<n]

// 3. 재귀(rec) 함수와 암묵적 반환, := 연산자로 명시적 대입 후 l-value 반환
rec int find(int x) {
    if x == parent[x] then x
    else parent[x] := find(parent[x])
}

// 4. 튜플 스왑과 제어 흐름
bool union(int a, int b) {
    var x = find(a)
    var y = find(b)
    if x == y then
        return false // 함수 중간에서는 return이 필요하다

    if parent[x] < parent[y] {
        (x, y) = (y, x) // 튜플 스왑
    }

    parent[y] = x
    true // 암묵적 true 반환 (마지막 문장)
}

// 5. 간결한 foreach
query -> q {
    //6. Item1, Item2 대신 상수를 사용하는 간단해진 튜플 접근
    if union q.1 q.2 then
        // 7. 빠른 출력
        += "OK"
    else
        += "NO"
}
```

---

## 시작하기

1. [.NET 10 SDK](https://dotnet.microsoft.com/) 이상을 설치합니다.
2. [Releases](https://github.com/614project/Pscp/releases)에서 PSCP SDK를 받아 설치합니다. (직접 빌드하는 방법은 [위키의 설치 문서](https://github.com/614project/Pscp/wiki/Installation)에 있습니다.)
3. 원하는 폴더에 `main.pscp`를 만들고 코드를 작성한 뒤 `pscp run`을 실행합니다.

```powershell
pscp init      # main.pscp와 .pscp 프로젝트 생성
pscp run       # 트랜스파일 후 실행
pscp test      # name.in / name.out 샘플로 채점
```

## 문서

도구 사용법은 [GitHub 위키](https://github.com/614project/Pscp/wiki)에 정리되어 있습니다.

- [설치](https://github.com/614project/Pscp/wiki/Installation): Windows 설치기, Linux 패키지
- [CLI 사용법](https://github.com/614project/Pscp/wiki/CLI): 명령과 옵션, 진단, 샘플 실행
- [언어 서버 (LSP)](https://github.com/614project/Pscp/wiki/Language-Server)
- [VS Code 확장](https://github.com/614project/Pscp/wiki/VS-Code-Extension)
- [개발 및 빌드](https://github.com/614project/Pscp/wiki/Development): 저장소 구성, 소스 빌드, Native AOT, 릴리즈

언어 규범 문서:

- [`docs/pscp_v_0_7_spec.md`](docs/pscp_v_0_7_spec.md): 언어 스펙 (규범)
- [`docs/pscp_v_0_7_changes.md`](docs/pscp_v_0_7_changes.md): 0.6 대비 변경 사항
- [`docs/pscp_v_0_7_language_server_and_vscode_extension_guide.md`](docs/pscp_v_0_7_language_server_and_vscode_extension_guide.md): 언어 서버와 VS Code 확장의 계약
