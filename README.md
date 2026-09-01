# YS Fourcut

웹캠 네컷 → 최종 흑백 비트맵 → 동일한 .NET API → 영수증. 지금은 WSL 가상 출력 개발 단계다. 전체 배경은 `AGENTS.md`, `SERVICE_DESIGN.md`, `WSL_DEVELOPMENT.md`를 따른다.

## 요구 사항

- WSL의 Fedora Linux, Node 24 LTS(`.nvmrc` 참고), Linux용 .NET 10 SDK.
- 실제 확인된 환경과 설치 방법은 `docs/ENVIRONMENT.md` 참고.

## 실행

```bash
cd /home/msilot/ysfourcut
nvm use
npm ci
npm run dev
```

- `npm run dev`는 Vite(`http://127.0.0.1:5173`)와 Linux .NET 가상 호스트(`http://127.0.0.1:4317`)를 함께 띄운다.
- Windows Chrome/Edge에서 `http://127.0.0.1:5173`로 접속해 화면을 확인한다. 실제 웹캠은 Windows 브라우저에서 사용한다.
- `Ctrl+C`로 종료하면 이 스크립트가 시작한 두 프로세스만 정리한다. 다른 서버는 건드리지 않는다.
- `dotnet` 실행 파일을 PATH에서 찾지 못하면 `DOTNET_BIN` 환경 변수로 경로를 지정하거나 PATH에 추가한다 (`docs/ENVIRONMENT.md` 참고).

### 로컬 프로그램과 연결

화면을 처음 열면 세션이 없어 `연결이 필요해요`가 보인다. **로컬 프로그램과 연결하기**를 누르면
호스트의 페어링 화면이 일회용 코드를 만들어 화면 주소의 fragment로 돌려보내고, 앱이 주소에서 코드를 지운 뒤
세션 쿠키로 교환한다. 코드는 60초·1회만 유효하다. 버튼 대신 `http://127.0.0.1:4317/pair`를 직접 열어도 된다.
상세 계약은 `docs/API_CONTRACT.md`에 있다.

### Windows 브라우저·웹캠 확인 절차

1. WSL에서 `npm run dev`를 실행한 상태를 유지한다(같은 창을 닫지 않는다).
2. Windows의 Chrome 또는 Edge에서 `http://127.0.0.1:5173`을 연다(WSL↔Windows `127.0.0.1` 루프백은
   WSL2의 기본 지원 기능이며 별도 portproxy/방화벽 설정이 필요 없다. `docs/ENVIRONMENT.md` 참고).
3. `연결이 필요해요` 화면에서 **로컬 프로그램과 연결하기**를 누르고 페어링을 완료한다.
4. 시작 화면에서 카메라 권한 허용 팝업을 승인하면 실제 웹캠 미리보기가 뜬다(`getUserMedia()`).
5. 네 컷 촬영 → 필요한 컷만 다시 찍기 → 편집(문구 입력) → 가상 출력까지 실제 웹캠으로 한 번 진행해 본다.
6. 확인 후 부스 화면을 닫기 전에 정리(clear)까지 마쳤는지 결과 화면의 안내 문구로 확인한다.

이 저장소가 WSL 헤드리스(가짜 카메라)로 확인한 것과 Windows 실물 브라우저/웹캠으로 확인한 것은
서로 다른 근거다. 각 번호 handoff와 `docs/VALIDATION.md`가 이 둘을 구분해서 기록한다(아래 "검수 상태" 참고).

### 가상 80/58mm 전환

화면 아래 **장치·점검 (운영 도구)** 접이식 패널을 열면 "프린터 프로필" 카드에 현재 프로필과
선택 가능한 프리셋 목록(`virtual-80mm-8dpmm`, `virtual-58mm-8dpmm`)이 dot 폭·DPI와 함께 나온다.
원하는 프리셋의 **사용하기** 버튼을 누르면 접수 revision과 편집 화면의 합성 폭이 함께 바뀐다.
출력이 진행 중일 때는 이 패널의 조작이 막힌다(`disabled`, "출력이 진행 중이라 설정을 바꿀 수 없어요").
두 프리셋 모두 `kind=virtual`이고 `hardwareVerified=false`다 — AHAPOS 실측값이 아니다(`docs/PRINTER_REFERENCE.md`).

### PNG 저장 · 서버 결과 정리(삭제)

- 결과 화면에서 출력이 끝나면(`rendered`) **PNG 저장** 버튼으로 사용자가 직접 파일로 저장할 수 있다.
  서버가 자동으로 파일을 만들거나 남기지 않는다.
- **다음 촬영**을 누르면 브라우저의 원본·합성 이미지와 서버에 남은 결과(content/paper/appearance PNG)를
  함께 정리한다. 서버 정리 결과(`remainingJobs`)가 0이 아니면 화면에 "정리하지 못했어요"가 뜨고
  **다시 정리하기** 버튼으로 재시도한다.
- 장치·점검 패널의 **서버 결과 정리** 버튼으로 언제든 수동 정리를 다시 시도할 수 있다(같은 세션 범위만
  정리한다 — 다른 세션 결과나 서비스 전체 큐를 지우는 기능은 없다).

### 오류 시험 (모의 오류 패널)

장치·점검 패널의 "가상 장치" 항목에서 모의 오류를 하나 선택하면(`용지 없음`, `덮개 열림`, `오프라인`,
`출력 지연`, 렌더 도중 실패 등) **다음 출력 1회**에만 그 오류가 주입된다. 실물 장치 상태가 아니라는
안내가 함께 표시된다. 결과 화면은 실패 상태(`virtual_failed`)와 실패 코드를 그대로 보여주며 성공으로
위장하지 않는다. 시험이 끝나면 select를 **모의 오류 없음**으로 되돌린다(아래 "문제 해결" 참고).

## 명령

| 명령 | 설명 |
| --- | --- |
| `npm run dev` | Vite + .NET 가상 호스트 동시 실행 |
| `npm run build` | 웹 프로덕션 빌드(`apps/web/dist`) |
| `npm run build:host` | .NET 호스트 빌드 |
| `npm run typecheck` | `packages/imaging`, `apps/web` TypeScript 검사 |
| `npm run lint` | `apps/web` ESLint |
| `npm run test` | `apps/web` Vitest |
| `npm run test:host` | `tests/print-host` .NET 테스트 |
| `npm run test:all` | 웹 + .NET 테스트 |
| `npm run package:win` | 웹 빌드 + Windows(`win-x64`) self-contained 배포 폴더 생성. 아래 "Windows 배포판" 참고 |

`build:host` / `test:host`는 `scripts/dotnet.mjs`가 `dotnet`을 찾아 실행하므로 PATH 설정 없이도 동작한다.

## 폴더

- `apps/web` — React/TypeScript/Vite 화면. 시작→촬영→편집→가상 출력→결과 부스 흐름(`src/booth/`)과
  연결/프로필/모의 오류 운영 도구(`src/dev/DevPrintCheck.tsx`, `App.tsx`의 "장치·점검" 패널)가 있다.
  API 계약은 `src/api/`.
- `apps/print-host` — .NET `net10.0` 호스트. health/bootstrap/printers/printer-profile/print-jobs/artifacts
  전부 구현(가상 렌더링 포함). Windows 배포는 `wwwroot/`를 직접 서빙(아래 "Windows 배포판" 참고).
- `packages/imaging` — 브라우저용 순수 TS 이미지 함수(합성·디더링·1비트 패킹).
- `tests/print-host` — .NET 계약·인증·렌더링·수명주기 테스트.
- `docs/ENVIRONMENT.md` — 실제 확인된 WSL 환경과 설치 방법.
- `docs/API_CONTRACT.md` — 실제 경로·타입·오류 코드와 담당 코드 위치.
- `docs/PRINTER_REFERENCE.md` — AHAPOS 실물 프린터에 대해 확인된 사실과 아직 확인하지 못한 항목.
- `docs/VALIDATION.md` — 11번 통합 검수의 WSL 자동/헤드리스/격리 호스트 근거와 Windows·실물 NOT_RUN 목록.
- `docs/HARDWARE_CHECKLIST.md` — 후속 AHAPOS 실물 검수(90/91번)용 체크리스트. 아직 미체크.

이 저장소는 `tasks/NN.md` 단위로 진행하며, 완료 상태는 `tasks/PROGRESS.md`에 기록한다.

## 검수 상태 (네 갈래)

"확인했다"를 하나로 뭉치지 않는다. 아래 네 갈래는 서로 다른 근거이며 섞어 쓰지 않는다. 상세는
`docs/VALIDATION.md`(WSL/격리 호스트 근거)와 각 `tasks/handoffs/NN.md`(번호별 partial/NOT_RUN 사유)에 있다.

| 갈래 | 상태 | 근거 |
| --- | --- | --- |
| WSL 가상 검수 (자동 시험 + 헤드리스 가짜 카메라) | 대체로 완료 | `npm run test/test:host` 웹 129·.NET 164, Playwright 헤드리스 E2E·격리 호스트 배터리(`docs/VALIDATION.md`) |
| Windows 가상 검수 (실물 브라우저·웹캠, Windows 배포 실행) | **NOT_RUN** | 02/05/06/07/08/09/10/11/13 handoff 공통. 실제 Windows 머신 필요. 14번은 문서만 바꿔 새 UI가 없으므로 별도 항목 없음 |
| 디자인 마감 (04/07/12번) | WSL 범위 완료 | `docs/UI_DESIGN.md`, `tasks/handoffs/12.md`(독립 검수 결함 0건). Windows 실물 렌더링 확인은 위 Windows 가상 검수에 포함 |
| AHAPOS 실물 검수 | 미착수 | `docs/PRINTER_REFERENCE.md`(미확인 항목), `docs/HARDWARE_CHECKLIST.md`(90/91번 대상) |

## 문제 해결

보안 검사를 끄거나(Origin/헤더 검증 해제, `0.0.0.0` 공개) 서비스 전체 결과를 지우는 것은 해결책으로 쓰지 않는다.
아래는 실제 오류 코드·문구를 기준으로 한 최소 복구다.

| 증상 | 실제 오류/문구 | 복구 |
| --- | --- | --- |
| 서버 미실행 | 화면에 "연결 실패: 로컬 프로그램에 연결하지 못했어요. `npm run dev`가 실행 중인지 확인해 주세요." | WSL 터미널에서 `npm run dev`가 살아 있는지 확인하고, 죽었으면 다시 실행한 뒤 화면의 **다시 확인** 버튼을 누른다 |
| 권한 거부(401) | `SESSION_REQUIRED`/`SESSION_INVALID` — "로컬 프로그램과 연결해 주세요" / "연결이 만료됐어요. 다시 연결해 주세요" | **로컬 프로그램과 연결하기**로 다시 페어링한다. 세션 인증을 우회하거나 검사를 끄지 않는다 |
| 권한 거부(403) | `HOST_NOT_ALLOWED`/`CLIENT_HEADER_REQUIRED`/`ORIGIN_NOT_ALLOWED`(`docs/API_CONTRACT.md`) | 주소가 `localhost`가 아니라 정확히 `127.0.0.1:5173`인지 확인한다. Vite 프록시를 거치지 않고 4317에 직접 다른 Origin으로 요청하지 않는다 |
| 포트 충돌(5173) | Vite가 `strictPort: true`라서 5173이 이미 쓰이면 시작에 실패하고 종료한다 | `ss -tlnp \| grep :5173`(또는 `lsof -i :5173`)으로 점유 프로세스를 찾는다. **자신의 이전 `npm run dev`인지 먼저 확인**하고, 맞으면 그 창에서 `Ctrl+C`로 종료 후 재실행한다. 모르는 프로세스면 강제로 죽이지 말고 원인부터 확인한다. `0.0.0.0` 바인딩이나 portproxy 추가로 우회하지 않는다 |
| 포트 충돌(4317) | .NET Kestrel이 바인딩에 실패하면 `dev.mjs`가 "dotnet-host 프로세스가 종료됨"을 출력하고 Vite까지 함께 정리한다 | 위와 같은 방식으로 4317 점유 프로세스를 확인한다. 다른 WSL 세션이 이미 개발 서버를 띄워 둔 경우가 흔하므로, 그 세션을 이어 쓸지 자신이 새로 띄울지부터 판단한다(handoff 11/12/13이 남긴 공유 서버 소유권 메모 참고) |
| 세션/코드 만료 | `BOOTSTRAP_CODE_EXPIRED`("연결 코드가 만료됐어요"), `BOOTSTRAP_CODE_ALREADY_USED`, `SESSION_INVALID` | 코드는 60초·1회용이다. **로컬 프로그램과 연결하기**를 다시 눌러 새 코드로 재페어링한다 |
| 삭제(정리) 실패 | 결과 화면 "정리하지 못했어요" 또는 서버 결과 정리 알림 "서버에 결과 N건이 남아 있어요" — 진행 중 작업이 있으면 `409 PRINTER_BUSY` | 출력이 끝날 때까지 기다린 뒤 **다시 정리하기**(또는 장치·점검 패널의 **서버 결과 정리**)를 다시 누른다. 정리는 같은 세션 범위만 지운다 — 다른 세션/서비스 전체 큐를 지우는 별도 경로는 없고 만들지 않는다 |
| 가상 오류 주입 상태가 안 풀림 | 장치·점검 패널에 "주입된 모의 오류: …(다음 작업 1회)"가 계속 보이거나, 정상 출력을 시켰는데 계속 실패 | 모의 오류는 **다음 출력 1회**에만 적용되므로 한 번 더 출력하면 소모되어 사라진다. 즉시 되돌리려면 장치·점검 패널의 select를 **모의 오류 없음**으로 바꾼다(`POST /api/virtual-printer/fault`, `fault: null`) |

## Windows 배포판 (13번, virtual 전용)

**개발 실행**(WSL, `npm run dev`)은 Node 24 LTS와 Linux용 .NET 10 SDK가 설치돼 있어야 한다.
**Windows 배포 실행**(`dist/win-x64/YsFourcut.Host.exe`)은 self-contained publish라 대상 Windows PC에
.NET SDK/런타임 설치가 필요 없다 — 대신 이 exe 자체는 **이 세션에서 실행을 확인하지 않았다(NOT_RUN)**.
아래는 13번이 WSL에서 실제로 만들고 검사한 산출물 구조이며, 실행 확인 자체는 별개다.

`npm run package:win`은 웹을 빌드해 `apps/print-host/wwwroot/`(재생성되는 폴더, 저장소에 커밋하지 않음)에
넣은 뒤 `dotnet publish -r win-x64 --self-contained -o dist/win-x64`를 실행한다. Native AOT/ReadyToRun은
쓰지 않는다. `dist/`는 `.gitignore` 대상이며, 이 스크립트는 `dist/win-x64/`와
`apps/print-host/wwwroot/`만 만들고 다른 배포 폴더는 건드리지 않는다.

- 결과 폴더는 `YsFourcut.Host.exe`, .NET 런타임 dll(`coreclr.dll`, `hostfxr.dll` 등, self-contained라 대상
  PC에 .NET SDK/런타임 설치가 필요 없다), Windows용 Skia 네이티브(`libSkiaSharp.dll`), `wwwroot/`(웹 빌드 +
  한글 서브셋 폰트), `Profiles/*.json`(가상 프린터 프리셋)을 포함한다. Linux `.so` 파일은 포함되지 않는다.
- 기본 환경(`appsettings.json`)의 `PrinterMode`는 `virtual`이다. 개발 환경(`Development`)이 아닐 때만
  이 호스트가 `wwwroot`를 직접 서빙한다(`Program.cs`) — Windows 브라우저 → 이 호스트 하나로 화면과 API를
  모두 받는다.
- **WSL(Linux, 헤드리스)에서 확인 가능한 것**: publish 성공, 산출물 파일 목록·RID·용량, Windows 네이티브
  파일 존재. **WSL에서 확인 불가능한 것**: 실제 Windows 실행, 브라우저 bootstrap, 인공 PNG 생성, 오프라인
  자산 로딩. 이 부분은 실제 Windows 머신에서 `dist/win-x64/YsFourcut.Host.exe`를 실행해 확인해야 한다
  (`tasks/handoffs/13.md` 참고).
