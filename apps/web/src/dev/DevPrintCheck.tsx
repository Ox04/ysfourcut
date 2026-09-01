import { useCallback, useEffect, useRef, useState } from 'react';
import { packMonoBitmap, toBase64 } from '@ysfourcut/imaging';
import { ApiError, api, fetchArtifactBlobUrl } from '../api/client';
import {
  BIT_ORDER,
  BLACK_BIT,
  SCHEMA_VERSION,
  VIRTUAL_FAULTS,
  VIRTUAL_FAULT_LABELS_KO,
  describeError,
  type ArtifactKind,
  type PrinterProfileView,
  type PrintJobStatusResponse,
  type VirtualFaultKind,
  type VirtualFaultView,
} from '../api/contract';

const ARTIFACT_KINDS: ArtifactKind[] = ['content', 'paper', 'appearance'];

/** 인공 패턴. 실제 사진 합성은 09번에서 만든다. */
function buildDevPattern(width: number, height: number): Uint8Array {
  const pixels = new Uint8Array(width * height);
  const set = (x: number, y: number) => {
    if (x >= 0 && y >= 0 && x < width && y < height) pixels[y * width + x] = 1;
  };

  for (let x = 0; x < width; x += 1) {
    for (let t = 0; t < 3; t += 1) {
      set(x, t);
      set(x, height - 1 - t);
    }
  }
  for (let y = 0; y < height; y += 1) {
    for (let t = 0; t < 3; t += 1) {
      set(t, y);
      set(width - 1 - t, y);
    }
  }
  for (let y = 40; y < height - 40; y += 1) {
    for (let x = 40; x < width - 40; x += 1) {
      if ((x >> 3) % 2 === (y >> 3) % 2) set(x, y);
    }
  }

  return pixels;
}

/**
 * 05번의 개발 확인 화면. 인공 비트맵 제출 → 상태 조회 → 인증 fetch로 받은 Blob PNG 표시까지
 * 실제 API로 연결한다. **제품 UI 디자인이 아니다**(07번에서 만든다).
 */
export default function DevPrintCheck({
  profile,
  disabled = false,
  onFaultChange,
}: {
  profile: PrinterProfileView;
  /** 제품 흐름의 출력이 진행 중이면 이 패널의 조작도 막는다. */
  disabled?: boolean;
  /** 모의 오류를 바꿨을 때 부스 화면이 서버 값을 다시 읽게 한다. */
  onFaultChange?: () => void;
}) {
  const [status, setStatus] = useState<PrintJobStatusResponse | null>(null);
  const [images, setImages] = useState<Partial<Record<ArtifactKind, string>>>({});
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [fault, setFault] = useState<VirtualFaultView | null>(null);
  const jobIdRef = useRef<string | null>(null);

  const loadFault = useCallback(async () => {
    try {
      setFault((await api.virtualFault()).fault);
    } catch {
      setFault(null);
    }
  }, []);

  useEffect(() => {
    void (async () => {
      await loadFault();
    })();
  }, [loadFault]);

  const injectFault = useCallback(
    async (kind: VirtualFaultKind | null) => {
      setBusy(true);
      setNotice(null);
      try {
        const result = await api.setVirtualFault({
          schemaVersion: SCHEMA_VERSION,
          fault: kind,
          delayMs: kind === 'delay' ? 2000 : 0,
          failAfterRows: kind === 'fail_after_rows' ? 120 : null,
        });
        setFault(result.fault);
        onFaultChange?.();
      } catch (error) {
        setNotice(error instanceof ApiError ? describeError(error.detail) : '주입에 실패했어요.');
      } finally {
        setBusy(false);
      }
    },
    [onFaultChange],
  );

  const releaseImages = useCallback(() => {
    setImages((current) => {
      Object.values(current).forEach((url) => url && URL.revokeObjectURL(url));
      return {};
    });
  }, []);

  useEffect(() => releaseImages, [releaseImages]);

  const loadImages = useCallback(
    async (clientJobId: string) => {
      const loaded: Partial<Record<ArtifactKind, string>> = {};
      for (const kind of ARTIFACT_KINDS) {
        loaded[kind] = await fetchArtifactBlobUrl(clientJobId, kind);
      }
      setImages(loaded);
    },
    [],
  );

  const submit = useCallback(async () => {
    setBusy(true);
    setNotice(null);
    releaseImages();

    try {
      const width = profile.paper.contentWidthDots;
      const height = 480;
      const bitmap = packMonoBitmap(buildDevPattern(width, height), width, height);

      // 클릭 한 번에 UUID 하나. 통신이 끊겨도 같은 ID로 조회한다.
      const clientJobId = crypto.randomUUID();
      jobIdRef.current = clientJobId;

      let current = await api.submitPrintJob({
        schemaVersion: SCHEMA_VERSION,
        clientJobId,
        profileId: profile.profileId,
        profileRevision: profile.revision,
        bitmap: {
          widthDots: bitmap.widthDots,
          heightDots: bitmap.heightDots,
          strideBytes: bitmap.strideBytes,
          bitOrder: BIT_ORDER,
          blackBit: BLACK_BIT,
          dataBase64: toBase64(bitmap.data),
        },
      });
      setStatus(current);

      // 약 1초 간격으로 상태를 조회한다.
      for (let attempt = 0; attempt < 30 && current.queueBusy; attempt += 1) {
        await new Promise((resolve) => setTimeout(resolve, 400));
        current = await api.printJob(clientJobId);
        setStatus(current);
      }

      await loadFault();

      if (current.state === 'rendered') {
        await loadImages(clientJobId);
        setNotice('서버가 만든 PNG 세 장을 받았어요.');
      } else if (current.failure) {
        setNotice(describeError(current.failure));
      } else {
        setNotice('아직 끝나지 않았어요. 잠시 뒤 다시 확인해 주세요.');
      }
    } catch (error) {
      setNotice(error instanceof ApiError ? describeError(error.detail) : '요청에 실패했어요.');
    } finally {
      setBusy(false);
    }
  }, [loadFault, loadImages, profile, releaseImages]);

  const clear = useCallback(async () => {
    setBusy(true);

    // 서버 정리 성공 여부와 상관없이 **화면 사진부터** 없앤다.
    // 서버 결과가 남아 있으면 TTL이 보조 수단이며, 다음 사용자 세션은
    // remainingJobs === 0 이거나 hostInstanceId가 바뀐 뒤에만 시작한다(10번에서 화면 흐름에 반영).
    releaseImages();
    setStatus(null);

    try {
      const result = await api.clearSessionArtifacts();
      setNotice(
        result.remainingJobs === 0
          ? `서버 결과 ${result.clearedJobs}건을 정리했어요. 남은 결과 없음.`
          : `서버에 결과 ${result.remainingJobs}건이 남아 있어요. 다음 사용자를 시작하면 안 돼요.`,
      );
    } catch (error) {
      setNotice(
        `${error instanceof ApiError ? describeError(error.detail) : '정리에 실패했어요.'} 화면 사진은 지웠지만 서버 결과는 남아 있을 수 있어요.`,
      );
    } finally {
      setBusy(false);
    }
  }, [releaseImages]);

  return (
    <section className="panel">
      <h2 className="panel__title">개발 확인: 가상 출력</h2>
      <p>인공 패턴을 실제 API로 보내고 서버가 만든 PNG를 받아 옵니다. 제품 화면은 07번에서 만듭니다.</p>

      <p>
        {/* 인공 패턴 제출은 제품과 무관한 작업을 서버에 만들고 그동안 부스 출력이 거절된다.
            운영 번들에서는 접지 않으면 실수로 누를 수 있으므로 개발 실행에서만 노출한다. */}
        {import.meta.env.DEV && (
          <>
            <button type="button" onClick={() => void submit()} disabled={busy || disabled}>
              인공 비트맵 출력
            </button>{' '}
          </>
        )}
        <button type="button" onClick={() => void clear()} disabled={busy || disabled}>
          서버 결과 정리
        </button>
      </p>

      {notice && <p role="status">{notice}</p>}

      <p>
        <strong>가상 장치</strong> · 주입된 모의 오류:{' '}
        {fault ? `${VIRTUAL_FAULT_LABELS_KO[fault.kind]} (다음 작업 1회)` : '없음'}
      </p>
      <p>
        <select
          value={fault?.kind ?? ''}
          disabled={busy || disabled}
          onChange={(event) =>
            void injectFault(event.target.value === '' ? null : (event.target.value as VirtualFaultKind))
          }
        >
          <option value="">모의 오류 없음</option>
          {VIRTUAL_FAULTS.map((kind) => (
            <option key={kind} value={kind}>
              {VIRTUAL_FAULT_LABELS_KO[kind]}
            </option>
          ))}
        </select>{' '}
        <small>실제 장치 상태가 아니라 개발용 모의 오류입니다.</small>
      </p>

      {status && (
        <ul>
          <li>상태: {status.state}</li>
          <li>작업 번호: {status.clientJobId}</li>
          <li>실물 출력: {String(status.isPhysical)} · 큐 사용 중: {String(status.queueBusy)}</li>
          {status.layout && (
            <li>
              지면 {status.layout.paperWidthDots}×{status.layout.paperHeightDots}dot, 내용 원점 (
              {status.layout.contentXDots}, {status.layout.contentYDots})
            </li>
          )}
          {status.simulation && (
            <li>
              외형 revision {status.simulation.appearanceRevision} · 가상 출력
              {status.simulation.injectedFault && ` · 주입된 모의 오류: ${status.simulation.injectedFault}`}
            </li>
          )}
          {status.failure && <li>실패: {describeError(status.failure)}</li>}
        </ul>
      )}

      {/* 위 인공 제출로만 채워지는 확인용 이미지다. 운영 번들에서는 항상 비어 있으므로 함께 접는다. */}
      {import.meta.env.DEV && (
      <div style={{ display: 'flex', gap: '1rem', flexWrap: 'wrap' }}>
        {ARTIFACT_KINDS.map((kind) => (
          <figure key={kind} style={{ margin: 0 }}>
            <figcaption>{kind}</figcaption>
            {images[kind] ? (
              <img
                src={images[kind]}
                alt={`서버가 만든 ${kind} 이미지`}
                style={{ width: 200, imageRendering: 'pixelated', background: '#8a8a8f' }}
              />
            ) : (
              <p style={{ width: 200, color: '#666' }}>없음</p>
            )}
          </figure>
        ))}
      </div>
      )}
    </section>
  );
}
