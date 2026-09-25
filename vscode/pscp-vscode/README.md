# PSCP VS Code Extension

이 확장은 `.pscp` 파일을 VS Code에서 편하게 다루기 위한 PSCP `v0.6`용 확장입니다.

포함 기능:

- `.pscp` 언어 등록
- v0.6 문법 하이라이팅
- 진단
- 자동완성
- hover
- definition / references
- document symbols
- semantic tokens
- signature help
- rename
- inlay hints
- status bar / server log
- 현재 파일 transpile/run 명령

언어 서버 연결 순서:

1. `pscp.server.path`
2. 호환 설정 `pscp.languageServerPath`
3. VSIX에 포함된 bundled language server
4. `pscp.sdkPath`
5. 설치된 PSCP SDK (`%LOCALAPPDATA%\Programs\Pscp\pscp.exe` 등)
6. PATH의 `pscp` 또는 `pscp.exe`
7. 저장소 개발 빌드 폴백

실행 파일 경로 설정(`pscp.server.path`, `pscp.languageServerPath`, `pscp.sdkPath`, `pscp.transpiler.path`)은 사용자/머신 설정에서만 읽습니다. 저장소의 `.vscode/settings.json`이 임의의 실행 파일을 지정할 수 없게 하기 위해서입니다.

`PSCP: Run Current File` / `PSCP: Transpile Current File`은 `pscp`를 셸을 거치지 않는 VS Code task로 실행합니다. 따라서 PowerShell, cmd, bash 어디서든 경로 인용 문제가 없고, 실행이 끝나도 출력이 남습니다.
깊은 재귀 때문에 큰 스택이 필요하면 `pscp.transpiler.args`에 `["--large-stack"]`을 지정하세요.

## VSIX 빌드

저장소 루트에서 다음 명령을 실행합니다.

```powershell
powershell -ExecutionPolicy Bypass -File .\vscode\Build-Vsix.ps1
```

생성 위치:

```text
artifacts\vscode\local.pscp-vscode-0.6.7.vsix
```

## 설치

다음 두 방법 중 하나로 설치할 수 있습니다.

1. VS Code의 `Extensions: Install from VSIX...`
2. 터미널에서:

```powershell
code --install-extension .\artifacts\vscode\local.pscp-vscode-0.6.7.vsix
```
