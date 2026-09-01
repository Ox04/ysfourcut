// 유휴 정리 타이머. 기본 120초(SERVICE_DESIGN 4절)이며 사용자의 조작마다 다시 시작한다.
// 출력이 끝나지 않았거나 결과가 불명확한 동안에는 호출자가 enabled=false로 꺼 둔다.

import { useEffect, useRef } from 'react';

export const IDLE_TIMEOUT_MS = 120_000;

/** 개발 실행에서만 `?idleSeconds=` 로 타이머를 줄여 정리 동작을 실측한다. */
export function resolveIdleTimeoutMs(search: string, isDev: boolean): number {
  if (!isDev) return IDLE_TIMEOUT_MS;

  const raw = new URLSearchParams(search).get('idleSeconds');
  if (raw === null) return IDLE_TIMEOUT_MS;

  const seconds = Number(raw);
  if (!Number.isFinite(seconds)) return IDLE_TIMEOUT_MS;

  return Math.min(600, Math.max(5, Math.round(seconds))) * 1000;
}

const ACTIVITY_EVENTS = ['pointerdown', 'keydown', 'touchstart', 'wheel'] as const;

export function useIdleTimeout(enabled: boolean, timeoutMs: number, onIdle: () => void): void {
  const onIdleRef = useRef(onIdle);

  useEffect(() => {
    onIdleRef.current = onIdle;
  }, [onIdle]);

  useEffect(() => {
    if (!enabled) return;

    let timer: ReturnType<typeof setTimeout> | null = null;

    const arm = () => {
      if (timer) clearTimeout(timer);
      timer = setTimeout(() => onIdleRef.current(), timeoutMs);
    };

    arm();
    ACTIVITY_EVENTS.forEach((name) => window.addEventListener(name, arm, { passive: true }));

    return () => {
      if (timer) clearTimeout(timer);
      ACTIVITY_EVENTS.forEach((name) => window.removeEventListener(name, arm));
    };
  }, [enabled, timeoutMs]);
}
