import { describe, expect, it } from 'vitest';
import {
  cancelCompose,
  composeSettled,
  createComposeQueue,
  debounceElapsed,
  isComposing,
  requestCompose,
  type ComposeEffect,
  type ComposeQueueState,
} from './composeQueue';

type Settings = { caption: string };

function types(effects: ComposeEffect<Settings>[]): string[] {
  return effects.map((effect) => effect.type);
}

/** 요청 → 디바운스 경과까지 한 번에 진행한다. */
function requestAndStart(
  state: ComposeQueueState<Settings>,
  input: Settings,
): { state: ComposeQueueState<Settings>; startedRevision: number } {
  const requested = requestCompose(state, input);
  const revision = requested.state.revision;
  const started = debounceElapsed(requested.state, revision);
  return { state: started.state, startedRevision: revision };
}

describe('composeQueue', () => {
  it('설정을 바꾸면 revision이 올라가고 디바운스 뒤 한 번만 시작한다', () => {
    const initial = createComposeQueue<Settings>();

    const first = requestCompose(initial, { caption: 'ㄱ' });
    expect(first.state.revision).toBe(1);
    expect(types(first.effects)).toEqual(['schedule-debounce']);

    // 디바운스 중 연속 변경: 마지막 요청만 남는다.
    const second = requestCompose(first.state, { caption: 'ㄴ' });
    const third = requestCompose(second.state, { caption: 'ㄷ' });
    expect(third.state.revision).toBe(3);

    // 지난 번호의 타이머는 아무 일도 하지 않는다.
    expect(types(debounceElapsed(third.state, 1).effects)).toEqual([]);
    expect(types(debounceElapsed(third.state, 2).effects)).toEqual([]);

    const started = debounceElapsed(third.state, 3);
    expect(started.effects).toEqual([{ type: 'start', revision: 3, input: { caption: 'ㄷ' } }]);
    expect(started.state.running).toEqual({ revision: 3, input: { caption: 'ㄷ' } });
  });

  it('최신 결과는 화면에 반영한다', () => {
    const { state, startedRevision } = requestAndStart(createComposeQueue<Settings>(), { caption: 'ㄱ' });

    const settled = composeSettled(state, startedRevision, 'ok');

    expect(settled.effects).toEqual([{ type: 'apply', revision: 1 }]);
    expect(settled.state.appliedRevision).toBe(1);
    expect(isComposing(settled.state)).toBe(false);
  });

  it('늦게 끝난 합성은 최신 편집 결과를 덮지 않는다', () => {
    const first = requestAndStart(createComposeQueue<Settings>(), { caption: '처음' });

    // 합성이 도는 동안 사용자가 설정을 바꾼다.
    const changed = requestCompose(first.state, { caption: '바뀜' });
    expect(changed.state.revision).toBe(2);

    // 이제 1번 합성이 뒤늦게 끝난다.
    const late = composeSettled(changed.state, 1, 'ok');

    expect(late.effects[0]).toEqual({ type: 'discard', revision: 1, reason: 'stale' });
    expect(late.state.appliedRevision).toBeNull();

    // 2번은 디바운스가 끝난 뒤 시작된다.
    const ready = debounceElapsed(late.state, 2);
    expect(ready.effects).toEqual([{ type: 'start', revision: 2, input: { caption: '바뀜' } }]);

    const applied = composeSettled(ready.state, 2, 'ok');
    expect(applied.effects).toEqual([{ type: 'apply', revision: 2 }]);
    expect(applied.state.appliedRevision).toBe(2);
  });

  it('진행 중에 준비된 요청은 끝난 직후 이어서 시작한다', () => {
    const first = requestAndStart(createComposeQueue<Settings>(), { caption: '처음' });

    const changed = requestCompose(first.state, { caption: '다음' });
    const ready = debounceElapsed(changed.state, 2);

    // 아직 1번이 진행 중이므로 시작하지 않는다.
    expect(types(ready.effects)).toEqual([]);
    expect(ready.state.running).toEqual({ revision: 1, input: { caption: '처음' } });

    const settled = composeSettled(ready.state, 1, 'ok');
    expect(settled.effects).toEqual([
      { type: 'discard', revision: 1, reason: 'stale' },
      { type: 'start', revision: 2, input: { caption: '다음' } },
    ]);
  });

  it('이미 버린 작업의 결과가 또 와도 상태를 되돌리지 않는다', () => {
    const first = requestAndStart(createComposeQueue<Settings>(), { caption: '처음' });
    const settled = composeSettled(first.state, 1, 'ok');

    const duplicate = composeSettled(settled.state, 1, 'ok');

    expect(duplicate.effects).toEqual([{ type: 'discard', revision: 1, reason: 'stale' }]);
    expect(duplicate.state.appliedRevision).toBe(1);
  });

  it('실패한 합성은 반영하지 않고 다음 요청을 이어 간다', () => {
    const first = requestAndStart(createComposeQueue<Settings>(), { caption: '처음' });
    const changed = requestCompose(first.state, { caption: '다음' });
    const ready = debounceElapsed(changed.state, 2);

    const failed = composeSettled(ready.state, 1, 'failed');

    expect(failed.effects).toEqual([
      { type: 'discard', revision: 1, reason: 'failed' },
      { type: 'start', revision: 2, input: { caption: '다음' } },
    ]);
    expect(failed.state.appliedRevision).toBeNull();
  });

  it('취소하면 대기·진행 중 요청을 모두 버린다', () => {
    const first = requestAndStart(createComposeQueue<Settings>(), { caption: '처음' });
    const changed = requestCompose(first.state, { caption: '다음' });

    const cancelled = cancelCompose(changed.state);

    expect(cancelled.effects).toEqual([
      { type: 'discard', revision: 1, reason: 'cancelled' },
      { type: 'discard', revision: 2, reason: 'cancelled' },
    ]);
    expect(isComposing(cancelled.state)).toBe(false);

    // 취소 뒤 도착한 결과도 반영하지 않는다.
    const late = composeSettled(cancelled.state, 1, 'ok');
    expect(late.effects).toEqual([{ type: 'discard', revision: 1, reason: 'stale' }]);
    expect(late.state.appliedRevision).toBeNull();
  });
});
