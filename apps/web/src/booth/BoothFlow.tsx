import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { DEFAULT_DITHER_METHOD } from '@ysfourcut/imaging';
import { ApiError, api } from '../api/client';
import { describeError } from '../api/contract';
import { CameraPreview } from '../camera/CameraPreview';
import { useCamera } from '../camera/useCamera';
import { useCaptureSession } from '../camera/useCaptureSession';
import { toPrintJobBitmap } from '../compose/printJobBitmap';
import { useStripComposition } from '../compose/useStripComposition';
import { usePrintJob } from '../print/usePrintJob';
import { decideCleanup } from '../print/sessionCleanup';
import { Button } from '../ui/Button';
import { TicketPanel } from '../ui/parts';
import { BoothShell, type BoothStep } from './BoothShell';
import { CaptureScreen } from './CaptureScreen';
import { EditScreen } from './EditScreen';
import { ResultScreen } from './ResultScreen';
import { StartScreen } from './StartScreen';
import { useIdleTimeout, resolveIdleTimeoutMs } from './useIdleTimeout';
import { usePrinterProfile } from './usePrinterProfile';
import { useVirtualFault } from './useVirtualFault';
import type { DeviceReadiness, EditSettings, ResultView } from './types';

// 시작 → 카메라 → 4컷 → 편집 → 가상 출력 → 결과 → 다음 촬영을 한 화면 흐름으로 잇는다.
// 출력은 09번이 만든 최종 비트맵 하나를 그대로 보내며 제출 시점에 다시 합성하지 않는다.
// 성공 표시는 서버가 rendered를 확정하고 세 PNG를 모두 받은 뒤에만 만든다(print/printJobMachine).

const DEFAULT_SETTINGS: EditSettings = {
  frame: 'basic',
  brightness: 0,
  contrast: 0,
  caption: '',
  dither: DEFAULT_DITHER_METHOD,
};

/** 로컬(사용자 시간대) 기준 YYYY-MM-DD. 세션 시작 시 고정한다. */
function localDateString(): string {
  const now = new Date();
  const pad = (value: number) => String(value).padStart(2, '0');
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
}

type CleanupState = {
  status: 'idle' | 'running' | 'blocked';
  message: string | null;
};

export function BoothFlow({
  hostInstanceId = null,
  deviceRevision = 0,
  onPrintBusyChange,
}: {
  /** 이번 세션을 시작할 때 본 호스트 인스턴스. 정리 실패 시 재시작 여부를 판단한다. */
  hostInstanceId?: string | null;
  /** 장치 패널에서 프로필·모의 오류를 바꾸면 증가한다. 서버 값을 다시 읽는다. */
  deviceRevision?: number;
  /** 출력 중에는 바깥 장치 패널의 설정 변경도 막는다. */
  onPrintBusyChange?: (busy: boolean) => void;
}) {
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const camera = useCamera();

  // 셀카처럼 좌우를 뒤집어 보여 준다. 캡처도 같은 규칙을 쓴다.
  const mirrored = true;
  const capture = useCaptureSession(videoRef, { mirrored });
  const printer = usePrinterProfile(deviceRevision);
  const virtualFault = useVirtualFault(deviceRevision);
  const print = usePrintJob();

  const [settings, setSettings] = useState<EditSettings>(DEFAULT_SETTINGS);
  const [sessionDate, setSessionDate] = useState(localDateString);
  const [cleanup, setCleanup] = useState<CleanupState>({ status: 'idle', message: null });
  const [reprintArmed, setReprintArmed] = useState(false);

  const filled = capture.slots.filter((slot) => slot.imageUrl !== null).length;
  const isCapturing = capture.state.status === 'countdown' || capture.state.status === 'capturing';
  const isComplete = capture.state.status === 'complete';
  const printPhase = print.state.phase;
  const showResult = printPhase !== 'idle';

  const step: BoothStep = showResult
    ? 'result'
    : isCapturing
      ? 'capture'
      : isComplete
        ? 'edit'
        : 'start';

  const photoUrls = useMemo(() => capture.slots.map((slot) => slot.imageUrl), [capture.slots]);

  // 프로필이 바뀌면 원본에서 다시 합성한다(합성 결과를 늘리거나 줄이지 않는다).
  const strip = useStripComposition({
    photoUrls: isComplete ? photoUrls : [],
    frame: settings.frame,
    brightness: settings.brightness,
    contrast: settings.contrast,
    caption: settings.caption,
    dither: settings.dither,
    sessionDate,
    contentWidthDots: printer.paper?.contentWidthDots ?? null,
    limits: printer.paper?.limits ?? null,
    profileRevision: printer.paper?.revision ?? null,
  });

  const device: DeviceReadiness = useMemo(
    () => ({
      cameraReady: camera.status === 'ready',
      printReady: printer.paper !== null,
      printerMode: printer.paper?.printerMode ?? 'virtual',
      injectedFault: virtualFault.fault?.kind ?? null,
    }),
    [camera.status, printer.paper, virtualFault.fault],
  );

  // 시작 화면에서 미리 카메라를 준비해 첫 컷이 검은 프레임이 되지 않게 한다.
  useEffect(() => {
    if (camera.status === 'idle') {
      void camera.start();
    }
  }, [camera]);

  // 출력 중에는 바깥(장치 패널)의 설정 변경도 잠근다.
  useEffect(() => {
    onPrintBusyChange?.(print.isPrinting);
  }, [print.isPrinting, onPrintBusyChange]);

  // 모의 오류는 작업 하나에만 적용되고 자동으로 풀린다. 끝나면 실제 값을 다시 읽는다.
  const faultRefresh = virtualFault.refresh;
  useEffect(() => {
    if (printPhase === 'rendered' || printPhase === 'failed') faultRefresh();
  }, [printPhase, faultRefresh]);

  const onStart = useCallback(() => {
    setCleanup({ status: 'idle', message: null });
    if (camera.status !== 'ready') {
      void camera.start();
      return;
    }
    setSessionDate(localDateString());
    capture.start();
  }, [camera, capture]);

  /** 최종 비트맵을 그대로 PNG로 내려받는다. 서버로 보내는 것과 같은 픽셀이다. */
  const download = useCallback((url: string, name: string) => {
    // 문서에 붙인 뒤 눌러야 브라우저가 확실히 저장을 시작한다.
    const link = document.createElement('a');
    link.href = url;
    link.download = name;
    link.rel = 'noopener';
    document.body.appendChild(link);
    link.click();
    link.remove();
  }, []);

  const onSavePng = useCallback(() => {
    if (!strip.composition) return;
    download(strip.composition.previewUrl, `ysfourcut-${sessionDate}.png`);
  }, [download, strip.composition, sessionDate]);

  /** 결과 화면 저장. 서버가 만든 실물 느낌 PNG를 사용자가 눌렀을 때만 내려받는다. */
  const onSaveResult = useCallback(() => {
    const url = print.state.images.appearance;
    if (!url) return;
    download(url, `ysfourcut-${sessionDate}-receipt.png`);
  }, [download, print.state.images.appearance, sessionDate]);

  /**
   * 출력 클릭. 이 시점에 비트맵·프로필 revision·UUID를 고정한다.
   * 재합성하지 않고 09번이 만든 `composition.bitmap`을 그대로 보낸다.
   */
  const submitPrint = useCallback(() => {
    const composition = strip.composition;
    const paper = printer.paper;
    if (!composition || !paper || strip.isComposing) return;

    setReprintArmed(false);
    print.start({
      clientJobId: crypto.randomUUID(),
      profileId: paper.profileId,
      profileRevision: paper.revision,
      bitmap: toPrintJobBitmap(composition.bitmap),
    });
  }, [print, printer.paper, strip.composition, strip.isComposing]);

  /**
   * 확정된 실패 뒤의 재시도, 또는 완료 뒤의 명시적 다시 출력.
   * 완료 뒤에는 중복 안내를 한 번 보여 주고 두 번째 누름에서만 **새 UUID**로 접수한다.
   */
  const onReprint = useCallback(() => {
    if (printPhase === 'rendered' && !reprintArmed) {
      setReprintArmed(true);
      return;
    }
    submitPrint();
  }, [printPhase, reprintArmed, submitPrint]);

  /**
   * 다음 촬영·유휴 정리. 서버 결과 정리와 브라우저 원본·합성·Blob URL 정리를 함께 한다.
   * 정리에 실패하면 화면 사진부터 지우되 다음 사용자를 시작하지 않는다.
   */
  const endSession = useCallback(async () => {
    if (print.isPrinting) return;

    setCleanup({ status: 'running', message: null });
    setReprintArmed(false);

    // ① 화면·브라우저부터 비운다. 원본 blob URL, 합성 결과, 서버 PNG Blob URL 모두.
    capture.cancel();
    print.reset();
    setSettings(DEFAULT_SETTINGS);

    // ② 서버 결과를 정리한다.
    let cleared: { clearedJobs: number; remainingJobs: number } | null = null;
    let clearErrorMessage: string | null = null;
    try {
      const response = await api.clearSessionArtifacts();
      cleared = { clearedJobs: response.clearedJobs, remainingJobs: response.remainingJobs };
    } catch (error) {
      clearErrorMessage = error instanceof ApiError ? describeError(error.detail) : '서버 결과를 정리하지 못했어요.';
    }

    // ③ 정리가 확인되지 않으면 호스트 재시작(캐시가 빈 새 인스턴스)인지 본다.
    let currentHostInstanceId: string | null = null;
    if (!cleared || cleared.remainingJobs !== 0) {
      try {
        currentHostInstanceId = (await api.health()).hostInstanceId;
      } catch {
        currentHostInstanceId = null;
      }
    }

    const decision = decideCleanup({
      clear: cleared,
      clearErrorMessage,
      sessionHostInstanceId: hostInstanceId,
      currentHostInstanceId,
    });

    setCleanup({
      status: decision.canStartNext ? 'idle' : 'blocked',
      message: decision.canStartNext ? null : decision.message,
    });
  }, [capture, hostInstanceId, print]);

  // 유휴 정리(기본 120초). 출력이 끝나지 않은 동안에는 켜지 않는다.
  const idleTimeoutMs = useMemo(
    () =>
      resolveIdleTimeoutMs(
        typeof window === 'undefined' ? '' : window.location.search,
        import.meta.env.DEV,
      ),
    [],
  );
  const idleArmed =
    (filled > 0 || printPhase === 'rendered' || printPhase === 'failed') &&
    !isCapturing &&
    !print.isPrinting &&
    cleanup.status === 'idle';

  useIdleTimeout(idleArmed, idleTimeoutMs, () => void endSession());

  const previewMessage = strip.error
    ? strip.error.message
    : printer.isLoading
      ? '프린터 설정을 읽는 중이에요.'
      : printer.paper === null
        ? (printer.error ?? '로컬 프로그램과 연결하면 인쇄 크기에 맞춰 만들어요.')
        : undefined;

  const printReady = printer.paper !== null && strip.composition !== null && !strip.isComposing;
  const printBlockedReason =
    printer.paper === null
      ? (printer.error ?? '로컬 프로그램과 연결해야 출력할 수 있어요.')
      : strip.isComposing
        ? '미리보기를 만드는 중이에요. 잠시만 기다려 주세요.'
        : (strip.error?.message ?? '아직 보낼 네컷이 준비되지 않았어요.');

  /** 서버가 알려 준 상태만 쓴다. 화면에서 성공을 만들어 내지 않는다. */
  const result: ResultView = useMemo(() => {
    const status = print.state.status;
    const state =
      printPhase === 'rendered'
        ? 'rendered'
        : printPhase === 'failed'
          ? // 서버 실패든 결과 이미지 수신 실패든 사용자에게는 확정된 실패다(성공을 표시하지 않는다).
            'virtual_failed'
          : printPhase === 'loading-images'
            ? 'rendering'
            : (status?.state ?? 'accepted');

    return {
      state,
      images: print.state.images,
      layout: status?.layout ?? null,
      failureMessage: print.state.failure ? describeError(print.state.failure) : undefined,
      isPartial: print.state.isPartial,
    };
  }, [print.state.failure, print.state.images, print.state.isPartial, print.state.status, printPhase]);

  const resultNotice = print.state.status?.simulation?.injectedFault
    ? `이 작업에는 모의 오류(${print.state.status.simulation.injectedFault})가 주입돼 있었어요. 실제 장치 상태가 아니에요.`
    : reprintArmed
      ? '이미 한 장을 만들었어요. 한 번 더 누르면 새 작업 번호로 다시 만들어요.'
      : null;

  return (
    <BoothShell step={step} device={device}>
      {cleanup.status !== 'idle' && (
        <TicketPanel
          className="booth-flow__cleanup"
          title={cleanup.status === 'blocked' ? '정리하지 못했어요' : '정리 중'}
        >
          <p role={cleanup.status === 'blocked' ? 'alert' : 'status'}>
            {cleanup.message ?? '이전 촬영을 정리하는 중이에요…'}
          </p>
          {cleanup.status === 'blocked' && (
            <Button variant="primary" onClick={() => void endSession()}>
              다시 정리하기
            </Button>
          )}
        </TicketPanel>
      )}

      {step === 'start' && cleanup.status === 'idle' && (
        <div className="booth-flow__start">
          <StartScreen
            device={{
              ...device,
              printBlockedReason:
                capture.captureError ??
                (camera.status === 'error' ? camera.error?.message : undefined) ??
                printer.error ??
                undefined,
            }}
            onStart={onStart}
          />
          {/* 카메라 준비 상태·오류는 시작 화면에서 미리 보여 준다. 스트림은 촬영 예열을 위해 유지한다. */}
          <figure className="booth-flow__camera-status">
            <div className="booth-flow__camera-frame">
              <CameraPreview
                videoRef={videoRef}
                stream={camera.stream}
                status={camera.status}
                error={camera.error}
                mirrored={mirrored}
                onRetry={() => void camera.start()}
              />
            </div>
            <figcaption className="booth-flow__camera-caption">카메라 확인용 미리보기</figcaption>
          </figure>
        </div>
      )}

      {step === 'capture' && (
        <CaptureScreen
          preview={
            <CameraPreview
              videoRef={videoRef}
              stream={camera.stream}
              status={camera.status}
              error={camera.error}
              mirrored={mirrored}
              onRetry={() => void camera.start()}
            />
          }
          slots={capture.slots}
          currentCut={capture.state.targetCut ?? Math.min(filled + 1, capture.slots.length)}
          countdownSeconds={capture.state.status === 'countdown' ? capture.state.secondsLeft : null}
          isFlashing={capture.isFlashing}
          captureError={capture.captureError}
          onCancel={capture.cancel}
        />
      )}

      {step === 'edit' && (
        <EditScreen
          slots={capture.slots}
          settings={settings}
          sessionDate={sessionDate}
          preview={
            strip.composition
              ? { url: strip.composition.previewUrl, layout: strip.composition.layout }
              : null
          }
          isComposing={strip.isComposing}
          previewMessage={previewMessage}
          printReady={printReady}
          printBlockedReason={printBlockedReason}
          onChange={setSettings}
          onRetakeCut={capture.retakeCut}
          onRetakeAll={capture.retakeAll}
          onSavePng={onSavePng}
          onPrint={submitPrint}
        />
      )}

      {step === 'result' && (
        <ResultScreen
          result={result}
          busy={print.isPrinting || cleanup.status === 'running'}
          notice={resultNotice}
          onSave={onSaveResult}
          onReprint={onReprint}
          onNextSession={() => void endSession()}
        />
      )}
    </BoothShell>
  );
}
