# PSCP 

## 🚀 PSCP 언어 소개

**PSCP**는 알고리즘 문제 해결(PS)과 경쟁 프로그래밍(CP)에 최적화된 새로운 프로그래밍 언어입니다. 
가장 빠르고 간결하게 의도를 코드로 표현하고, 이를 빠르고 최적화된 **C# 코드로 트랜스파일(Transpile)**하는 것을 목표로 설계되었습니다.

F#은 매우 뛰어난 표현력을 가집니다. 특히 컬렉션을 다루는 편리함은 압도적입니다. 하지만 때로는 함수형의 철학을 무시하거나(break, return) C#의 문법 설탕이 절실히 필요해지는 상황이 오기도 합니다. 또한 두 언어 모두 입출력에서 매우 장황하다는 단점을 공유하고 있습니다.
이를 해소하기 위해 PSCP라는 언어를 새로 고안하게 되었습니다. 최대한 많은걸 표현하도록 최적화된 문법과, 풍부한 PSCP상의 API를 제공하여, 적게 쓰고 더 많이 표현할수 있게 합니다.

---

## ✨ 핵심 철학

* **반복 작업의 최소화:** 입력 파싱, 배열 생성, 범위 순회 등 PS에서 매번 작성하는 귀찮은 보일러플레이트 코드를 언어 차원의 문법 설탕(Syntax Sugar)으로 대폭 줄였습니다.
* **직관적이고 짧은 문법:** 코드의 길이를 줄여 타이핑 시간을 단축하고 버그 발생 확률을 낮춥니다.
* **제로 코스트 추상화:** 문법 설탕은 런타임 오버헤드를 발생시키지 않습니다. 불필요한 배열 생성이나 LINQ 체인 대신, 트랜스파일러가 직접적이고 빠른 C# 코드로 변환합니다.
* **기본 불변성(Default Immutability):** 변수는 기본적으로 불변(`let`)이며, 필요할 때만 가변(`var`, `mut`)으로 선언하여 사이드 이펙트를 통제합니다.

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

- List : `list += x` (요소 추가), `list -= x` (요소 제거)
- HashSet: `visited += x` (요소 추가 및 `bool` 반환), `visited -= x` (요소 제거)
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
HashSet<bool> visited

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
* **자동 객체 생성 `new![n]`:** 컬렉션 배열을 선언할 때 내부 요소들까지 `new()`로 자동 초기화합니다. (예: `List<int>[] graph = new![n]`)
* **내장 집계 함수 (Aggregate Intrinsic):** `min`, `max`, `sum`, `sumBy`, `chmin`, `chmax` 등을 별도의 라이브러리 임포트 없이 즉시 사용합니다.
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
    a = find(a)
    b = find(b)
    if a == b then
        false

    if size[a] < size[b] {
        (a, b) = (b, a) // 튜플 스왑
    }

    parent[b] = a
    true // 암묵적 true 반환
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

## 구성

이 저장소에는 다음 구현이 들어 있습니다.

- C# 기반 `pscp` 트랜스파일러
- SDK 스타일 `pscp` CLI
- stdio 기반 `pscp` 언어 서버
- `.pscp` 파일용 VS Code 확장
- Windows 설치 프로그램

언어 및 언어 서버 관련 참고 문서는 [docs](./docs) 아래에 있습니다.
목록은 다음과 같습니다.

- `src/Pscp.Transpiler`: `pscp` -> C# 트랜스파일러
- `src/Pscp.Cli`: CLI 및 SDK 진입점 (`pscp.exe`)
- `src/Pscp.LanguageServer`: LSP 서버
- `src/Pscp.Installer`: Windows 설치기
- `tests/Pscp.Transpiler.Tests`: 트랜스파일러 스모크 테스트
- `vscode/pscp-vscode`: VS Code 확장
- `vscode/Build-Vsix.ps1`: VSIX 빌드 스크립트
- `installer/Build-Installer.ps1`: 설치기 빌드 스크립트

## 빠른 SDK 빌드

저장소에서 바로 CLI를 실행하려면:

```powershell
dotnet build src\Pscp.Cli\Pscp.Cli.csproj
dotnet run --project src\Pscp.Cli\Pscp.Cli.csproj -- init .\sample
```

그러면 다음이 생성됩니다.

- `sample\main.pscp`
- `sample\.pscp\Pscp.Generated.csproj`
- `sample\.pscp\Program.cs`

## 사용자 코드 실행 방법

설치된 SDK를 사용한다면 보통 아래 세 명령이면 충분합니다.

```powershell
pscp init
pscp run
pscp build
```

동작 방식:

- `pscp init`: 현재 폴더에 `main.pscp`, 하위 `.pscp` 폴더에 C# 프로젝트를 만듭니다.
- `pscp run [file.pscp]`: `.pscp\Program.cs`로 트랜스파일한 뒤 `dotnet run`까지 이어서 실행합니다.
- `pscp build [file.pscp]`: `.pscp\Program.cs`로 트랜스파일한 뒤 `dotnet build`를 수행합니다.
- 파일명을 생략하면 현재 폴더의 `main.pscp`를 자동으로 찾습니다.

예시:

```powershell
pscp run code.pscp
```

또는 현재 폴더에 `main.pscp`가 있다면:

```powershell
pscp run
```

입력 처리:

- `pscp run --stdin-file input.txt`: 파일 내용을 표준 입력으로 넘깁니다.
- `--stdin-file` 없이 실행하면 콘솔에서 직접 입력할 수 있습니다.
- 파이프/리다이렉션된 표준 입력도 그대로 프로그램으로 전달됩니다.

저장소에서 직접 실행할 때는 다음처럼 사용할 수 있습니다.

```powershell
dotnet run --project src\Pscp.Cli\Pscp.Cli.csproj -- run .\sample\main.pscp --stdin-file .\sample\input.txt
```

## CLI 명령

```text
pscp init [directory] [--force]
pscp check [file.pscp]
pscp transpile [file.pscp] [-o output.cs] [--print] [--namespace N] [--class-name C]
pscp build [file.pscp] [-c Debug|Release] [--release] [--debug]
pscp run [file.pscp] [--stdin-file input.txt] [-c Debug|Release] [--release] [--debug]
pscp lsp
pscp version
```

## Native AOT

Windows 배포용 빌드에서는 다음을 우선적으로 Native AOT로 publish합니다.

- `pscp.exe`
- `Pscp.LanguageServer.exe`
- `setup.exe`

`src/Pscp.Transpiler`는 라이브러리 프로젝트이므로 자체적으로 독립 exe를 만들지는 않지만, AOT 호환 속성을 적용했고 `pscp.exe`와 `Pscp.LanguageServer.exe` 안에 함께 포함됩니다.

언어 서버는 AOT 환경에서도 동작하도록 `System.Text.Json` 반사 기반 직렬화를 명시적으로 허용했습니다.

## Windows 설치기

설치기 빌드:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
```

필요하면 Native AOT를 끄고 단일 파일 self-contained 빌드로 강제할 수도 있습니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1 -NoNativeAot
```

빌드가 끝나면 설치기는 아래 경로에 생성됩니다.

```text
artifacts\installer\setup\setup.exe
```

기본 설치 동작:

- `%LOCALAPPDATA%\Programs\Pscp`에 설치
- `pscp.exe`와 `Pscp.LanguageServer.exe`를 함께 배치
- 사용자 PATH에 설치 경로 추가 시도
- Windows 프로그램 제거 목록에 등록 시도
- `uninstall.exe`를 함께 설치해 제거 가능

직접 설치/제거:

```powershell
.\artifacts\installer\setup\setup.exe
.\artifacts\installer\setup\setup.exe --install-dir D:\Tools\Pscp
D:\Tools\Pscp\uninstall.exe --uninstall --install-dir D:\Tools\Pscp
```

설치 후에는 새 터미널에서 바로 `pscp`를 사용할 수 있습니다.

## VS Code 확장

VS Code 확장은 [vscode/pscp-vscode](./vscode/pscp-vscode)에 있습니다.

기본 동작 순서:

1. 설정된 `pscp.languageServerPath` 사용
2. 설정된 `pscp.sdkPath` 사용
3. 설치된 PSCP SDK (`%LOCALAPPDATA%\Programs\Pscp\pscp.exe` 등) 자동 탐색
4. PATH의 `pscp.exe` 탐색
5. 마지막으로 저장소 개발 빌드 폴백 사용

개발 모드로 실행하려면:

```powershell
code .\vscode\pscp-vscode
```

그 뒤 `F5`로 Extension Development Host를 띄우고 `.pscp` 파일을 열면 됩니다.

VSIX 빌드:

```powershell
powershell -ExecutionPolicy Bypass -File .\vscode\Build-Vsix.ps1
```

생성 위치:

```text
artifacts\vscode\local.pscp-vscode-0.6.1.vsix
```

설치 방법:

```powershell
code --install-extension .\artifacts\vscode\local.pscp-vscode-0.6.1.vsix
```

또는 VS Code에서 `Extensions: Install from VSIX...`를 사용하면 됩니다.
