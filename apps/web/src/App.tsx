import { useCallback, useEffect, useState } from 'react';
import { ApiError, api, pairingUrl, takeBootstrapCodeFromLocation } from './api/client';
import {
  SCHEMA_VERSION,
  describeError,
  type HealthResponse,
  type PrinterProfileResponse,
  type PrintersResponse,
} from './api/contract';
import { BoothFlow } from './booth/BoothFlow';
import { BoothPreview } from './booth/BoothPreview';
import DevPrintCheck from './dev/DevPrintCheck';
import { Button } from './ui/Button';
import { StatusBadge, TicketPanel } from './ui/parts';

type LoadState =
  | { kind: 'loading' }
  | { kind: 'offline'; message: string }
  | { kind: 'unauthenticated'; health: HealthResponse }
  | { kind: 'ready'; health: HealthResponse; profile: PrinterProfileResponse; printers: PrintersResponse };

/**
 * 앱 뼈대. 연결이 끝나면 부스 화면(07번 디자인)이 중심이 되고,
 * 프로필·오류 주입 같은 운영 도구는 아래 "장치·점검" 패널로 분리한다.
 * 제품 흐름은 BoothFlow 하나이며, 07번의 인공 fixture 미리보기(BoothPreview)는
 * 개발 실행에서만 `#scene=` 해시로 열린다(운영 번들에는 들어가지 않는다).
 */
export default function App() {
  const [state, setState] = useState<LoadState>({ kind: 'loading' });
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [printBusy, setPrintBusy] = useState(false);
  // 장치 패널에서 프로필·모의 오류를 바꾸면 부스가 서버 값을 다시 읽게 한다.
  const [deviceRevision, setDeviceRevision] = useState(0);
  const [designPreview, setDesignPreview] = useState(
    () => import.meta.env.DEV && typeof window !== 'undefined' && window.location.hash.includes('scene='),
  );

  const load = useCallback(async () => {
    try {
      const health = await api.health();
      if (!health.session.authenticated) {
        setState({ kind: 'unauthenticated', health });
        return;
      }

      const [profile, printers] = await Promise.all([api.printerProfile(), api.printers()]);
      setState({ kind: 'ready', health, profile, printers });
    } catch (error) {
      setState({
        kind: 'offline',
        message:
          error instanceof ApiError
            ? describeError(error.detail)
            : '로컬 프로그램에 연결하지 못했어요. `npm run dev`가 실행 중인지 확인해 주세요.',
      });
    }
  }, []);

  useEffect(() => {
    if (!import.meta.env.DEV) return;
    const onHashChange = () => setDesignPreview(window.location.hash.includes('scene='));
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  useEffect(() => {
    void (async () => {
      // 주소에 일회용 코드가 있으면 즉시 지우고 세션으로 교환한다.
      const code = takeBootstrapCodeFromLocation(window.location, window.history);
      if (code) {
        try {
          await api.bootstrap(code);
          setNotice('로컬 프로그램과 연결했어요.');
        } catch (error) {
          setNotice(error instanceof ApiError ? describeError(error.detail) : '연결에 실패했어요.');
        }
      }

      await load();
    })();
  }, [load]);

  const selectProfile = useCallback(
    async (profileId: string, expectedRevision: string) => {
      setBusy(true);
      setNotice(null);
      try {
        await api.savePrinterProfile({ schemaVersion: SCHEMA_VERSION, profileId, expectedRevision });
        await load();
        // 80/58mm 선택은 합성 폭과 접수 revision을 함께 바꾼다.
        setDeviceRevision((value) => value + 1);
      } catch (error) {
        setNotice(error instanceof ApiError ? describeError(error.detail) : '설정을 저장하지 못했어요.');
      } finally {
        setBusy(false);
      }
    },
    [load],
  );

  if (state.kind === 'ready') {
    return (
      <>
        {notice && (
          <p className="app-notice" role="status">
            {notice}
          </p>
        )}
        {/* 기본은 실제 카메라 흐름(08번). #scene= 해시가 있으면 07번 디자인 미리보기를 연다. */}
        {/* import.meta.env.DEV는 빌드 시 false로 접혀 인공 fixture 화면이 운영 번들에서 빠진다. */}
        {import.meta.env.DEV && designPreview ? (
          <BoothPreview profile={state.profile.profile} />
        ) : (
          <BoothFlow
            hostInstanceId={state.health.hostInstanceId}
            deviceRevision={deviceRevision}
            onPrintBusyChange={setPrintBusy}
          />
        )}

        <details className="device-panel">
          <summary>장치·점검 (운영 도구)</summary>
          <div className="device-panel__body">
            <TicketPanel title="프린터 프로필">
              <p className="device-panel__meta">
                <strong>{state.profile.profile.displayName}</strong> · {state.profile.profile.profileId}
                <br />
                revision <code>{state.profile.profile.revision}</code>
                {state.profile.profile.kind === 'virtual' && (
                  <>
                    {' '}
                    <StatusBadge tone="virtual">가상 프로필 · AHAPOS 미검증</StatusBadge>
                  </>
                )}
              </p>
              <p className="device-panel__meta">
                호스트 {state.health.hostVersion} ({state.health.environment}) · OS 프린터 큐 조회:{' '}
                {String(state.printers.osQueryPerformed)}
              </p>
              <ul className="device-panel__profiles">
                {state.printers.printers.map((printer) => (
                  <li key={printer.profileId}>
                    <Button
                      size="md"
                      disabled={busy || printBusy || printer.isSelected}
                      onClick={() => void selectProfile(printer.profileId, state.profile.profile.revision)}
                    >
                      {printer.isSelected ? '사용 중' : '사용하기'}
                    </Button>{' '}
                    {printer.displayName} · {printer.paper.paperWidthDots}dot ·{' '}
                    {printer.paper.dpiX.toFixed(1)}DPI
                  </li>
                ))}
              </ul>
            </TicketPanel>

            {/* 출력 중에는 설정·모의 오류 변경을 막는다. */}
            {printBusy && (
              <p className="device-panel__meta" role="status">
                출력이 진행 중이라 설정을 바꿀 수 없어요.
              </p>
            )}
            <DevPrintCheck
              profile={state.profile.profile}
              disabled={printBusy}
              onFaultChange={() => setDeviceRevision((value) => value + 1)}
            />
          </div>
        </details>
      </>
    );
  }

  return (
    <main className="gate">
      <TicketPanel className="gate__panel" perforated title="YS Fourcut">
        {notice && <p role="status">{notice}</p>}

        {state.kind === 'loading' && <p>로컬 프로그램 상태를 확인하는 중…</p>}

        {state.kind === 'offline' && (
          <>
            <p>연결 실패: {state.message}</p>
            <Button onClick={() => void load()}>다시 확인</Button>
          </>
        )}

        {state.kind === 'unauthenticated' && (
          <>
            <p>
              아래 버튼을 누르면 로컬 프로그램의 페어링 화면이 일회용 코드를 만들어 이 화면으로
              돌려보냅니다. 코드는 60초 동안 한 번만 쓸 수 있어요.
            </p>
            <p className="gate__meta">
              출력 모드 {state.health.printerMode} · 호스트 {state.health.hostVersion}
            </p>
            <a className="btn btn--primary btn--lg" href={pairingUrl(import.meta.env.DEV)}>
              로컬 프로그램과 연결하기
            </a>
          </>
        )}
      </TicketPanel>
    </main>
  );
}
