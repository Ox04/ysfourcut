@AGENTS.md

번호형 요청은 tasks/NN.md의 해당 작업 하나만 실행한다. 예: `01 진행해` → tasks/01.md.
`/ys-task NN`은 .claude/commands/ys-task.md의 프로젝트 진입점이다. OMC 절차는 명시적으로 호출할 때만 적용하고 진행 중인 기존 작업에 새 모드를 끼워 넣지 않는다.
`/ys-series START END`는 .claude/commands/ys-series.md의 순차 실행 진입점이다. 직접 호출된 범위에서만 번호별 구현→독립 검수→다음 번호를 수행한다.
전체 순서/모델 표는 TASKS.md, 진행 기록은 tasks/PROGRESS.md다. 모두 자동으로 읽으라는 뜻은 아니다.
장문의 설계서 전체나 모든 작업을 자동 로딩/연속 실행하지 않는다. 완료/중단 시 handoff를 저장하고 해당 작업에서 멈춘다.
