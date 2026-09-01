// 가상 출력 한 건의 수명(접수 → 상태 조회 → 서버 PNG 수신)을 다루는 순수 상태 머신.
// React·타이머·fetch에 의존하지 않으므로 그대로 시험한다.
//
// 지키는 규칙(SERVICE_DESIGN 9절 · VIRTUAL_PRINTER_DESIGN 8절):
// - 클릭 한 번에 만든 ticket(비트맵·프로필 revision·UUID)을 끝까지 고정한다.
// - 응답을 받지 못하면 **같은 UUID를 조회**한다. 새 UUID로 자동 재접수하지 않는다.
// - 성공은 서버가 확정한 `rendered` + 세 이미지를 모두 받은 뒤에만 만든다.
// - 늦게 도착한 응답은 attempt로 걸러 이전 작업을 되살리지 않는다.

import type { ArtifactKind, PrintJobBitmap, PrintJobStatusResponse } from '../api/contract';

/** 상태 조회 간격. 서버는 렌더 10초 예산 + 지연 주입 최대 10초를 쓴다. */
export const POLL_INTERVAL_MS = 500;

/** 조회 횟수 상한. 넘으면 자동 재시도 없이 실패로 확정하고 사용자 판단에 맡긴다. */
export const MAX_POLLS = 120;

/** 완성 결과에 반드시 있어야 하는 이미지. 하나라도 없으면 성공으로 표시하지 않는다. */
export const REQUIRED_ARTIFACTS: readonly ArtifactKind[] = ['content', 'paper', 'appearance'];

/** 서버가 아직 진행 중인 상태. terminal 판단은 이 목록 + queueBusy로 한다. */
const IN_FLIGHT_STATES: readonly string[] = ['accepted', 'rendering', 'submitting'];

/** 서버 오류 코드와 겹치지 않는 앱 내부 코드. describeError는 여기 message를 그대로 쓴다. */
export const CLIENT_ERROR_MESSAGES_KO = {
  ARTIFACT_FETCH_FAILED: '서버가 만든 결과 이미지를 받지 못했어요. 다시 시도해 주세요.',
  ARTIFACT_INCOMPLETE: '서버가 결과 이미지를 다 만들지 못했어요. 다시 시도해 주세요.',
  POLL_GAVE_UP: '출력 상태를 확인하지 못했어요. 로컬 프로그램을 확인한 뒤 다시 시도해 주세요.',
  REQUEST_FAILED: '로컬 프로그램에 연결하지 못했어요. 다시 시도해 주세요.',
} as const;

export type PrintTicket = {
  /** 클릭 한 번에 만든 UUID. 재조회에도 같은 값을 쓴다. */
  clientJobId: string;
  profileId: string;
  profileRevision: string;
  /** 클릭 시점의 최종 비트맵. 이후 편집이 바뀌어도 이 값은 바뀌지 않는다. */
  bitmap: PrintJobBitmap;
};

export type PrintPhase =
  /** 아직 아무 작업도 없음 */
  | 'idle'
  /** POST 진행 중 */
  | 'submitting'
  /** 같은 UUID로 상태를 조회하는 중 */
  | 'polling'
  /** 서버가 rendered를 확정해 이미지를 받는 중 */
  | 'loading-images'
  /** 세 이미지를 모두 받음 */
  | 'rendered'
  /** 확정된 실패 */
  | 'failed';

export type PrintFailure = { code: string; message: string };

export type PrintJobMachineState = {
  phase: PrintPhase;
  /** 접수 시도 번호. 늦은 응답을 거르는 유일한 기준이다. */
  attempt: number;
  ticket: PrintTicket | null;
  status: PrintJobStatusResponse | null;
  polls: number;
  failure: PrintFailure | null;
  /** 서버 PNG의 blob URL. 호출자가 만들고 release-images로 해제한다. */
  images: Partial<Record<ArtifactKind, string>>;
  /** 실패 지점까지의 진단 이미지인지 (complete=false) */
  isPartial: boolean;
};

export type PrintEffect =
  | { type: 'submit'; attempt: number; ticket: PrintTicket }
  | { type: 'schedule-poll'; attempt: number; clientJobId: string }
  | { type: 'fetch-artifacts'; attempt: number; clientJobId: string; kinds: ArtifactKind[] }
  /** 더 쓰지 않는 blob URL. 호출자가 해제한다. */
  | { type: 'release-images'; urls: string[] };

export type PrintJobResult = { state: PrintJobMachineState; effects: PrintEffect[] };

/** 한 번의 호출 결과. `lost`는 응답 자체를 못 받은 경우로 서버는 이미 접수했을 수 있다. */
export type CallOutcome<T> =
  | { kind: 'ok'; value: T }
  | { kind: 'lost'; message: string }
  | { kind: 'rejected'; code: string; message: string };

export function createPrintJobState(): PrintJobMachineState {
  return {
    phase: 'idle',
    attempt: 0,
    ticket: null,
    status: null,
    polls: 0,
    failure: null,
    images: {},
    isPartial: false,
  };
}

/** 접수~수신 중. 이 동안에는 새 접수·설정 변경·세션 정리를 받지 않는다. */
export function isPrinting(state: PrintJobMachineState): boolean {
  return state.phase === 'submitting' || state.phase === 'polling' || state.phase === 'loading-images';
}

function imageUrls(state: PrintJobMachineState): string[] {
  return Object.values(state.images).filter((url): url is string => typeof url === 'string');
}

function releaseImages(state: PrintJobMachineState): PrintEffect[] {
  const urls = imageUrls(state);
  return urls.length > 0 ? [{ type: 'release-images', urls }] : [];
}

/** 조회를 한 번 더 예약한다. 예산을 넘기면 자동 재시도 없이 실패로 확정한다. */
function schedulePoll(state: PrintJobMachineState): PrintJobResult {
  if (state.ticket === null) return { state, effects: [] };

  if (state.polls >= MAX_POLLS) {
    return fail(state, {
      code: 'POLL_GAVE_UP',
      message: CLIENT_ERROR_MESSAGES_KO.POLL_GAVE_UP,
    });
  }

  return {
    state: { ...state, phase: 'polling', polls: state.polls + 1 },
    effects: [{ type: 'schedule-poll', attempt: state.attempt, clientJobId: state.ticket.clientJobId }],
  };
}

function fail(state: PrintJobMachineState, failure: PrintFailure): PrintJobResult {
  return { state: { ...state, phase: 'failed', failure }, effects: [] };
}

/** 서버 상태 하나를 반영한다. terminal이 아니면 계속 조회한다. */
function applyStatus(
  state: PrintJobMachineState,
  status: PrintJobStatusResponse,
): PrintJobResult {
  const next: PrintJobMachineState = { ...state, status };

  if (status.queueBusy || IN_FLIGHT_STATES.includes(status.state)) {
    return schedulePoll(next);
  }

  if (status.state === 'rendered') {
    const missing = REQUIRED_ARTIFACTS.filter((kind) => {
      const artifact = status.artifacts.find((item) => item.kind === kind);
      return !artifact || !artifact.available || !artifact.complete;
    });

    // 서버가 rendered라고 해도 필수 이미지가 없으면 성공을 표시하지 않는다.
    if (missing.length > 0) {
      return fail(next, {
        code: 'ARTIFACT_INCOMPLETE',
        message: CLIENT_ERROR_MESSAGES_KO.ARTIFACT_INCOMPLETE,
      });
    }

    return {
      state: { ...next, phase: 'loading-images', isPartial: false },
      effects: [
        {
          type: 'fetch-artifacts',
          attempt: state.attempt,
          clientJobId: status.clientJobId,
          kinds: [...REQUIRED_ARTIFACTS],
        },
      ],
    };
  }

  // terminal 실패. 부분 이미지는 진단용으로만 받아 온다.
  const diagnostic = status.artifacts.filter((item) => item.available).map((item) => item.kind);
  const failed: PrintJobMachineState = {
    ...next,
    phase: 'failed',
    isPartial: status.artifacts.some((item) => item.available && !item.complete),
    failure: status.failure
      ? { code: status.failure.code, message: status.failure.message }
      : { code: 'REQUEST_FAILED', message: CLIENT_ERROR_MESSAGES_KO.REQUEST_FAILED },
  };

  return {
    state: failed,
    effects:
      diagnostic.length > 0
        ? [
            {
              type: 'fetch-artifacts',
              attempt: state.attempt,
              clientJobId: status.clientJobId,
              kinds: diagnostic,
            },
          ]
        : [],
  };
}

/**
 * 출력 클릭. 진행 중이면 아무것도 하지 않는다(연타가 두 번째 POST를 만들지 않는다).
 * 재시도·다시 출력도 이 함수를 쓰며, 호출자가 **새 UUID**를 담은 ticket을 만든다.
 */
export function startPrint(state: PrintJobMachineState, ticket: PrintTicket): PrintJobResult {
  if (isPrinting(state)) return { state, effects: [] };

  const attempt = state.attempt + 1;

  return {
    state: {
      ...createPrintJobState(),
      attempt,
      phase: 'submitting',
      ticket,
    },
    effects: [...releaseImages(state), { type: 'submit', attempt, ticket }],
  };
}

export function submitSettled(
  state: PrintJobMachineState,
  attempt: number,
  outcome: CallOutcome<PrintJobStatusResponse>,
): PrintJobResult {
  if (attempt !== state.attempt || state.phase !== 'submitting') {
    return { state, effects: [] };
  }

  switch (outcome.kind) {
    case 'ok':
      return applyStatus(state, outcome.value);
    case 'lost':
      // 서버가 이미 접수했을 수 있다. **같은 UUID 조회부터** 한다.
      return schedulePoll(state);
    case 'rejected':
      return fail(state, { code: outcome.code, message: outcome.message });
  }
}

export function pollSettled(
  state: PrintJobMachineState,
  attempt: number,
  outcome: CallOutcome<PrintJobStatusResponse>,
): PrintJobResult {
  if (attempt !== state.attempt || state.phase !== 'polling') {
    return { state, effects: [] };
  }

  switch (outcome.kind) {
    case 'ok':
      return applyStatus(state, outcome.value);
    case 'lost':
      // 재접속에서도 새 작업을 만들지 않고 같은 UUID를 계속 조회한다.
      return schedulePoll(state);
    case 'rejected':
      return fail(state, { code: outcome.code, message: outcome.message });
  }
}

export type ArtifactOutcome =
  | { kind: 'ok'; images: Partial<Record<ArtifactKind, string>> }
  /** 일부만 받았을 때 이미 만든 URL을 함께 넘겨 해제한다. */
  | { kind: 'failed'; message: string; urls: string[] };

export function artifactsSettled(
  state: PrintJobMachineState,
  attempt: number,
  outcome: ArtifactOutcome,
): PrintJobResult {
  const urls = outcome.kind === 'ok' ? Object.values(outcome.images) : outcome.urls;
  const release: PrintEffect[] =
    urls.length > 0 ? [{ type: 'release-images', urls: urls.filter((url): url is string => !!url) }] : [];

  // 정리·새 작업 뒤에 도착한 결과는 화면에 올리지 않고 버린다.
  if (attempt !== state.attempt) return { state, effects: release };

  if (state.phase === 'loading-images') {
    if (outcome.kind === 'failed') {
      return {
        state: {
          ...state,
          phase: 'failed',
          failure: { code: 'ARTIFACT_FETCH_FAILED', message: outcome.message },
        },
        effects: release,
      };
    }

    const missing = REQUIRED_ARTIFACTS.filter((kind) => !outcome.images[kind]);
    if (missing.length > 0) {
      return {
        state: {
          ...state,
          phase: 'failed',
          failure: {
            code: 'ARTIFACT_INCOMPLETE',
            message: CLIENT_ERROR_MESSAGES_KO.ARTIFACT_INCOMPLETE,
          },
        },
        effects: release,
      };
    }

    return { state: { ...state, phase: 'rendered', images: outcome.images }, effects: [] };
  }

  // 확정 실패의 진단 이미지. 성공으로 바꾸지 않는다.
  if (state.phase === 'failed' && outcome.kind === 'ok') {
    return { state: { ...state, images: outcome.images }, effects: [] };
  }

  return { state, effects: release };
}

/**
 * 결과를 버리고 처음 상태로 돌아간다(다음 촬영·유휴 정리·화면 이탈).
 * attempt를 올려 진행 중이던 응답이 화면에 다시 나타나지 않게 한다.
 */
export function resetPrintJob(state: PrintJobMachineState): PrintJobResult {
  return {
    state: { ...createPrintJobState(), attempt: state.attempt + 1 },
    effects: releaseImages(state),
  };
}
