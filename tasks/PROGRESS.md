# 작업 진행표

상태: **14번 사용 문서 완료(README에 WSL 개발 실행/Windows 브라우저·웹캠 절차/가상 80·58mm 전환/PNG 저장·정리/모의 오류 시험/문제 해결 절, docs/HARDWARE_CHECKLIST.md 신설, 문서-실제 파일·명령 대조). 13번 Windows 패키징 완료(`npm run package:win` 신설, dist/win-x64 self-contained 산출물의 Win32 Skia 네이티브·폰트·wwwroot 구조 직접 확인, 독립 검수 통과·결함 0건). Windows 실행 자체는 실물 머신 없어 NOT_RUN. 12번 디자인 마감 완료(독립 검수 통과·결함 0건). 11번 통합 검수 완료(웹 129 + .NET 164). 02/05/06/07/08/09/10/11/13/14 공통으로 Windows 브라우저·실물 웹캠·Windows 실행 확인이 남음. 90/91(AHAPOS 실물)은 여전히 deferred.**
현재/마지막 작업: 14 (완료, /ys-series 13 14. 90/91/99 자동 시작하지 않음)
99번에서 해결할 대상: 없음

| 번호 | 상태 | handoff·메모 |
| --- | --- | --- |
| 01 | done | tasks/handoffs/01.md — WSL 뼈대 검증 완료. Windows에서 5173·4317 HTTP 200 및 브라우저의 virtual 연결 문구 확인 |
| 02 | partial | tasks/handoffs/02.md — API 계약·프로필·인증 구현, .NET 65 + 웹 8 테스트와 실서버 curl 확인 완료. Windows 브라우저에서 연결 화면 확인만 남음 |
| 03 | done | tasks/handoffs/03.md — CrossEscPos Skia 1.2.0 복원·Linux 네이티브 확인, content/paper PNG 픽셀 일치와 640×1876 치수 검증(테스트 78개) |
| 04 | done | tasks/handoffs/04.md — 종이색·질감·그림자·절취 3종 구현, content/paper 픽셀 불변·seed 재현성 검증(테스트 92개), 이미지 직접 확인 |
| 05 | partial | tasks/handoffs/05.md — 별도 worker·중복 방지·잠금·메모리 artifact(TTL/한도)·작업 기록 구현, .NET 109 테스트와 실서버 curl 확인. 개발 확인 화면의 브라우저 동작만 미확인 |
| 06 | partial | tasks/handoffs/06.md — 독립 검수 2회(구현 검수 + 수정 검수) 반영. 결함 5건 수정으로 AC-06-01b·05 종결, 세션 격리 강화. .NET 135 통과. **브라우저 확인만 남음** |
| 07 | partial | tasks/handoffs/07.md — 영수증 티켓 방향 확정, 토큰·컴포넌트·5화면+장치 패널 구현. AC 5개 PASS(헤드리스 Chromium 실측+독립 검수, 넘침 3건 수정 후 전수 측정). Windows 브라우저 확인만 남음 |
| 08 | partial | tasks/handoffs/08.md — 순수 촬영 상태 머신 + 카메라 훅 + BoothFlow 연결. AC-01/02/03/05 PASS(가짜 카메라 실측, 검수 결함 6건 수정), AC-08-04 실물 웹캠 NOT_RUN |
| 09 | partial | tasks/handoffs/09.md — 레이아웃·디더링·Worker·최종 1비트 파이프라인 구현. 독립 검수 2회(구현 검수 + 수정분 재검수) 통과, HIGH 0건·지적 3건 수정. AC 5개 PASS(웹 101 + .NET 162 테스트, 헤드리스 실측 19건, 서버 content PNG와 픽셀 0 차이). Windows 브라우저·실물 웹캠 확인만 남음 |
| 10 | partial | tasks/handoffs/10.md — 촬영→편집→가상 출력→결과→다음 촬영 연결, 접수/조회/정리 순수 상태 머신 신설. AC-10-02~06 PASS(웹 129 + .NET 162 테스트, 헤드리스 실측 77건, 실패 주입 5종), AC-10-01은 Windows 브라우저·실물 웹캠 NOT_RUN. 독립 검수 통과(HIGH 0건, 판단 2건은 11번으로) |
| 11 | partial | tasks/handoffs/11.md — 통합 검수. AC-11-01~06 전부 PASS(웹 129 + .NET 164, 헤드리스 E2E 12, 격리 namespace 호스트 배터리 14, .NET 중단 앱 시험 5, 실시간 TTL 10분). 결함 3건 수정(종료 예외·/pair 코드 로그 유출·개발 버튼 운영 노출) + 회귀 2건. Windows 브라우저·실물 웹캠 NOT_RUN. 독립 검수 통과(HIGH 0건, 역검증으로 수정 3건 확인) |
| 12 | partial | tasks/handoffs/12.md — 시작 카메라 카드·컷 다시 찍기 pill·정리 차단 제목·안내 문구 4건 마감(Fable 메인 직접). AC-12-01/02/04/05 PASS, 03 N/A(외관 미변경), 웹 129 테스트·번들 게이트 유지. Windows 브라우저·실물 웹캠 NOT_RUN. 독립 검수(Sonnet) 통과, 결함 0건 |
| 13 | partial | tasks/handoffs/13.md — `npm run package:win` 신설(웹 빌드+win-x64 self-contained publish, dist/win-x64). wwwroot 자산·한글 폰트·Win32 Skia dll(`libSkiaSharp.dll`) 포함과 Linux `.so` 0건 직접 확인, self-contained 런타임 dll 구조 확인. 웹 129·NET 164 회귀 없음. **Windows 실행(AC-13-03/04) NOT_RUN** — 실제 Windows 머신 필요 |
| 14 | done | tasks/handoffs/14.md — README에 개발/Windows 배포 실행 구분, 가상 80/58mm·PNG 저장/정리·모의 오류 시험 절, 문제 해결(서버 미실행/401·403/포트 충돌/코드 만료/정리 실패/오류 주입 복구) 추가. docs/HARDWARE_CHECKLIST.md 신설(전부 미체크). 11~13의 partial/NOT_RUN 그대로 보존, 코드 미변경. 독립 검수(Sonnet) 통과, 결함 0건(표 각주 1건 반영) |
| 90 | deferred | 프린터 준비 후 조사 |
| 91 | deferred | 실물 어댑터·검수 |
| 99 | on_demand | 원인 불명 실패가 있을 때만 |

상태는 pending / in_progress / done / partial / blocked / deferred / on_demand를 사용한다. 실제 작업을 시작하거나 끝낼 때만 갱신한다. 환경 때문에 일부 검증이 남으면 done으로 덮지 않고 partial/blocked와 정확한 사유를 적는다.
완료 작업을 다시 요청받으면 handoff와 산출물을 확인하고, 누락된 완료 조건만 처리한다. 위의 한 줄 메모에 대화 전체나 로그를 누적하지 않는다.
