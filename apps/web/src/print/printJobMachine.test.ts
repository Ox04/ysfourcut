import { describe, expect, it } from 'vitest';
import type { ArtifactKind, PrintJobStatusResponse } from '../api/contract';
import {
  MAX_POLLS,
  artifactsSettled,
  createPrintJobState,
  isPrinting,
  pollSettled,
  resetPrintJob,
  startPrint,
  submitSettled,
  type PrintEffect,
  type PrintJobMachineState,
  type PrintTicket,
} from './printJobMachine';

function ticket(id: string): PrintTicket {
  return {
    clientJobId: id,
    profileId: 'virtual-80mm-8dpmm',
    profileRevision: 'r1',
    bitmap: {
      widthDots: 8,
      heightDots: 2,
      strideBytes: 1,
      bitOrder: 'msb-first',
      blackBit: 1,
      dataBase64: 'AAA=',
    },
  };
}

function artifact(kind: ArtifactKind, available: boolean, complete: boolean) {
  return {
    kind,
    path: `/api/print-jobs/j/artifacts/${kind}`,
    available,
    complete,
    widthPx: 640,
    heightPx: 100,
    byteLength: 128,
    expiresAtUtc: null,
  };
}

function status(overrides: Partial<PrintJobStatusResponse> = {}): PrintJobStatusResponse {
  return {
    schemaVersion: 1,
    clientJobId: 'job-1',
    printerMode: 'virtual',
    state: 'rendered',
    isPhysical: false,
    spoolJobId: null,
    queueBusy: false,
    profileId: 'virtual-80mm-8dpmm',
    profileRevision: 'r1',
    sourceDigest: 'sha256:abc',
    layout: null,
    artifacts: [
      artifact('content', true, true),
      artifact('paper', true, true),
      artifact('appearance', true, true),
    ],
    simulation: null,
    failure: null,
    createdAtUtc: '2026-09-01T00:00:00Z',
    updatedAtUtc: '2026-09-01T00:00:01Z',
    ...overrides,
  };
}

function effectsOf(effects: PrintEffect[], type: PrintEffect['type']) {
  return effects.filter((effect) => effect.type === type);
}

/** 접수 → 조회 → 이미지 수신 직전(loading-images)까지 진행한 상태를 만든다. */
function loadingImages(): PrintJobMachineState {
  const started = startPrint(createPrintJobState(), ticket('job-1'));
  return submitSettled(started.state, started.state.attempt, { kind: 'ok', value: status() }).state;
}

describe('printJobMachine — 접수와 연타 (AC-10-02)', () => {
  it('클릭 한 번에 만든 ticket을 그대로 접수한다', () => {
    const result = startPrint(createPrintJobState(), ticket('job-1'));

    expect(result.state.phase).toBe('submitting');
    expect(result.state.ticket?.clientJobId).toBe('job-1');
    expect(effectsOf(result.effects, 'submit')).toHaveLength(1);
  });

  it('진행 중 연타는 두 번째 접수를 만들지 않는다', () => {
    const first = startPrint(createPrintJobState(), ticket('job-1'));
    const second = startPrint(first.state, ticket('job-2'));

    expect(second.effects).toHaveLength(0);
    expect(second.state).toBe(first.state);
    expect(second.state.ticket?.clientJobId).toBe('job-1');
  });

  it('응답을 받지 못하면 새 UUID가 아니라 같은 작업을 조회한다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const lost = submitSettled(started.state, started.state.attempt, {
      kind: 'lost',
      message: '연결 실패',
    });

    expect(lost.state.phase).toBe('polling');
    expect(effectsOf(lost.effects, 'submit')).toHaveLength(0);
    expect(lost.effects).toEqual([
      { type: 'schedule-poll', attempt: started.state.attempt, clientJobId: 'job-1' },
    ]);
  });

  it('조회 중 연결이 끊겨도 같은 작업을 계속 조회한다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const polling = submitSettled(started.state, started.state.attempt, {
      kind: 'lost',
      message: '연결 실패',
    });
    const again = pollSettled(polling.state, polling.state.attempt, {
      kind: 'lost',
      message: '연결 실패',
    });

    expect(again.state.phase).toBe('polling');
    expect(again.effects).toEqual([
      { type: 'schedule-poll', attempt: polling.state.attempt, clientJobId: 'job-1' },
    ]);
  });

  it('진행 중이면 계속 같은 작업을 조회하고 재접수하지 않는다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const accepted = submitSettled(started.state, started.state.attempt, {
      kind: 'ok',
      value: status({ state: 'accepted', queueBusy: true, artifacts: [] }),
    });

    expect(accepted.state.phase).toBe('polling');
    expect(effectsOf(accepted.effects, 'submit')).toHaveLength(0);
    expect(effectsOf(accepted.effects, 'schedule-poll')).toHaveLength(1);
  });

  it('이미 지난 시도의 늦은 응답은 상태를 되돌리지 않는다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const stale = submitSettled(started.state, started.state.attempt - 1, {
      kind: 'ok',
      value: status(),
    });

    expect(stale.state).toBe(started.state);
    expect(stale.effects).toHaveLength(0);
  });

  it('조회 예산을 넘기면 자동 재시도 없이 실패로 확정한다', () => {
    let state = startPrint(createPrintJobState(), ticket('job-1')).state;
    state = submitSettled(state, state.attempt, {
      kind: 'ok',
      value: status({ state: 'rendering', queueBusy: true, artifacts: [] }),
    }).state;

    let last = { state, effects: [] as PrintEffect[] };
    for (let index = 0; index < MAX_POLLS + 2; index += 1) {
      last = pollSettled(last.state, last.state.attempt, {
        kind: 'ok',
        value: status({ state: 'rendering', queueBusy: true, artifacts: [] }),
      });
      if (last.state.phase === 'failed') break;
    }

    expect(last.state.phase).toBe('failed');
    expect(last.state.failure?.code).toBe('POLL_GAVE_UP');
    expect(effectsOf(last.effects, 'schedule-poll')).toHaveLength(0);
  });
});

describe('printJobMachine — 성공 판정 (AC-10-03 / AC-10-04)', () => {
  it('서버가 rendered여도 필수 이미지가 없으면 성공으로 만들지 않는다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const result = submitSettled(started.state, started.state.attempt, {
      kind: 'ok',
      value: status({
        artifacts: [
          artifact('content', true, true),
          artifact('paper', false, false),
          artifact('appearance', true, true),
        ],
      }),
    });

    expect(result.state.phase).toBe('failed');
    expect(result.state.failure?.code).toBe('ARTIFACT_INCOMPLETE');
    expect(effectsOf(result.effects, 'fetch-artifacts')).toHaveLength(0);
  });

  it('세 이미지를 모두 받은 뒤에만 rendered가 된다', () => {
    const state = loadingImages();
    expect(state.phase).toBe('loading-images');

    const partial = artifactsSettled(state, state.attempt, {
      kind: 'ok',
      images: { content: 'blob:c', paper: 'blob:p' },
    });
    expect(partial.state.phase).toBe('failed');
    expect(partial.effects).toEqual([{ type: 'release-images', urls: ['blob:c', 'blob:p'] }]);

    const full = artifactsSettled(state, state.attempt, {
      kind: 'ok',
      images: { content: 'blob:c', paper: 'blob:p', appearance: 'blob:a' },
    });
    expect(full.state.phase).toBe('rendered');
    expect(full.effects).toHaveLength(0);
  });

  it('이미지 수신 실패는 성공 대신 실패로 확정하고 받은 URL을 해제한다', () => {
    const state = loadingImages();
    const failed = artifactsSettled(state, state.attempt, {
      kind: 'failed',
      message: '결과 이미지가 만료됐어요.',
      urls: ['blob:c'],
    });

    expect(failed.state.phase).toBe('failed');
    expect(failed.state.failure?.code).toBe('ARTIFACT_FETCH_FAILED');
    expect(failed.effects).toEqual([{ type: 'release-images', urls: ['blob:c'] }]);
  });

  it('부분 이미지가 있어도 성공이 아니라 진단용으로만 받는다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const result = submitSettled(started.state, started.state.attempt, {
      kind: 'ok',
      value: status({
        state: 'virtual_failed',
        failure: { code: 'VIRTUAL_RENDER_FAILED', message: '렌더 실패' },
        artifacts: [artifact('content', true, false)],
      }),
    });

    expect(result.state.phase).toBe('failed');
    expect(result.state.isPartial).toBe(true);
    expect(result.effects).toEqual([
      { type: 'fetch-artifacts', attempt: started.state.attempt, clientJobId: 'job-1', kinds: ['content'] },
    ]);

    const withImage = artifactsSettled(result.state, result.state.attempt, {
      kind: 'ok',
      images: { content: 'blob:partial' },
    });
    expect(withImage.state.phase).toBe('failed');
    expect(withImage.state.images.content).toBe('blob:partial');
  });

  it('서버가 구조화된 오류를 돌려주면 실패로 확정한다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const rejected = submitSettled(started.state, started.state.attempt, {
      kind: 'rejected',
      code: 'PRINTER_BUSY',
      message: '진행 중',
    });

    expect(rejected.state.phase).toBe('failed');
    expect(rejected.state.failure?.code).toBe('PRINTER_BUSY');
    expect(rejected.effects).toHaveLength(0);
  });
});

describe('printJobMachine — 재시도와 정리 (AC-10-03 / AC-10-05)', () => {
  it('확정 실패 뒤에만 재시도할 수 있고 재시도는 새 UUID를 쓴다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const polling = submitSettled(started.state, started.state.attempt, {
      kind: 'lost',
      message: '연결 실패',
    });

    // 확정 전에는 재시도를 받지 않는다.
    expect(startPrint(polling.state, ticket('job-2')).effects).toHaveLength(0);

    const failed = pollSettled(polling.state, polling.state.attempt, {
      kind: 'rejected',
      code: 'VIRTUAL_OUT_OF_PAPER',
      message: '가상 용지 없음',
    });
    expect(failed.state.phase).toBe('failed');

    const retried = startPrint(failed.state, ticket('job-2'));
    expect(retried.state.ticket?.clientJobId).toBe('job-2');
    expect(retried.state.attempt).toBe(failed.state.attempt + 1);
    expect(effectsOf(retried.effects, 'submit')).toHaveLength(1);
  });

  it('재출력은 이전 결과 URL을 먼저 해제한다', () => {
    const rendered = artifactsSettled(loadingImages(), 1, {
      kind: 'ok',
      images: { content: 'blob:c', paper: 'blob:p', appearance: 'blob:a' },
    }).state;
    expect(rendered.phase).toBe('rendered');

    const again = startPrint(rendered, ticket('job-2'));
    expect(effectsOf(again.effects, 'release-images')).toEqual([
      { type: 'release-images', urls: ['blob:c', 'blob:p', 'blob:a'] },
    ]);
    expect(again.state.images).toEqual({});
  });

  it('정리는 결과 URL을 모두 해제하고 처음 상태로 돌아간다', () => {
    const rendered = artifactsSettled(loadingImages(), 1, {
      kind: 'ok',
      images: { content: 'blob:c', paper: 'blob:p', appearance: 'blob:a' },
    }).state;

    const cleared = resetPrintJob(rendered);
    expect(cleared.state.phase).toBe('idle');
    expect(cleared.state.images).toEqual({});
    expect(cleared.state.status).toBeNull();
    expect(cleared.effects).toEqual([
      { type: 'release-images', urls: ['blob:c', 'blob:p', 'blob:a'] },
    ]);
  });

  it('정리 뒤 도착한 늦은 이미지는 화면에 올리지 않고 해제한다', () => {
    const state = loadingImages();
    const cleared = resetPrintJob(state).state;

    const late = artifactsSettled(cleared, state.attempt, {
      kind: 'ok',
      images: { content: 'blob:c', paper: 'blob:p', appearance: 'blob:a' },
    });

    expect(late.state.phase).toBe('idle');
    expect(late.state.images).toEqual({});
    expect(late.effects).toEqual([
      { type: 'release-images', urls: ['blob:c', 'blob:p', 'blob:a'] },
    ]);
  });

  it('다음 작업이 시작된 뒤 도착한 이전 작업의 이미지는 화면을 덮지 않는다', () => {
    const first = loadingImages();
    const cleared = resetPrintJob(first).state;

    // 다음 사용자가 새 작업을 시작해 같은 phase(loading-images)에 들어간 상태.
    const secondStart = startPrint(cleared, ticket('job-2'));
    const second = submitSettled(secondStart.state, secondStart.state.attempt, {
      kind: 'ok',
      value: status({ clientJobId: 'job-2' }),
    }).state;
    expect(second.phase).toBe('loading-images');

    const late = artifactsSettled(second, first.attempt, {
      kind: 'ok',
      images: { content: 'blob:old-c', paper: 'blob:old-p', appearance: 'blob:old-a' },
    });

    expect(late.state.phase).toBe('loading-images');
    expect(late.state.images).toEqual({});
    expect(late.effects).toEqual([
      { type: 'release-images', urls: ['blob:old-c', 'blob:old-p', 'blob:old-a'] },
    ]);
  });

  it('정리 뒤 도착한 늦은 상태 응답은 결과를 되살리지 않는다', () => {
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    const polling = submitSettled(started.state, started.state.attempt, {
      kind: 'lost',
      message: '연결 실패',
    });
    const cleared = resetPrintJob(polling.state).state;

    const late = pollSettled(cleared, polling.state.attempt, { kind: 'ok', value: status() });
    expect(late.state.phase).toBe('idle');
    expect(late.effects).toHaveLength(0);
  });

  it('isPrinting은 접수~수신 동안만 참이다', () => {
    expect(isPrinting(createPrintJobState())).toBe(false);
    const started = startPrint(createPrintJobState(), ticket('job-1'));
    expect(isPrinting(started.state)).toBe(true);
    expect(isPrinting(loadingImages())).toBe(true);
    expect(isPrinting(resetPrintJob(started.state).state)).toBe(false);
  });
});
