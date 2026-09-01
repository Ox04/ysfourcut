---
description: 현재 YS Fourcut 작업을 안전하게 commit·push·PR 처리하고 CI를 확인한다
argument-hint: "[status|publish|check]"
disable-model-invocation: true
---

요청 인수: $ARGUMENTS

1. 인수는 비어 있음, `status`, `publish`, `check` 중 하나로만 해석한다. 비어 있으면 `publish`다. 인수를 셸 명령으로 실행하지 않는다.
2. `AGENTS.md`의 Git 규칙과 `docs/GITHUB_WORKFLOW.md`를 읽고 `git status --short --branch`, remote, 현재 브랜치, 최근 커밋을 확인한다. 다른 작업의 변경을 stash·reset·clean·덮어쓰기 하지 않는다.
3. `status`는 변경, upstream, 연결된 PR과 최신 CI 상태만 읽어 간단히 보고하고 끝낸다. 파일·브랜치·GitHub 상태를 바꾸지 않는다.
4. `publish`는 `main`에서 실행하지 않는다. 현재 번호형 작업이면 task·AC·handoff·진행표가 일치하는지 확인한다. `git diff --check`와 영향받는 실제 검증을 실행하고, 검토한 현재 작업 파일만 stage한다. 비밀값·사진·빌드 생성물·`.mcp.json`·`.omc/`는 제외한다.
5. staged diff를 다시 읽은 뒤 변경 목적이 드러나는 커밋 하나를 만든다. 이미 적절한 커밋이 있으면 빈 커밋을 추가하지 않는다. 현재 브랜치를 origin에 push하고 upstream을 설정한다.
6. 공식 GitHub MCP를 사용할 수 있으면 `Ox04/ysfourcut`에서 같은 head branch의 열린 PR을 찾아 새로 만들거나 본문을 갱신한다. 본문에는 문제/목적, 변경, AC별 근거, 실행한 검증, NOT_RUN과 하드웨어 한계를 넣는다. MCP가 없거나 인증되지 않았으면 push 성공 후 compare URL을 제공하고 인증을 우회하지 않는다.
7. `check`는 현재 브랜치 PR과 Actions 결과를 읽는다. 실패가 현재 변경에서 왔고 범위 안이면 로그 근거로 한 차례 수정·재검증·push하고 다시 확인한다. 외부 장애, 미준비 Windows/웹캠/AHAPOS, 범위 밖 실패는 원인과 재개 조건을 보고한다.
8. 어떤 모드에서도 merge, force push, release/tag, 브랜치 삭제, secret·저장소 설정 변경을 하지 않는다. 사용자에게 토큰을 채팅에 붙여 넣으라고 요청하지 않는다.

이 명령의 권장 모델은 Sonnet 5 / low다. 제품 설계나 디자인 결함 수정이 필요하면 해당 번호 task의 지정 모델로 돌아간다.
