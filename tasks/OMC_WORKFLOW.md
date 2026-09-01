# Oh My Claude Code 전환 안내

대상은 [Yeachan-Heo/oh-my-claudecode](https://github.com/Yeachan-Heo/oh-my-claudecode)다. 확인일 2026-08-31. 같은 이름의 다른 프로젝트와 구분한다. OMC는 Claude Code 위에서 작업을 조율하는 플러그인이며, 응답 속도나 Max 사용량 절감을 보장하지 않는다.

## 지금 전환하는 순서

1. 06번은 인계됐고 07번 디자인도 구현·독립 검수를 마쳤다. 실제 현재 상태는 진행표가 기준이며 02/05/06/07에 남은 Windows 브라우저 확인은 숨기지 않는다.
2. 다음 구현은 같은 WSL 프로젝트 `/home/msilot/ysfourcut`의 새 Claude Code 세션에서 시작한다. Windows 쪽 별도 복사본에서 구현하지 않는다.
3. OMC가 없다면 아래 설치 명령을 Claude Code 입력창에서 **한 줄씩** 실행한다. 이미 설치했다면 재설치하지 말고 `/help` 및 `/omc-doctor`로 사용 가능한 명령을 확인한다. 설치/설정은 이 문서 작성 작업에서 실행하지 않았다.

```text
/plugin marketplace add https://github.com/Yeachan-Heo/oh-my-claudecode
/plugin install oh-my-claudecode
/omc-setup
```

설정 도중 기존 프로젝트 `CLAUDE.md`의 `@AGENTS.md`와 번호형 라우터를 보존한다. 현재 프로젝트의 지침을 OMC 예시로 통째로 교체하지 않는다. 이 사용법에는 별도 API 키·npm 전역 CLI·tmux 작업자가 필요하지 않다. [공식 설치/실행 구분](https://github.com/Yeachan-Heo/oh-my-claudecode#quick-start)

4. `/model`에서 모델을 고른다. Fable 디자인 선호와 [기존 모델 배정](MODELS.md)은 유지한다. `/ys-task`에 모델 자동 변경 기능은 없다.
5. 한 번호씩 실행하려면 **`/ys-task 08`**이다. 08~14를 순서대로 자동 진행하려면 **`/ys-series 08 14`**를 사용한다. 새 명령 파일은 현재 실행 중인 Claude Code 세션에 자동 반영되지 않을 수 있으므로 새 세션에서 시작한다.

`/ys-task`는 이 저장소의 `.claude/commands/ys-task.md`에서 제공한다. 파일 내용을 복사하지 않는다. 새 세션에서도 명령이 안 보이면 프로젝트 경로/설정의 명령 로딩을 확인하고, 기존 `07 진행해`로 동일 task를 실행할 수 있다. 이는 OMC 명령을 실제 호출했다는 뜻은 아니다. [Claude Code 프로젝트 명령](https://code.claude.com/docs/en/skills)

## 자동으로 다음 번호 실행

프로젝트 명령 [`.claude/commands/ys-series.md`](../.claude/commands/ys-series.md)가 메인 관리자로 남고, 각 번호를 별도의 OMC executor에 순차 위임한다. 완료 문장만 보지 않고 handoff·진행표·AC 근거를 확인하고 별도 verifier가 승인한 뒤 다음 번호를 시작한다. 중단 후 같은 명령을 다시 쓰면 첫 미완료 번호부터 재개한다.

```text
/ys-series 08 14
```

- 08~11은 Opus, 12는 Fable, 13~14는 Sonnet 구현 작업자로 지정한다. 12의 검수자는 디자인을 변경하지 않는다.
- 작업자/검수자는 한 번에 하나씩 실행한다. `/team`, Autopilot, Ralph, Graph, `/goal`을 함께 켜지 않는다.
- `partial`이어도 필요한 선행 계약이 존재하고 남은 것이 다음을 막지 않는 환경 검수일 때만 넘어간다. `blocked`·필수 FAIL·handoff 누락에서는 멈춘다.
- 같은 원인 수정은 두 번까지만 하고, 이후 재현을 남겨 99번으로 인계한다. 90/91/99는 자동 범위에 넣지 않는다.
- rate limit이나 세션 종료를 영구 데몬처럼 우회하지 않는다. 다시 같은 명령을 입력하면 저장된 프로젝트 기록에서 이어간다.

OMC v5.0.2에는 순차 DAG용 Graph가 있지만 기본 agent 노드는 읽기 전용이며, command 노드는 외부 효과와 journal 기록 사이에서 중단되면 재실행될 수 있다. 따라서 이 프로젝트의 코드 수정 시리즈에는 Graph descriptor를 사용하지 않는다. [OMC Graph의 권한·재실행 경계](https://github.com/Yeachan-Heo/oh-my-claudecode/blob/v5.0.2/skills/graph/SKILL.md)

OMC v5 계열은 Fable 모델 별칭 처리를 추가했지만 설치된 버전에서 실제 호출을 확인해야 한다. 지원하지 않으면 12번을 다른 모델로 대체하지 않고 멈춘다. [OMC v5 릴리스](https://github.com/Yeachan-Heo/oh-my-claudecode/releases/tag/v5.0.2)

## 실행 규칙

- **범위:** 번호 하나 = 실행 단위 하나. 해당 task를 짧은 요구사항 문서로, `Acceptance Criteria`를 완료 조건으로 사용한다. 공통 판정은 [Definition of Done](DEFINITION_OF_DONE.md)이다. 이미 있는 목표를 다시 인터뷰하거나 전체 프로젝트 PRD를 새로 만들지 않는다.
- **기본:** 실제 설치된 OMC에 `oh-my-claudecode:execute`가 있으면 그 경로를 사용한다. 구현자는 메인 한 명으로 시작한다. 설계가 정해진 작은 작업에 Team/장기 루프를 자동 추가하지 않는다. 구현 후 현재 변경만 독립 검수하는 Claude 검수자 한 명을 허용한다. 보안·상태 경합은 Opus 검수, 좁은 기계적 확인은 Sonnet 검수를 권장한다.
- **버전 차이:** 조사한 main의 Execute와 구버전 Ralph의 반복·검수 절차는 같지 않다. `/execute`가 없다고 `/ralph`나 `autopilot`을 자동 대체 실행하지 않는다. 부재를 밝히고 일반 task 방식으로 진행할 수 있지만 권한 거부를 우회하거나 OMC 성공으로 표시하지 않는다. [Execute 원문](https://github.com/Yeachan-Heo/oh-my-claudecode/blob/main/skills/execute/SKILL.md)
- **디자인:** 07/12는 Fable 메인 세션이 실제 UI/이미지를 보고 구현한다. OMC 기본 designer/executor 라우팅에 디자인을 넘기지 않는다. 독립 검수자는 회귀·접근성·완료 기준만 확인하고 다른 테마로 재디자인하지 않는다.
- **계획 비용:** 시작 시 변경 모듈·최대 3~5개 실행 단위·대응 AC만 짧게 정한다. 선행 계약이 분명하면 전체 분석/계획/합의 단계를 반복하지 않는다. OMC가 자체 계획 파일을 요구하면 현재 번호와 AC ID를 연결한 요약만 만든다.
- **검수:** 관련 검증부터 수행하고 실제 결과를 handoff에 기록한다. 기존 테스트가 AC를 검증하면 재사용한다. 구현 후 별도 검수에서 발견한 결함을 수정하면 관련 근거를 갱신한다. 독립 검수 도구가 없으면 그 제한을 적고 자체 검증을 독립 승인으로 표시하지 않는다.
- **중단:** 같은 실패를 근거 없이 반복하지 않고 공통 완료 기준의 재시도 규칙을 따른다. 사용량 부족·카메라/Windows/장치 부재는 장기 루프로 해결하지 않는다. 진행 사실과 차단 조건을 저장하고 멈춘다. 명령 하나가 끝났다고 다음 번호로 넘어가지 않는다.

## 병렬 검수가 필요한 경우만

**`/ys-task 11 team`**은 선택 사항이다. 기존 일반 `/ys-task 11`도 사용할 수 있다. 이 선택만 현재 11번 범위의 OMC Team 호출을 허용하며 07/12 디자인이나 다음 번호의 병렬 구현을 허용하지 않는다.

| 담당 | 소유 범위 | 동시에 하지 않을 것 |
| --- | --- | --- |
| 검수 A | 촬영/이미지/화면 AC 조사, 웹 검증 결과 | B와 같은 파일 수정, 공용 서버 재시작 |
| 검수 B | 인증/작업 상태/수명 AC 조사, .NET 검증 결과 | A와 같은 파일 수정, 공용 세션 clear/실패 주입 |
| 메인 | 계약 결정, 공용 서버 검증 순서, 결함별 수정 담당 지정, 최종 기록 | 다른 세션 소유 서버/작업 강제 종료 |

처음에는 읽기·분석을 분리하고 실제 수정은 파일별 한 명에게만 맡긴다. 공유 서버를 중단/정리하는 검증은 직렬 실행한다. 각 작업자에게 전체 설계서 대신 소유 파일·AC·금지 범위만 전달한다.

`/team 2:executor`의 2는 실행 단계 작업자 수이며, 계획/검수 단계의 추가 에이전트까지 합친 고정 비용을 뜻하지 않는다. 기본 executor는 선택한 메인 모델과 다를 수 있으므로 실제 모델을 확인한다. 팀 기능 활성화가 필요하면 설치된 버전 안내를 따르며 이 저장소가 전역 설정을 자동 변경하지 않는다. [공식 Team 실행·라우팅](https://github.com/Yeachan-Heo/oh-my-claudecode/blob/main/skills/team/SKILL.md)

## 기록과 설정 경계

- 재개 기준은 저장소의 `tasks/PROGRESS.md`와 `tasks/handoffs/NN.md`다. OMC 내부 상태가 완료여도 프로젝트 AC가 미검증이면 done이 아니다.
- `.omc/` 내부 상태/PRD/잠금 파일은 플러그인 소유다. 문서 준비 과정에서 가짜 실행 상태나 `passes: true`를 만들지 않는다. 이 프로젝트용 별도 루트 `prd.json`도 만들지 않았다.
- OMC 런타임 기록에는 프롬프트/경로가 포함될 수 있다. `.gitignore`에 로컬 상태 제외를 추가한다. Git 밖에서 실행할 때 상태 위치는 설치된 OMC가 정한 경로를 확인하고 `.omc/`라고 추측하지 않는다. [공식 상태 저장 설명](https://github.com/Yeachan-Heo/oh-my-claudecode#omc-state-and-git)
- 모델별 자동 라우팅 설정, 추가 과금, 외부 Codex/Gemini 호출, 무제한 병렬/반복, 자동 커밋·푸시·배포는 설정하지 않았다. 숫자 상한은 문서 운영 규칙이지 실행을 강제로 차단하는 프로그램이 아니다.
- `/ys-task`로 06/07 구현·검수가 수행된 기록은 각 handoff에 있다. `/ys-series` 연속 실행은 아직 실제 Claude 세션에서 호출하지 않았다. 명령 작성 과정에서 앱 빌드·서버 조작·진행표 변경을 하지 않았으며 명령 구조와 문서 링크만 검증한다.
