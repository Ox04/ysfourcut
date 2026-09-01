import { describe, expect, it } from 'vitest';
import { IDLE_TIMEOUT_MS, resolveIdleTimeoutMs } from './useIdleTimeout';

describe('resolveIdleTimeoutMs — 유휴 정리 시간', () => {
  it('기본은 120초다', () => {
    expect(IDLE_TIMEOUT_MS).toBe(120_000);
    expect(resolveIdleTimeoutMs('', true)).toBe(120_000);
    expect(resolveIdleTimeoutMs('?idleSeconds=5', false)).toBe(120_000);
  });

  it('개발 실행에서만 실측용으로 줄일 수 있다', () => {
    expect(resolveIdleTimeoutMs('?idleSeconds=6', true)).toBe(6_000);
  });

  it('범위 밖 값과 숫자가 아닌 값은 안전한 범위로 맞춘다', () => {
    expect(resolveIdleTimeoutMs('?idleSeconds=1', true)).toBe(5_000);
    expect(resolveIdleTimeoutMs('?idleSeconds=99999', true)).toBe(600_000);
    expect(resolveIdleTimeoutMs('?idleSeconds=abc', true)).toBe(120_000);
  });
});
