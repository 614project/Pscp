# PSCP

`pscp`는 문제 풀이 스타일 문법을 갖춘 실험적 언어이며, 이 저장소에는 다음 구현이 들어 있습니다.

- C# 기반 `pscp` 트랜스파일러
- SDK 스타일 `pscp` CLI
- stdio 기반 `pscp` 언어 서버
- `.pscp` 파일용 VS Code 확장
- Windows 설치 프로그램

언어 및 언어 서버 관련 참고 문서는 [docs](./docs) 아래에 있습니다.

## 구성

- `src/Pscp.Transpiler`: `pscp` -> C# 트랜스파일러
- `src/Pscp.Cli`: CLI 및 SDK 진입점 (`pscp.exe`)
- `src/Pscp.LanguageServer`: LSP 서버
- `src/Pscp.Installer`: Windows 설치기
- `tests/Pscp.Transpiler.Tests`: 트랜스파일러 스모크 테스트
- `vscode/pscp-vscode`: VS Code 확장
- `vscode/Build-Vsix.ps1`: VSIX 빌드 스크립트
- `installer/Build-Installer.ps1`: 설치기 빌드 스크립트

## SDK 빠른 시작

저장소에서 바로 CLI를 실행하려면:

```powershell
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1'
$env:DOTNET_CLI_HOME='D:\Projects\Pscp\.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet build src\Pscp.Cli\Pscp.Cli.csproj
dotnet run --project src\Pscp.Cli\Pscp.Cli.csproj -- init .\sample
```

그러면 다음이 생성됩니다.

- `sample\main.pscp`
- `sample\.pscp\Pscp.Generated.csproj`
- `sample\.pscp\Program.cs`

`pscp init`은 현재 폴더에도 바로 실행할 수 있습니다.

```powershell
pscp init
```

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
artifacts\vscode\local.pscp-vscode-0.1.0.vsix
```

설치 방법:

```powershell
code --install-extension .\artifacts\vscode\local.pscp-vscode-0.1.0.vsix
```

또는 VS Code에서 `Extensions: Install from VSIX...`를 사용하면 됩니다.

## 검증

주요 검증 명령:

```powershell
dotnet build src\Pscp.Cli\Pscp.Cli.csproj
dotnet build src\Pscp.LanguageServer\Pscp.LanguageServer.csproj
dotnet build src\Pscp.Installer\Pscp.Installer.csproj
dotnet run --project tests\Pscp.Transpiler.Tests\Pscp.Transpiler.Tests.csproj
powershell -ExecutionPolicy Bypass -File .\installer\Build-Installer.ps1
powershell -ExecutionPolicy Bypass -File .\vscode\Build-Vsix.ps1
```

추가로 다음 흐름을 직접 확인했습니다.

- Native AOT `pscp.exe version`
- Native AOT `pscp.exe init` / `pscp.exe run`
- Native AOT `Pscp.LanguageServer.exe`의 `initialize` 응답
- `setup.exe` 설치 후 `pscp.exe version`
- `uninstall.exe --uninstall ...` 후 설치 폴더 삭제
- VSIX 생성 후 내부 필수 엔트리 검증
