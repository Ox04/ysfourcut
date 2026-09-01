import { useCallback, useEffect, useRef, useState } from 'react';
import {
  acceptFrame,
  cancelSession,
  createCaptureState,
  retakeAll as retakeAllPure,
  retakeCut as retakeCutPure,
  startSession as startSessionPure,
  tick as tickPure,
  toCutSlots,
  type CaptureEffect,
  type CaptureResult,
  type CaptureState,
} from './captureSession';
import { CUT_ASPECT, captureTargetSize, coverCropRect, isFrameReady } from './frameCapture';

// 순수 상태 머신(captureSession)을 실제 타이머·비디오·캔버스에 연결하는 얇은 층.
// 사진은 blob URL로 브라우저 메모리에만 두고, 쓰지 않는 URL은 즉시 해제한다.

const FLASH_MS = 380;

/** 프레임이 준비되기를 기다리는 최대 시간. 넘으면 실패로 확정하고 무한 재시도를 끝낸다. */
const FRAME_WAIT_MS = 2500;

export type UseCaptureSessionOptions = {
  /** 미리보기와 같은 좌우 반전 여부. 캡처에도 똑같이 적용한다. */
  mirrored: boolean;
};

export function useCaptureSession(
  videoRef: React.RefObject<HTMLVideoElement | null>,
  { mirrored }: UseCaptureSessionOptions,
) {
  const [state, setState] = useState<CaptureState>(createCaptureState);
  const [isFlashing, setFlashing] = useState(false);

  // 타이머·비동기 콜백이 읽을 최신 값. 렌더 중에는 건드리지 않고 effect에서만 갱신한다.
  const stateRef = useRef(state);
  const mirroredRef = useRef(mirrored);
  const flashTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // 다음 프레임 재시도·연속 캡처에서 자기 자신을 부르기 위한 통로.
  const captureFrameRef = useRef<(sessionId: number, captureToken: number, startedAtMs?: number) => void>(
    () => undefined,
  );
  // 언마운트 뒤에는 어떤 비동기 콜백도 상태를 바꾸거나 재시도를 예약하지 않는다.
  const mountedRef = useRef(true);
  const [captureError, setCaptureError] = useState<string | null>(null);

  useEffect(() => {
    stateRef.current = state;
  }, [state]);

  useEffect(() => {
    mirroredRef.current = mirrored;
  }, [mirrored]);

  /** 이 캡처 요청이 아직 유효한지. 언마운트·취소·새 세션이면 무효다. */
  const isCurrent = useCallback((sessionId: number, captureToken: number) => {
    const current = stateRef.current;
    return (
      mountedRef.current && current.sessionId === sessionId && current.captureToken === captureToken
    );
  }, []);

  /** 지금 영상 프레임을 캡처한다. 준비 전이면 다음 프레임에서 다시 시도한다. */
  const captureFrame = useCallback(
    (sessionId: number, captureToken: number, startedAtMs = Date.now()) => {
      if (!isCurrent(sessionId, captureToken)) return;

      const video = videoRef.current;

      // 준비되지 않은 검은 프레임은 확정하지 않는다. 다만 무한히 기다리지도 않는다.
      if (!video || !isFrameReady(video)) {
        if (Date.now() - startedAtMs > FRAME_WAIT_MS) {
          setCaptureError('카메라 영상을 받지 못했어요. 다시 시도해 주세요.');
          const cancelled = cancelSession(stateRef.current);
          stateRef.current = cancelled.state;
          setState(cancelled.state);
          cancelled.effects.forEach(
            (effect) => effect.type === 'release-url' && URL.revokeObjectURL(effect.url),
          );
          return;
        }

        requestAnimationFrame(() => {
          if (isCurrent(sessionId, captureToken)) {
            captureFrameRef.current(sessionId, captureToken, startedAtMs);
          }
        });
        return;
      }

      const crop = coverCropRect({ width: video.videoWidth, height: video.videoHeight }, CUT_ASPECT);
      const size = captureTargetSize(crop);

      const canvas = document.createElement('canvas');
      canvas.width = size.width;
      canvas.height = size.height;

      const context = canvas.getContext('2d');
      if (!context) {
        setCaptureError('이 브라우저에서 사진을 만들지 못했어요.');
        return;
      }

      if (mirroredRef.current) {
        // 미리보기와 같은 좌우 반전. 글자·프레임은 09번 합성에서 반전 없이 얹는다.
        context.translate(size.width, 0);
        context.scale(-1, 1);
      }

      context.drawImage(video, crop.sx, crop.sy, crop.sWidth, crop.sHeight, 0, 0, size.width, size.height);

      canvas.toBlob((blob) => {
        if (!blob) {
          if (mountedRef.current) setCaptureError('사진을 저장하지 못했어요. 다시 시도해 주세요.');
          return;
        }

        const url = URL.createObjectURL(blob);

        // 언마운트된 뒤 도착한 프레임은 슬롯에 넣지 않고 즉시 해제한다.
        if (!mountedRef.current) {
          URL.revokeObjectURL(url);
          return;
        }

        // 세션·셔터 토큰이 다르면 순수 머신이 이 URL을 버리라고 알려 준다.
        const result = acceptFrame(stateRef.current, {
          sessionId,
          captureToken,
          url,
          nowMs: Date.now(),
        });
        stateRef.current = result.state;
        setState(result.state);

        // 실제로 슬롯에 들어간 프레임에만 셔터 연출을 보여 준다.
        const accepted = !result.effects.some(
          (effect) => effect.type === 'release-url' && effect.url === url,
        );
        if (accepted) {
          setFlashing(true);
          if (flashTimerRef.current) clearTimeout(flashTimerRef.current);
          flashTimerRef.current = setTimeout(() => setFlashing(false), FLASH_MS);
        }

        for (const effect of result.effects) {
          if (effect.type === 'release-url') {
            URL.revokeObjectURL(effect.url);
          } else {
            captureFrameRef.current(effect.sessionId, effect.captureToken);
          }
        }
      }, 'image/png');
    },
    [isCurrent, videoRef],
  );

  useEffect(() => {
    captureFrameRef.current = captureFrame;
  }, [captureFrame]);

  /** 순수 결과를 적용하고 부수 효과(URL 해제·캡처 요청)를 실행한다. */
  const apply = useCallback(
    (result: CaptureResult) => {
      stateRef.current = result.state;
      setState(result.state);
      for (const effect of result.effects as CaptureEffect[]) {
        if (effect.type === 'release-url') {
          URL.revokeObjectURL(effect.url);
        } else {
          captureFrame(effect.sessionId, effect.captureToken);
        }
      }
    },
    [captureFrame],
  );

  // 1초 카운트다운. countdown 상태이고 같은 셔터일 때만 돈다.
  useEffect(() => {
    if (state.status !== 'countdown') return;

    const timer = setInterval(() => apply(tickPure(stateRef.current)), 1000);
    return () => clearInterval(timer);
  }, [state.status, state.captureToken, apply]);

  // 화면을 떠날 때 남은 사진 URL과 타이머를 정리하고, 진행 중인 비동기 경로를 무효화한다.
  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      if (flashTimerRef.current) clearTimeout(flashTimerRef.current);
      stateRef.current.slots.forEach((slot) => slot && URL.revokeObjectURL(slot.url));
    };
  }, []);

  const start = useCallback(() => {
    setCaptureError(null);
    apply(startSessionPure(stateRef.current));
  }, [apply]);
  const cancel = useCallback(() => {
    setCaptureError(null);
    apply(cancelSession(stateRef.current));
  }, [apply]);
  const retakeAll = useCallback(() => {
    setCaptureError(null);
    apply(retakeAllPure(stateRef.current));
  }, [apply]);
  const retakeCut = useCallback(
    (cut: number) => {
      setCaptureError(null);
      apply(retakeCutPure(stateRef.current, cut));
    },
    [apply],
  );

  return {
    state,
    slots: toCutSlots(state),
    isFlashing,
    captureError,
    start,
    cancel,
    retakeAll,
    retakeCut,
  };
}
