// 합성 요청의 순서를 정하는 순수 상태 머신. React·타이머·Worker에 의존하지 않는다.
// 규칙: 설정이 바뀌면 revision을 올리고, 늦게 도착한 결과는 최신 revision을 덮지 않는다.
// 설정 변경은 잠깐 묶었다가(디바운스) 한 번만 합성한다.

/** 설정 변경을 묶는 시간. SERVICE_DESIGN 5절의 "150ms 정도". */
export const COMPOSE_DEBOUNCE_MS = 150;

export type ComposeJob<TInput> = { revision: number; input: TInput };

export type ComposeQueueState<TInput> = {
  /** 지금까지 만든 마지막 요청 번호. 화면 설정의 최신 상태를 가리킨다. */
  revision: number;
  /** 디바운스를 기다리는 요청. ready면 시작할 준비가 끝났다. */
  pending: (ComposeJob<TInput> & { ready: boolean }) | null;
  /** Worker/합성이 실제로 진행 중인 요청 */
  running: ComposeJob<TInput> | null;
  /** 화면에 반영한 마지막 결과 번호 */
  appliedRevision: number | null;
};

export type ComposeEffect<TInput> =
  /** 디바운스 타이머를 이 번호로 다시 건다. */
  | { type: 'schedule-debounce'; revision: number }
  /** 이 입력으로 합성을 시작한다. 결과는 settle로 되돌린다. */
  | { type: 'start'; revision: number; input: TInput }
  /** 이 결과를 화면·전송에 쓴다. */
  | { type: 'apply'; revision: number }
  /** 이 결과는 버린다(늦었거나 실패). */
  | { type: 'discard'; revision: number; reason: 'stale' | 'failed' | 'cancelled' };

export type ComposeQueueResult<TInput> = {
  state: ComposeQueueState<TInput>;
  effects: ComposeEffect<TInput>[];
};

export function createComposeQueue<TInput>(): ComposeQueueState<TInput> {
  return { revision: 0, pending: null, running: null, appliedRevision: null };
}

export function isComposing<TInput>(state: ComposeQueueState<TInput>): boolean {
  return state.running !== null || state.pending !== null;
}

/** 준비된 대기 요청이 있고 진행 중인 작업이 없으면 시작한다. */
function startIfPossible<TInput>(state: ComposeQueueState<TInput>): ComposeQueueResult<TInput> {
  if (state.running !== null || state.pending === null || !state.pending.ready) {
    return { state, effects: [] };
  }

  const { revision, input } = state.pending;
  return {
    state: { ...state, pending: null, running: { revision, input } },
    effects: [{ type: 'start', revision, input }],
  };
}

/** 새 설정·새 원본으로 합성을 요청한다. 진행 중인 작업은 그대로 두고 결과만 버린다. */
export function requestCompose<TInput>(
  state: ComposeQueueState<TInput>,
  input: TInput,
): ComposeQueueResult<TInput> {
  const revision = state.revision + 1;

  return {
    state: { ...state, revision, pending: { revision, input, ready: false } },
    effects: [{ type: 'schedule-debounce', revision }],
  };
}

/** 디바운스 시간이 지났다. 그 사이 더 새 요청이 왔으면 이 신호는 버린다. */
export function debounceElapsed<TInput>(
  state: ComposeQueueState<TInput>,
  revision: number,
): ComposeQueueResult<TInput> {
  if (state.pending === null || state.pending.revision !== revision) {
    return { state, effects: [] };
  }

  return startIfPossible({ ...state, pending: { ...state.pending, ready: true } });
}

/** 합성이 끝났다. 최신 revision일 때만 화면에 반영한다. */
export function composeSettled<TInput>(
  state: ComposeQueueState<TInput>,
  revision: number,
  outcome: 'ok' | 'failed',
): ComposeQueueResult<TInput> {
  if (state.running === null || state.running.revision !== revision) {
    // 취소·재시작 뒤에 도착한 결과. 상태를 되돌리지 않는다.
    return { state, effects: [{ type: 'discard', revision, reason: 'stale' }] };
  }

  const cleared: ComposeQueueState<TInput> = { ...state, running: null };

  if (outcome === 'failed') {
    const next = startIfPossible(cleared);
    return {
      state: next.state,
      effects: [{ type: 'discard', revision, reason: 'failed' }, ...next.effects],
    };
  }

  // 합성 도중 설정이 또 바뀌었으면 이 결과는 낡았다.
  const isLatest = revision === state.revision;
  const applied: ComposeQueueState<TInput> = isLatest
    ? { ...cleared, appliedRevision: revision }
    : cleared;

  const next = startIfPossible(applied);

  return {
    state: next.state,
    effects: [
      isLatest
        ? { type: 'apply', revision }
        : { type: 'discard', revision, reason: 'stale' },
      ...next.effects,
    ],
  };
}

/** 촬영 다시 시작·화면 이탈. 진행 중·대기 중 요청을 모두 버린다. */
export function cancelCompose<TInput>(state: ComposeQueueState<TInput>): ComposeQueueResult<TInput> {
  const effects: ComposeEffect<TInput>[] = [];

  if (state.running !== null) {
    effects.push({ type: 'discard', revision: state.running.revision, reason: 'cancelled' });
  }
  if (state.pending !== null) {
    effects.push({ type: 'discard', revision: state.pending.revision, reason: 'cancelled' });
  }

  return {
    state: { ...state, pending: null, running: null },
    effects,
  };
}
