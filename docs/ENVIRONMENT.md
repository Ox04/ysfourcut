# 실행 환경 (01번에서 확인)

작성일: 2026-08-31. 01번 작업에서 실제로 확인/설치한 내용만 적는다. 예상이나 계획은 적지 않는다.

## 확인된 사실

| 항목 | 상태 |
| --- | --- |
| 배포판 | Fedora Linux 42 (WSL), x86_64 |
| 프로젝트 경로 | `/home/msilot/ysfourcut`, Linux 파일시스템 |
| Node/npm (시스템 기본) | nvm 기본은 여전히 v25.2.1. 건드리지 않음 |
| Node 24 LTS | nvm에 `v24.19.0`(lts/krypton) 이미 설치돼 있음. 이 프로젝트는 `.nvmrc`로 `24.19.0` 고정 |
| npm | `v11.17.0` (Node 24.19.0에 동봉) |
| Linux .NET | 이전에는 미발견. **이번에 사용자 범위로 새로 설치함** |
| .NET 설치 방법 | 공식 `https://dot.net/v1/dotnet-install.sh --channel 10.0 --install-dir "$HOME/.dotnet"`. sudo/dnf/배포판 변경 없음 |
| .NET 버전 | SDK `10.0.400`, `dotnet --info` 정상 출력, RID `linux-x64` |
| dotnet PATH | 로그인 셸 PATH에는 아직 없음. `~/.dotnet/dotnet`에 있음. 로그인 셸 rc 파일은 수정하지 않음(전역 기본값 변경 금지) |
| fontconfig/freetype | 이전 확인 그대로 `2.16.0`/`2.13.3` 설치됨. Skia 자체는 03번에서 실제로 로드해 확인할 예정, 이번에는 시도하지 않음 |

## dotnet 실행 파일을 찾는 방법

이 저장소의 도구는 아래 순서로 `dotnet`을 찾는다 (`scripts/dev.mjs`):

1. `DOTNET_BIN` 환경 변수 (지정 시 그 경로를 그대로 사용)
2. 현재 셸의 `PATH`
3. 사용자 범위 설치 기본 위치 `~/.dotnet/dotnet`

새 셸에서 `dotnet` 명령을 직접 쓰려면:

```bash
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"
```

이 프로젝트는 이 두 줄을 셸 rc 파일에 자동으로 추가하지 않았다. 필요하면 사용자가 직접 추가한다.

## 실제로 실행/검증한 것 (WSL 안에서)

- `npm install`로 루트 lockfile 생성, `apps/web` + `packages/imaging` 워크스페이스 설치 성공.
- `npm run typecheck`, `npm run lint`, `npm run test`(Vitest 1개 통과), `npm run build`(Vite 프로덕션 빌드) 모두 성공.
- `dotnet build apps/print-host/YsFourcut.Host.csproj` 성공 (net10.0, 0 경고/0 오류).
- `npm run dev`로 Vite(5173)와 .NET(4317)를 함께 실행: `dotnet run`이 실제 자식 프로세스(`YsFourcut.Host`)를 띄우고 `Now listening on: http://127.0.0.1:4317` 로그 확인.
- `curl http://127.0.0.1:4317/api/health` 직접 호출: `{"status":"ok","printerMode":"virtual","environment":"Development"}` 200 응답.
- `curl http://127.0.0.1:5173/api/health` (Vite 프록시 경유, `Origin: http://127.0.0.1:5173`): 같은 본문 200 응답, `access-control-allow-origin: http://127.0.0.1:5173` 헤더 확인.
- `Origin: http://evil.example`로 같은 엔드포인트 호출: 응답 본문은 오지만 `access-control-allow-origin` 헤더가 없음 → 브라우저에서는 스크립트가 응답을 읽지 못함(CORS 차단). 서버가 요청 자체를 거부하지 않는 것은 CORS의 정상 동작이다.
- `dev.mjs`에 `SIGINT` 전달 후 프로세스 트리 확인: `dotnet run`, `YsFourcut.Host`(실제 앱), `vite`, `dev.mjs` 전부 종료. `ss -tlnp`로 4317/5173 포트가 닫힌 것 확인. 같은 SDK가 띄운 무관한 `VBCSCompiler` 공유 빌드 서버는 종료하지 않음(이 앱이 관리하는 프로세스가 아님).

## Windows 연결 추가 확인 (2026-08-31, Codex)

- 요청: WSL 포트 전달 설정. 확인 결과 NAT 모드의 기본 localhost forwarding이 이미 동작했고, 접속 대상 개발 서버가 종료된 상태였다.
- 기존 `scripts/dev.mjs`로 Node 24.19.0의 Vite와 .NET 호스트를 실행했다. Windows에서 `http://127.0.0.1:5173/`, `http://127.0.0.1:5173/api/health`, `http://127.0.0.1:4317/api/health` 모두 HTTP 200을 확인했다. 두 health 응답은 `status=ok`, `printerMode=virtual`, `environment=Development`였다.
- Windows의 Codex 내장 브라우저에서 실제 화면의 `YS Fourcut — 연결 확인` 제목과 `연결됨. printerMode=virtual, environment=Development` 문구를 확인했다. 별도 Chrome/Edge 앱이나 웹캠을 확인한 것은 아니다.
- Windows와 WSL 모두 프로젝트의 5173·4317 리스너가 `127.0.0.1`에만 바인딩된 것을 확인했다. 수동 portproxy, 방화벽 규칙, `.wslconfig` 변경 및 WSL 재시작은 필요하지 않아 수행하지 않았다.
- 확인 후 개발 서버는 실행 상태로 두었다. 부팅 시 자동 실행을 설정한 것은 아니다. 서버 종료·WSL 재시작 후에는 README의 `nvm use` → `npm run dev`로 다시 실행한다.

## 아직 확인하지 않은 것

- 별도 Windows Chrome/Edge 앱과 실제 웹캠: 이번 추가 확인은 Codex 내장 브라우저로 수행했다. 웹캠은 08번에서 확인한다.
- **Linux Skia 로드**: fontconfig/freetype 존재만 확인했고, 실제 Skia 네이티브 라이브러리 로드와 PNG 생성은 03번에서 CrossEscPos 모듈을 붙일 때 확인한다.
- Fedora 42가 아직 .NET 공식 지원 배포판 목록에 있는지는 재확인하지 않았다(이전 기록: 42 미포함, 43/44만 명시). 이번 설치는 dotnet-install.sh 사용자 범위 스크립트로 성공했지만 이것이 공식 지원 상태 변경을 의미하지는 않는다.
