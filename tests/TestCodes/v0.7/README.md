# PSCP v0.7 적합성 테스트

이 폴더의 프로그램들은 **0.6에서 문제가 됐고 0.7 초안에서 해결되는 코드**다. 스펙은 `docs/pscp_v_0_7_spec.md`, 항목별 해설은 `docs/pscp_v_0_7_changes.md`에 있다.

## 구성

프로그램마다 파일이 세 개씩 있다.

| 파일 | 내용 |
|---|---|
| `NN_name.pscp` | 테스트 프로그램. 머리 주석에 관련 변경 항목(A1, B4 …)과 트랜스파일러 0.6.7에서의 실제 결과를 적었다 |
| `NN_name.in` | 표준 입력 (`10_read_lines_crlf.in`은 일부러 CRLF 줄 끝을 쓴다) |
| `NN_name.out` | 0.7 스펙을 따를 때의 기대 출력 (LF 줄 끝, 마지막 줄바꿈 포함) |

## 실행 방법

`tests/Pscp.Transpiler.Tests`가 이 폴더를 `V07_*` 케이스로 읽어 변환하고 C#으로 컴파일한다. 프로그램을 실제로 돌려서 `.out`과 비교하려면 CLI의 샘플 실행기를 쓴다.

```sh
cd tests/TestCodes/v0.7
pscp test 01_grid_bfs_char_board.pscp      # 한 프로그램
for f in *.pscp; do pscp test "$f" || echo "FAIL $f"; done
```

`pscp test`는 한 번만 빌드하고 같은 폴더의 `.in`/`.out` 쌍을 모두 실행해 비교한다. 종료 코드는 모두 통과하면 0, 하나라도 실패하면 1, 빌드 실패면 2다. VS Code 확장의 `PSCP: Run Samples`도 같은 명령을 쓴다.

폴더가 상위 `tests/TestCodes`와 따로 있는 이유는 이 프로그램들이 기대 출력까지 검사받는 적합성 테스트이고, 상위 폴더는 컴파일 검사만 받기 때문이다.

## 0.6.7 결과 요약

아래는 이 프로그램들을 만든 이유, 즉 **0.6.7에서 무엇이 잘못됐는지**의 기록이다. 0.7 구현에서는 17개 모두 기대 출력을 낸다.

| 프로그램 | 0.6.7 결과 | 주요 변경 |
|---|---|---|
| 01_grid_bfs_char_board | C# 오류 (중첩 builder), 격자 입력 오독 | B1, B4 |
| 02_slice_explicit_bounds | PSCP 오류 | A3 |
| 03_implicit_return_calls | PSCP 오류 | A1, C12 |
| 04_pipe_and_line_continuation | PSCP 오류 | A2, C4, C7 |
| 05_space_call_groups | C# 오류 | C2, C3, C6 |
| 06_string_ordinal_order | 다른 출력 | B13, B12 |
| 07_output_rendering | C# 오류 (중첩 builder), 다른 렌더링 | B4, B6 |
| 08_integer_math | C# 오류 | B8, D7, B5 |
| 09_read_tokens_until_eof | PSCP 오류 | B3 |
| 10_read_lines_crlf | PSCP 오류 | B2, B3 |
| 11_types_constants_ordering | C# 오류 | B17, A6, D6 |
| 12_binary_search_and_find | PSCP 오류 | D7, B15, C8 |
| 13_patterns_and_ranges | PSCP 오류 | C8, B10 |
| 14_destructuring_iteration | PSCP 오류 | B11, C13 |
| 15_name_resolution | C# 오류 | A4, C11, A5 |
| 16_conversions | 다른 출력 | B6, B5 |
| 17_new_input_api | PSCP 오류 | D1, B16 |

기대 출력 중 BFS(01), 이분 탐색(12), 그래프 차수(14), 암묵적 반환(03), 큰 수 계산(08)은 Python 참조 계산으로 다시 확인했다.
