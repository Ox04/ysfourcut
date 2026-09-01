import { describe, expect, it } from 'vitest';
import { decideCleanup } from './sessionCleanup';

const base = {
  clear: null,
  clearErrorMessage: null,
  sessionHostInstanceId: 'host-a',
  currentHostInstanceId: 'host-a',
};

describe('decideCleanup — 다음 사용자 시작 조건 (AC-10-05)', () => {
  it('remainingJobs가 0이면 다음 사용자를 시작한다', () => {
    const decision = decideCleanup({ ...base, clear: { clearedJobs: 2, remainingJobs: 0 } });

    expect(decision.canStartNext).toBe(true);
    expect(decision.reason).toBe('cleared');
    expect(decision.message).toContain('2건');
  });

  it('서버에 결과가 남아 있으면 다음 사용자를 시작하지 않는다', () => {
    const decision = decideCleanup({ ...base, clear: { clearedJobs: 0, remainingJobs: 1 } });

    expect(decision.canStartNext).toBe(false);
    expect(decision.reason).toBe('jobs-remain');
  });

  it('정리 호출이 실패하면 다음 사용자를 시작하지 않는다', () => {
    const decision = decideCleanup({
      ...base,
      clearErrorMessage: '이미 진행 중인 출력이 있어요.',
    });

    expect(decision.canStartNext).toBe(false);
    expect(decision.reason).toBe('clear-failed');
    expect(decision.message).toContain('이미 진행 중인 출력이 있어요.');
  });

  it('호스트가 다시 시작됐으면(캐시가 빈 새 인스턴스) 시작할 수 있다', () => {
    const decision = decideCleanup({
      ...base,
      clearErrorMessage: '연결 실패',
      currentHostInstanceId: 'host-b',
    });

    expect(decision.canStartNext).toBe(true);
    expect(decision.reason).toBe('host-restarted');
  });

  it('결과가 남았어도 호스트가 바뀌었으면 시작할 수 있다', () => {
    const decision = decideCleanup({
      ...base,
      clear: { clearedJobs: 0, remainingJobs: 3 },
      currentHostInstanceId: 'host-b',
    });

    expect(decision.canStartNext).toBe(true);
    expect(decision.reason).toBe('host-restarted');
  });

  it('호스트 상태를 읽지 못하면 잠금이 풀린 것으로 보지 않는다', () => {
    const decision = decideCleanup({
      ...base,
      clearErrorMessage: '연결 실패',
      currentHostInstanceId: null,
    });

    expect(decision.canStartNext).toBe(false);
    expect(decision.reason).toBe('clear-failed');
  });
});
