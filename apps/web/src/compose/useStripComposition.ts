// 순수 합성 큐(composeQueue)를 실제 타이머·Worker·blob URL에 연결하는 얇은 층.
// 늦게 끝난 합성은 최신 편집 결과를 덮지 않고, 쓰지 않는 미리보기 URL은 바로 해제한다.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ImagingError, type DitherMethod, type FrameStyle } from '@ysfourcut/imaging';
import {
  COMPOSE_DEBOUNCE_MS,
  cancelCompose,
  composeSettled,
  createComposeQueue,
  debounceElapsed,
  requestCompose,
  type ComposeQueueResult,
  type ComposeQueueState,
} from './composeQueue';
import { PhotoWorkerClient } from './photoWorkerClient';
import { composeStrip, type ComposedStrip, type StripComposeRequest } from './stripComposer';

type QueueInput = Omit<StripComposeRequest, 'revision'>;

export type StripCompositionError = { code: string; message: string };

export type UseStripCompositionInput = {
  /** 원본 사진 URL 네 개. 없으면 합성하지 않는다. */
  photoUrls: readonly (string | null)[];
  frame: FrameStyle;
  brightness: number;
  contrast: number;
  caption: string;
  dither: DitherMethod;
  sessionDate: string;
  /** 프로필에서 읽은 실제 인쇄 가능 폭. null이면 합성하지 않는다. */
  contentWidthDots: number | null;
  limits: { maxHeightDots: number; maxDecodedBytes: number } | null;
  /** 프로필 revision. 바뀌면 원본에서 다시 합성한다. */
  profileRevision: string | null;
};

function describe(error: unknown): StripCompositionError {
  if (error instanceof ImagingError) return { code: error.code, message: error.message };
  if (error instanceof Error && 'code' in error) {
    return { code: String((error as { code: unknown }).code), message: error.message };
  }
  return { code: 'COMPOSE_FAILED', message: '네컷을 만들지 못했어요. 다시 시도해 주세요.' };
}

export function useStripComposition(input: UseStripCompositionInput) {
  const [composition, setComposition] = useState<ComposedStrip | null>(null);
  const [error, setError] = useState<StripCompositionError | null>(null);
  const [busy, setBusy] = useState(false);

  const queueRef = useRef<ComposeQueueState<QueueInput>>(createComposeQueue<QueueInput>());
  const workerRef = useRef<PhotoWorkerClient | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const doneRef = useRef(new Map<number, ComposedStrip>());
  const appliedRef = useRef<ComposedStrip | null>(null);
  const mountedRef = useRef(true);
  // 타이머·비동기 콜백이 자기 자신을 부르기 위한 통로(useCaptureSession과 같은 방식).
  const dispatchRef = useRef<(result: ComposeQueueResult<QueueInput>) => void>(() => undefined);

  const releaseDone = useCallback((revision: number) => {
    const strip = doneRef.current.get(revision);
    if (!strip) return;
    URL.revokeObjectURL(strip.previewUrl);
    doneRef.current.delete(revision);
  }, []);

  /** 화면에 올려 둔 합성 결과를 버리고 blob URL을 해제한다. */
  const clearApplied = useCallback(() => {
    if (appliedRef.current) {
      URL.revokeObjectURL(appliedRef.current.previewUrl);
      appliedRef.current = null;
    }
    if (mountedRef.current) {
      setComposition(null);
      setError(null);
    }
  }, []);

  const dispatch = useCallback(
    (result: ComposeQueueResult<QueueInput>) => {
      queueRef.current = result.state;
      if (mountedRef.current) {
        setBusy(result.state.running !== null || result.state.pending !== null);
      }

      for (const effect of result.effects) {
        switch (effect.type) {
          case 'schedule-debounce': {
            if (timerRef.current) clearTimeout(timerRef.current);
            const revision = effect.revision;
            timerRef.current = setTimeout(() => {
              timerRef.current = null;
              if (!mountedRef.current) return;
              dispatchRef.current(debounceElapsed(queueRef.current, revision));
            }, COMPOSE_DEBOUNCE_MS);
            break;
          }

          case 'start': {
            const { revision, input: job } = effect;
            const worker = (workerRef.current ??= new PhotoWorkerClient());

            void composeStrip({ ...job, revision }, worker)
              .then((strip) => {
                doneRef.current.set(revision, strip);
                if (!mountedRef.current) {
                  releaseDone(revision);
                  return;
                }
                dispatchRef.current(composeSettled(queueRef.current, revision, 'ok'));
              })
              .catch((cause: unknown) => {
                worker.forget(revision);
                if (!mountedRef.current) return;
                // 이미 취소·교체된 요청의 실패는 화면에 현재 상태처럼 보이면 안 된다.
                if (queueRef.current.running?.revision === revision) {
                  setError(describe(cause));
                }
                dispatchRef.current(composeSettled(queueRef.current, revision, 'failed'));
              });
            break;
          }

          case 'apply': {
            const strip = doneRef.current.get(effect.revision);
            doneRef.current.delete(effect.revision);
            if (!strip) break;

            if (appliedRef.current) URL.revokeObjectURL(appliedRef.current.previewUrl);
            appliedRef.current = strip;
            if (mountedRef.current) {
              setComposition(strip);
              setError(null);
            }
            break;
          }

          case 'discard': {
            releaseDone(effect.revision);
            break;
          }
        }
      }
    },
    [releaseDone],
  );

  useEffect(() => {
    dispatchRef.current = dispatch;
  }, [dispatch]);

  useEffect(() => {
    mountedRef.current = true;
    const done = doneRef.current;

    return () => {
      mountedRef.current = false;
      if (timerRef.current) clearTimeout(timerRef.current);
      workerRef.current?.terminate();
      workerRef.current = null;
      done.forEach((strip) => URL.revokeObjectURL(strip.previewUrl));
      done.clear();
      if (appliedRef.current) URL.revokeObjectURL(appliedRef.current.previewUrl);
      appliedRef.current = null;
    };
  }, []);

  const contentWidthDots = input.contentWidthDots;
  const limits = input.limits;
  const photoKey = input.photoUrls.join('|');

  // 원본·설정·프로필 중 하나라도 바뀌면 원본에서 다시 합성한다.
  const job = useMemo<QueueInput | null>(() => {
    if (contentWidthDots === null || limits === null) return null;
    if (input.photoUrls.length === 0 || input.photoUrls.some((url) => !url)) return null;

    return {
      photoUrls: input.photoUrls,
      frame: input.frame,
      brightness: input.brightness,
      contrast: input.contrast,
      caption: input.caption,
      dither: input.dither,
      sessionDate: input.sessionDate,
      contentWidthDots,
      limits,
    };
    // photoKey는 URL 배열의 동일성 판단에 쓴다.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [
    photoKey,
    input.frame,
    input.brightness,
    input.contrast,
    input.caption,
    input.dither,
    input.sessionDate,
    contentWidthDots,
    limits?.maxHeightDots,
    limits?.maxDecodedBytes,
    input.profileRevision,
  ]);

  useEffect(() => {
    if (job === null) {
      dispatch(cancelCompose(queueRef.current));
      // 원본이 사라지면(다음 촬영·유휴 정리·전체 다시 찍기) 합성 결과와 그 URL도 함께 버린다.
      // 남겨 두면 다음 사용자 화면에 이전 사진이 다시 보인다.
      clearApplied();
      return;
    }

    dispatch(requestCompose(queueRef.current, job));
  }, [job, dispatch, clearApplied]);

  return useMemo(
    () => ({ composition, isComposing: busy, error }),
    [composition, busy, error],
  );
}
