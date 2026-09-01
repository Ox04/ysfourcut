// 네 컷 촬영 상태 머신. React·DOM·타이머에 의존하지 않는 순수 로직이라 그대로 시험한다.
// 늦게 도착한 프레임과 타이머는 sessionId·captureToken으로 걸러 이전 세션을 복원하지 못하게 한다.
// 원본 이미지 URL은 브라우저 메모리 전용이며 이 모듈은 저장·전송을 하지 않는다.

export const CUT_COUNT = 4;
export const COUNTDOWN_SECONDS = 3;

export type CaptureStatus =
  /** 촬영 전 */
  | 'idle'
  /** 카운트다운 중 */
  | 'countdown'
  /** 셔터를 눌러 프레임을 기다리는 중 */
  | 'capturing'
  /** 네 컷을 모두 채움 */
  | 'complete';

export type CutImage = {
  /** blob URL. 브라우저 메모리에만 존재한다. */
  url: string;
  capturedAtMs: number;
};

export type CaptureState = {
  /** 촬영 세션. 시작·취소·전체 재촬영마다 증가한다. */
  sessionId: number;
  /** 셔터 한 번. 카운트다운을 시작할 때마다 증가한다. */
  captureToken: number;
  status: CaptureStatus;
  /** 길이 4 고정. null은 아직 찍지 않은 슬롯이다. */
  slots: readonly (CutImage | null)[];
  /** 지금 찍는 컷 번호(1~4). 대기 중이면 null */
  targetCut: number | null;
  secondsLeft: number | null;
  /** 한 슬롯만 다시 찍는 중인지 */
  isRetake: boolean;
};

export type CaptureEffect =
  /** 지금 영상 프레임을 캡처하라. 결과는 acceptFrame으로 되돌린다. */
  | { type: 'capture-frame'; sessionId: number; captureToken: number; cut: number }
  /** 더 쓰지 않는 이미지 URL. 호출자가 해제한다. */
  | { type: 'release-url'; url: string };

export type CaptureResult = { state: CaptureState; effects: CaptureEffect[] };

export function createCaptureState(): CaptureState {
  return {
    sessionId: 0,
    captureToken: 0,
    status: 'idle',
    slots: [null, null, null, null],
    targetCut: null,
    secondsLeft: null,
    isRetake: false,
  };
}

export function filledCutCount(state: CaptureState): number {
  return state.slots.filter((slot) => slot !== null).length;
}

/** 카운트다운·캡처가 진행 중이면 새 조작을 받지 않는다(연타·effect 재실행 방지). */
export function isBusy(state: CaptureState): boolean {
  return state.status === 'countdown' || state.status === 'capturing';
}

function releaseAll(state: CaptureState): CaptureEffect[] {
  return state.slots
    .filter((slot): slot is CutImage => slot !== null)
    .map((slot) => ({ type: 'release-url', url: slot.url }) as const);
}

/** 지정한 컷의 카운트다운을 시작한다. 셔터 토큰을 새로 발급한다. */
function beginCut(state: CaptureState, cut: number, isRetake: boolean): CaptureState {
  return {
    ...state,
    captureToken: state.captureToken + 1,
    status: 'countdown',
    targetCut: cut,
    secondsLeft: COUNTDOWN_SECONDS,
    isRetake,
  };
}

/** 이전 사진을 모두 해제하고 새 세션의 1컷부터 시작한다. */
function restart(state: CaptureState): CaptureResult {
  const cleared: CaptureState = {
    ...state,
    sessionId: state.sessionId + 1,
    slots: [null, null, null, null],
  };

  return { state: beginCut(cleared, 1, false), effects: releaseAll(state) };
}

/** 촬영 시작. 이미 촬영 중이면 무시한다(연타·effect 재실행으로 세션이 겹치지 않는다). */
export function startSession(state: CaptureState): CaptureResult {
  return isBusy(state) ? { state, effects: [] } : restart(state);
}

/**
 * 전체 다시 찍기. 사용자가 명시적으로 버리는 동작이므로 촬영 중에도 받는다.
 * 새 세션이라 이전 세션의 늦은 타이머·프레임은 버려진다.
 */
export function retakeAll(state: CaptureState): CaptureResult {
  return restart(state);
}

/**
 * 한 컷만 다시 찍는다. 다른 세 장은 그대로 둔다.
 * 네 컷을 모두 채운 뒤에만 받는다(촬영 도중에는 순서대로 진행한다).
 */
export function retakeCut(state: CaptureState, cut: number): CaptureResult {
  if (isBusy(state) || cut < 1 || cut > CUT_COUNT || filledCutCount(state) < CUT_COUNT) {
    return { state, effects: [] };
  }

  return { state: beginCut(state, cut, true), effects: [] };
}

/** 촬영 취소. 세션을 무효화하고 사진을 모두 해제한다. */
export function cancelSession(state: CaptureState): CaptureResult {
  return {
    state: {
      ...createCaptureState(),
      sessionId: state.sessionId + 1,
      captureToken: state.captureToken + 1,
    },
    effects: releaseAll(state),
  };
}

/** 1초 경과. 0에 닿으면 캡처를 요청한다. */
export function tick(state: CaptureState): CaptureResult {
  if (state.status !== 'countdown' || state.secondsLeft === null || state.targetCut === null) {
    return { state, effects: [] };
  }

  const secondsLeft = state.secondsLeft - 1;
  if (secondsLeft > 0) {
    return { state: { ...state, secondsLeft }, effects: [] };
  }

  return {
    state: { ...state, status: 'capturing', secondsLeft: 0 },
    effects: [
      {
        type: 'capture-frame',
        sessionId: state.sessionId,
        captureToken: state.captureToken,
        cut: state.targetCut,
      },
    ],
  };
}

export type IncomingFrame = {
  sessionId: number;
  captureToken: number;
  url: string;
  nowMs: number;
};

/**
 * 캡처된 프레임을 받는다. 세션·셔터 토큰이 다르면(취소·전체 재촬영·중복 캡처)
 * 슬롯을 바꾸지 않고 URL만 해제하도록 알린다.
 */
export function acceptFrame(state: CaptureState, frame: IncomingFrame): CaptureResult {
  const stale =
    state.status !== 'capturing' ||
    state.sessionId !== frame.sessionId ||
    state.captureToken !== frame.captureToken ||
    state.targetCut === null;

  if (stale) {
    return { state, effects: [{ type: 'release-url', url: frame.url }] };
  }

  const cut = state.targetCut!;
  const previous = state.slots[cut - 1];
  const effects: CaptureEffect[] = previous ? [{ type: 'release-url', url: previous.url }] : [];

  const slots = state.slots.map((slot, index) =>
    index === cut - 1 ? { url: frame.url, capturedAtMs: frame.nowMs } : slot,
  );

  const filled: CaptureState = { ...state, slots, secondsLeft: null };

  // 한 컷만 다시 찍은 경우에는 다음 컷으로 넘어가지 않는다.
  const nextEmpty = state.isRetake ? -1 : slots.findIndex((slot) => slot === null);

  if (nextEmpty === -1) {
    return {
      state: {
        ...filled,
        status: slots.every((slot) => slot !== null) ? 'complete' : 'idle',
        targetCut: null,
        isRetake: false,
      },
      effects,
    };
  }

  return { state: beginCut(filled, nextEmpty + 1, false), effects };
}

/** 촬영 화면에 넘길 슬롯 표현. UI의 CutSlot과 같은 모양이다. */
export function toCutSlots(state: CaptureState): { index: number; imageUrl: string | null }[] {
  return state.slots.map((slot, index) => ({ index: index + 1, imageUrl: slot?.url ?? null }));
}
