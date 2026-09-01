// 순수 상태 머신(printJobMachine)을 실제 API·타이머·Blob URL에 연결하는 얇은 층.
// 서버 PNG는 인증 fetch로 받아 Blob URL로만 표시하고, 쓰지 않는 URL은 즉시 해제한다.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError, api, fetchArtifactBlobUrl } from '../api/client';
import { SCHEMA_VERSION, type ArtifactKind, type PrintJobStatusResponse } from '../api/contract';
import {
  CLIENT_ERROR_MESSAGES_KO,
  POLL_INTERVAL_MS,
  artifactsSettled,
  createPrintJobState,
  isPrinting,
  pollSettled,
  resetPrintJob,
  startPrint,
  submitSettled,
  type CallOutcome,
  type PrintEffect,
  type PrintJobMachineState,
  type PrintJobResult,
  type PrintTicket,
} from './printJobMachine';

/** ApiError(서버가 구조화된 오류를 돌려줌)와 응답 유실을 구분한다. */
function toOutcome(error: unknown): CallOutcome<PrintJobStatusResponse> {
  if (error instanceof ApiError) {
    return { kind: 'rejected', code: error.detail.code, message: error.detail.message };
  }
  return { kind: 'lost', message: CLIENT_ERROR_MESSAGES_KO.REQUEST_FAILED };
}

export type UsePrintJobOptions = {
  /** 상태 조회 간격. 시험에서만 바꾼다. */
  pollIntervalMs?: number;
};

export function usePrintJob({ pollIntervalMs = POLL_INTERVAL_MS }: UsePrintJobOptions = {}) {
  const [state, setState] = useState<PrintJobMachineState>(createPrintJobState);

  const stateRef = useRef(state);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const mountedRef = useRef(true);
  // 비동기 콜백이 자기 자신을 부르기 위한 통로(useCaptureSession과 같은 방식).
  const applyRef = useRef<(result: PrintJobResult) => void>(() => undefined);

  const runEffect = useCallback(
    (effect: PrintEffect) => {
      // 언마운트 뒤에도 URL 해제만은 반드시 수행한다.
      if (effect.type === 'release-images') {
        effect.urls.forEach((url) => URL.revokeObjectURL(url));
        return;
      }

      if (!mountedRef.current) return;

      switch (effect.type) {
        case 'submit': {
          const { attempt, ticket } = effect;
          void api
            .submitPrintJob({
              schemaVersion: SCHEMA_VERSION,
              clientJobId: ticket.clientJobId,
              profileId: ticket.profileId,
              profileRevision: ticket.profileRevision,
              bitmap: ticket.bitmap,
            })
            .then(
              (value) => applyRef.current(submitSettled(stateRef.current, attempt, { kind: 'ok', value })),
              (error: unknown) =>
                applyRef.current(submitSettled(stateRef.current, attempt, toOutcome(error))),
            );
          break;
        }

        case 'schedule-poll': {
          const { attempt, clientJobId } = effect;
          if (timerRef.current) clearTimeout(timerRef.current);
          timerRef.current = setTimeout(() => {
            timerRef.current = null;
            if (!mountedRef.current) return;
            // 응답이 끊겼어도 새 UUID를 만들지 않고 같은 작업을 조회한다.
            void api.printJob(clientJobId).then(
              (value) => applyRef.current(pollSettled(stateRef.current, attempt, { kind: 'ok', value })),
              (error: unknown) => applyRef.current(pollSettled(stateRef.current, attempt, toOutcome(error))),
            );
          }, pollIntervalMs);
          break;
        }

        case 'fetch-artifacts': {
          const { attempt, clientJobId, kinds } = effect;
          void (async () => {
            const loaded: Partial<Record<ArtifactKind, string>> = {};
            let failure: string | null = null;

            for (const kind of kinds) {
              try {
                loaded[kind] = await fetchArtifactBlobUrl(clientJobId, kind);
              } catch (error) {
                failure =
                  error instanceof ApiError
                    ? error.detail.message
                    : CLIENT_ERROR_MESSAGES_KO.ARTIFACT_FETCH_FAILED;
                break;
              }
            }

            const urls = Object.values(loaded).filter((url): url is string => !!url);
            applyRef.current(
              artifactsSettled(
                stateRef.current,
                attempt,
                failure === null ? { kind: 'ok', images: loaded } : { kind: 'failed', message: failure, urls },
              ),
            );
          })();
          break;
        }
      }
    },
    [pollIntervalMs],
  );

  const apply = useCallback(
    (result: PrintJobResult) => {
      stateRef.current = result.state;
      if (mountedRef.current) setState(result.state);
      result.effects.forEach(runEffect);
    },
    [runEffect],
  );

  useEffect(() => {
    applyRef.current = apply;
  }, [apply]);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
      if (timerRef.current) clearTimeout(timerRef.current);
      Object.values(stateRef.current.images).forEach((url) => url && URL.revokeObjectURL(url));
    };
  }, []);

  /** 클릭 시점에 고정한 ticket으로 접수한다. 진행 중이면 아무 일도 하지 않는다. */
  const start = useCallback(
    (ticket: PrintTicket) => apply(startPrint(stateRef.current, ticket)),
    [apply],
  );

  const reset = useCallback(() => {
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = null;
    apply(resetPrintJob(stateRef.current));
  }, [apply]);

  return useMemo(
    () => ({ state, isPrinting: isPrinting(state), start, reset }),
    [state, start, reset],
  );
}
