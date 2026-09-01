# 이 프로젝트의 Claude Code 모델 선택

확인일: 2026-08-31. 사용자는 **Max 5x**, Fable은 **Claude Fable 5**를 뜻한다. 아래 배정은 사용자 선호와 작업 난도를 반영한 추천이며 보장된 비용 계산표가 아니다.

## 모델을 쓰는 기준

| 모델 | 배정한 역할 | 이유 |
| --- | --- | --- |
| Fable 5 | 04 종이 외관, 07 전체 UI, 12 디자인 마감 | 사용자가 선호한 디자인 품질에 사용량을 집중 |
| Opus 5 | API/인증, 정확한 렌더링, 작업 상태, 카메라/합성, 통합 검수 | 복잡한 상태와 경계 구현에 주력 |
| Sonnet 5 | 01 실행 뼈대, 13 정해진 배포 작업, 14 문서 | 디자인 판단이 적고 산출물이 명확한 작업 |
| Fable 5, 필요할 때만 | 99 원인 불명 오류 해결 | 실패한 작업의 좁은 재현과 원인 조사만 수행 |

Sonnet에 디자인을 먼저 대충 만들게 한 뒤 Fable로 전부 다시 만드는 단계는 두지 않는다. 07에서 재사용할 실제 UI를 만들고 후속 작업은 기능만 연결한다. 12는 실제 사진·오류 상태·크기에서 한 번 마감하며 새 디자인 탐색을 반복하지 않는다.

## OMC 전환 시 유지할 것

- 06번 인계 이후 새 세션부터 `/ys-task 07`처럼 실행한다. 모델/effort는 위 배정대로 직접 고른다. 문서가 메인 모델이나 OMC worker 모델을 자동 설정하지 않는다.
- 07/12 디자인과 99 원인 조사는 Fable 메인 세션이 맡는다. OMC 기본 designer/executor가 Fable을 상속한다고 가정하지 않는다.
- 기본 OMC 실행은 구현자 한 명과 별도 Claude 검수 한 명으로 제한해 운영한다. 복잡한 상태·보안 검수는 Opus, 좁은 기계적 검수는 Sonnet을 권장한다. 추가 검수자의 실제 모델과 역할은 handoff에 적는다.
- `/ys-task 11 team`은 사용자가 고르는 선택 사항이다. Team은 실행 작업자 외 계획·검수 에이전트가 추가될 수 있어 메인 `/model`이나 `2:executor`만 보고 비용을 계산하지 않는다. [OMC 라우팅 설명](https://github.com/Yeachan-Heo/oh-my-claudecode/blob/main/skills/team/SKILL.md)
- 기존 사용자 전역 모델 라우팅/effort/추가 과금 설정은 이번 전환에서 수정하지 않는다. 실제 OMC 명령이 없으면 부재를 기록한다. 자세한 실행 범위는 [전환 안내](OMC_WORKFLOW.md)를 따른다.

`/ys-series 08 14`는 번호마다 별도 작업자를 만들어 08~11 Opus, 12 Fable, 13~14 Sonnet을 명시한다. 메인 세션의 모델을 자동 변경하지 않으며 effort를 바꿨다고 주장하지 않는다. 독립 검수는 08/12~14 Sonnet, 09~11 Opus다. Fable alias를 지원하지 않는 OMC에서는 12번 직전에 멈추고 다른 모델로 조용히 대체하지 않는다.

## 선택하는 방법

Claude Code에서 `/model`을 열어 표의 모델을 고르고 effort를 맞춘다. `/status`와 화면의 effort 표시로 실제 선택을 확인한다. 아래는 직접 지정할 때의 명령이며 한 줄씩 입력한다.

```text
/model claude-sonnet-5
/effort medium
```

```text
/model claude-opus-5
/effort high
```

```text
/model claude-fable-5
/effort high
```

08번은 Opus에서 `medium`, 14번은 Sonnet에서 `low`로 조정한다. 모델 설정 후 OMC 쿼리는 `/ys-task 07`, 일반 실행은 `07 진행해`다. 긴 MD 붙여 넣기, API 키 설정, 사용자 직접 에이전트 생성은 필요 없다.

새 세션으로 시작하는 WSL 명령 예시는 `claude --model claude-fable-5 --effort high`다. 프로젝트 설정 파일을 자동 변경하거나 모델 선택용 실행 스크립트를 추가하지 않았다. 이미 대화가 길다면 handoff를 저장한 뒤 새 세션을 쓴다.

공식 안내에서 Sonnet 5/Opus 5/Fable 5와 해당 모델 ID, `/model` 전환을 확인했다. 실제 계정의 제공 목록과 정책은 실행할 때 `/model`에서 확인한다. 로컬 CLI는 읽기 전용 조회에서 `2.1.251`이었으며 `--effort`가 제공됨을 확인했다. 계정별 사용량과 모델 호출은 조회/실행하지 않았다. [공식 모델 선택 안내](https://support.claude.com/en/articles/11940350-claude-code-model-configuration)

effort는 작업별 위 표를 사용한다. `max`/`xhigh`/`ultracode`를 상시 켜거나, 단순 작업에 최고 추론을 자동 적용하지 않는다. 지원되는 effort와 실제 적용 여부는 모델/설정에 따라 확인한다. [effort 공식 안내](https://code.claude.com/docs/en/model-config#adjust-effort-level)

## Max 5x 사용량을 다루는 방법

- Max 5x도 무제한은 아니다. 공식 안내는 5시간 단위 세션 한도와 공통 주간 한도를 설명한다. 실제 잔여량/갱신 시각은 `/usage` 또는 계정 Usage 화면에서 확인한다. [Max 정책](https://support.claude.com/en/articles/11049741-what-is-the-max-plan)
- 현재 Fable 안내에 따르면 Max에서 주간 한도의 최대 50%까지 Fable을 추가 비용 없이 사용할 수 있고, **별도의 추가 50%가 아니라 공통 주간 한도에서 소비**한다. 다른 모델보다 한도를 빨리 사용한다. 한도 이후 유료 사용을 자동 활성화하지 않는다. [Fable의 Max 사용 정책](https://support.claude.com/en/articles/15424964-claude-fable-5-on-your-plan)
- 정액제 한도를 API 토큰 단가로 환산해 `이 작업은 몇 %` 또는 `며칠 안에 끝남`이라고 예측하지 않는다. 읽은 파일·추론·재시도·테스트 출력에 따라 달라진다.
- `NN 진행해` 전에 해당 모델을 선택한다. `default`/`best`에 맡겨 원하는 모델이라고 가정하지 않는다. MD에 권장 모델을 써 두는 것만으로 실행 모델은 바뀌지 않는다.
- 세션당 작업 하나. 끝나면 handoff를 확인하고 `/clear` 후 다음 작업을 시작한다. `/clear`는 코드/파일을 지우지 않지만 이전 대화가 새 작업 문맥에 들어오지 않으므로 handoff부터 저장한다. 사용 한도를 리셋하는 명령은 아니다.
- task 파일의 필요한 절만 읽고, 장문 로그 대신 관련 오류를 짧게 남긴다. 파일을 MD로 옮긴 것 자체가 무료가 되는 것은 아니며 모델이 읽는 내용은 여전히 문맥을 차지한다. [문맥 관리 공식 안내](https://code.claude.com/docs/en/costs#manage-context-proactively)
- 같은 작업 안의 연속 디버깅에서는 무작정 `/clear`하지 않는다. 재현·결정·실패 원인을 보존하고, 실제로 새로운 단계나 모델로 넘어갈 때 초기화한다. 모델 변경/초기화마다 캐시 이득이 달라질 수 있어 고정 절감률은 제시하지 않는다.
- 기본은 병렬 구현 없이 실행하며 OMC의 독립 검수 비용은 별도다. 명시적인 `team` 요청에만 병렬 검수를 사용한다. 모델을 수시로 왕복하며 같은 파일을 재분석하지 않는다. 추가 과금·API 전환·크레딧 구매는 이 계획에 없다.

사용량이 부족하면 12번의 디자인 마감을 잠시 미루고 동작 가능한 11번 결과를 사용할 수 있다. 12번을 완료 처리하지는 않는다. 04/07에 쓸 Fable 여유를 먼저 남기는 편을 권장한다.
