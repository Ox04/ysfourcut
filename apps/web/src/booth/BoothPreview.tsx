import { useCallback, useEffect, useState } from 'react';
import { ApiError, api, fetchArtifactBlobUrl } from '../api/client';
import {
  BIT_ORDER,
  BLACK_BIT,
  SCHEMA_VERSION,
  describeError,
  type PrinterProfileView,
} from '../api/contract';
import { DEFAULT_DITHER_METHOD, packMonoBitmap, toBase64 } from '@ysfourcut/imaging';
import { BoothShell, type BoothStep } from './BoothShell';
import { CaptureScreen } from './CaptureScreen';
import { EditScreen } from './EditScreen';
import { ResultScreen } from './ResultScreen';
import { StartScreen } from './StartScreen';
import { useStripComposition } from '../compose/useStripComposition';
import { LONG_CAPTION_SAMPLE, LONG_FAILURE_SAMPLE, sampleSlots } from './fixtures';
import type { DeviceReadiness, EditSettings, ResultView } from './types';

// 07번의 상태 미리보기 드라이버. **개발 전용**이며 실제 촬영·출력 흐름은 08~10번이 연결한다.
// 결과 화면의 rendered 상태만은 fixture로 흉내 내지 않고 실제 서버 PNG를 받아야 볼 수 있다.

const SCENES = [
  ['start', '시작'],
  ['start-noprint', '시작(출력 불가)'],
  ['capture-1', '촬영 1/4 준비'],
  ['capture-3', '촬영 3/4 카운트다운'],
  ['capture-flash', '촬영 셔터 직후'],
  ['edit', '편집'],
  ['edit-busy', '편집(전송 중)'],
  ['printing', '결과: 만드는 중'],
  ['result-real', '결과: 실제 서버 PNG'],
  ['result-failed', '결과: 실패'],
] as const;

export type SceneId = (typeof SCENES)[number][0];

function sceneFromHash(): SceneId {
  const match = /scene=([a-z0-9-]+)/.exec(window.location.hash);
  const found = SCENES.find(([id]) => id === match?.[1]);
  return found ? found[0] : 'start';
}

/** 로컬(사용자 시간대) 기준 YYYY-MM-DD. 실제 세션 날짜 고정은 10번 상태 머신이 담당한다. */
function localDateString(): string {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

const DEFAULT_SETTINGS: EditSettings = {
  frame: 'basic',
  brightness: 10,
  contrast: 0,
  caption: LONG_CAPTION_SAMPLE,
  dither: DEFAULT_DITHER_METHOD,
};

/** 07 미리보기에서도 실제 합성 경로를 쓴다. 사진은 인공 샘플이다. */
const PREVIEW_SLOTS = sampleSlots(4);
const PREVIEW_PHOTO_URLS = PREVIEW_SLOTS.map((slot) => slot.imageUrl);

export function BoothPreview({ profile }: { profile: PrinterProfileView }) {
  const [scene, setScene] = useState<SceneId>(sceneFromHash);
  const [settings, setSettings] = useState<EditSettings>(DEFAULT_SETTINGS);
  const [realResult, setRealResult] = useState<ResultView | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const previewDate = localDateString();

  // 07 미리보기도 실제 합성 경로를 그대로 쓴다(인공 샘플 사진 + 실제 프로필 폭).
  const strip = useStripComposition({
    photoUrls: PREVIEW_PHOTO_URLS,
    frame: settings.frame,
    brightness: settings.brightness,
    contrast: settings.contrast,
    caption: settings.caption,
    dither: settings.dither,
    sessionDate: previewDate,
    contentWidthDots: profile.paper.contentWidthDots,
    limits: {
      maxHeightDots: profile.limits.maxContentHeightDots,
      maxDecodedBytes: profile.limits.maxDecodedBytes,
    },
    profileRevision: profile.revision,
  });

  useEffect(() => {
    const onHashChange = () => setScene(sceneFromHash());
    window.addEventListener('hashchange', onHashChange);
    return () => window.removeEventListener('hashchange', onHashChange);
  }, []);

  useEffect(
    () => () => {
      if (realResult) {
        Object.values(realResult.images).forEach((url) => url && URL.revokeObjectURL(url));
      }
    },
    [realResult],
  );

  /** 실제 API로 인공 비트맵을 출력해 결과 화면에 서버 PNG를 채운다. */
  const loadRealResult = useCallback(async () => {
    setLoading(true);
    setNotice(null);
    try {
      const width = profile.paper.contentWidthDots;
      const height = 720;
      const pixels = new Uint8Array(width * height);
      for (let y = 0; y < height; y += 1) {
        for (let x = 0; x < width; x += 1) {
          const border = y < 4 || y >= height - 4 || x < 4 || x >= width - 4;
          const grid = y > 60 && y < height - 80 && ((x >> 3) + (y >> 3)) % 2 === 0;
          if (border || grid) pixels[y * width + x] = 1;
        }
      }
      const bitmap = packMonoBitmap(pixels, width, height);
      const clientJobId = crypto.randomUUID();

      let status = await api.submitPrintJob({
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

      for (let i = 0; i < 40 && status.queueBusy; i += 1) {
        await new Promise((resolve) => setTimeout(resolve, 350));
        status = await api.printJob(clientJobId);
      }

      if (status.state !== 'rendered') {
        setNotice(status.failure ? describeError(status.failure) : '서버 결과를 받지 못했어요.');
        return;
      }

      const [appearance, paper, content] = await Promise.all([
        fetchArtifactBlobUrl(clientJobId, 'appearance'),
        fetchArtifactBlobUrl(clientJobId, 'paper'),
        fetchArtifactBlobUrl(clientJobId, 'content'),
      ]);

      setRealResult({ state: 'rendered', images: { appearance, paper, content }, layout: status.layout });
      setScene('result-real');
    } catch (error) {
      setNotice(error instanceof ApiError ? describeError(error.detail) : '요청에 실패했어요.');
    } finally {
      setLoading(false);
    }
  }, [profile]);

  const device: DeviceReadiness = {
    cameraReady: scene !== 'start-noprint',
    printReady: scene !== 'start-noprint',
    printerMode: 'virtual',
    printBlockedReason:
      scene === 'start-noprint' ? '로컬 프로그램과 연결되지 않았어요 (촬영·저장은 가능)' : undefined,
  };

  const step: BoothStep = scene.startsWith('capture')
    ? 'capture'
    : scene.startsWith('edit')
      ? 'edit'
      : scene.startsWith('result') || scene === 'printing'
        ? 'result'
        : 'start';

  const noop = () => undefined;

  return (
    <div className="booth-preview">
      <div className="booth-preview__bar" role="group" aria-label="개발 미리보기 장면 선택">
        <strong>화면 미리보기</strong>
        <select value={scene} onChange={(event) => setScene(event.target.value as SceneId)}>
          {SCENES.map(([id, label]) => (
            <option key={id} value={id}>
              {label}
            </option>
          ))}
        </select>
        <button type="button" disabled={loading} onClick={() => void loadRealResult()}>
          {loading ? '서버 결과 받는 중…' : '실제 서버 PNG 불러오기'}
        </button>
        <span className="booth-preview__note">개발 전용 · 인공 샘플이며 실제 촬영이 아님</span>
        {notice && <span role="status">{notice}</span>}
      </div>

      <BoothShell step={step} device={device}>
        {step === 'start' && <StartScreen device={device} onStart={noop} />}

        {step === 'capture' && (
          <CaptureScreen
            preview={<div className="capture__preview-fixture">카메라 미리보기 자리 (08번 연결)</div>}
            slots={sampleSlots(scene === 'capture-1' ? 0 : 2)}
            currentCut={scene === 'capture-1' ? 1 : 3}
            countdownSeconds={scene === 'capture-3' ? 2 : null}
            isFlashing={scene === 'capture-flash'}
            onCancel={noop}
          />
        )}

        {step === 'edit' && (
          <EditScreen
            slots={PREVIEW_SLOTS}
            settings={settings}
            sessionDate={previewDate}
            preview={
              strip.composition
                ? { url: strip.composition.previewUrl, layout: strip.composition.layout }
                : null
            }
            isComposing={strip.isComposing}
            previewMessage={strip.error?.message}
            printReady
            busy={scene === 'edit-busy'}
            onChange={setSettings}
            onRetakeCut={noop}
            onRetakeAll={noop}
            onSavePng={noop}
            onPrint={noop}
          />
        )}

        {scene === 'printing' && (
          <ResultScreen
            result={{ state: 'rendering', images: {}, layout: null }}
            onSave={noop}
            onReprint={noop}
            onNextSession={noop}
          />
        )}

        {scene === 'result-real' &&
          (realResult ? (
            <ResultScreen result={realResult} onSave={noop} onReprint={noop} onNextSession={noop} />
          ) : (
            <p className="booth-preview__empty" role="status">
              위의 “실제 서버 PNG 불러오기”를 누르면 진짜 서버 결과로 이 화면을 채워요. 가짜 성공
              이미지는 쓰지 않아요.
            </p>
          ))}

        {scene === 'result-failed' && (
          <ResultScreen
            result={{
              state: 'virtual_failed',
              images: {},
              layout: null,
              failureMessage: LONG_FAILURE_SAMPLE,
            }}
            onSave={noop}
            onReprint={noop}
            onNextSession={noop}
          />
        )}
      </BoothShell>
    </div>
  );
}
