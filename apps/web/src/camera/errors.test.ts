import { describe, expect, it } from 'vitest';
import { cameraErrorInfo, describeCameraError } from './errors';

describe('describeCameraError', () => {
  it.each([
    ['NotAllowedError', 'permission-denied'],
    ['PermissionDeniedError', 'permission-denied'],
    ['SecurityError', 'permission-denied'],
    ['NotFoundError', 'no-device'],
    ['DevicesNotFoundError', 'no-device'],
    ['NotReadableError', 'device-busy'],
    ['TrackStartError', 'device-busy'],
    ['OverconstrainedError', 'constraints'],
  ])('%s → %s', (name, code) => {
    const info = describeCameraError(Object.assign(new Error('raw'), { name }));

    expect(info.code).toBe(code);
    expect(info.message.length).toBeGreaterThan(0);
    expect(info.recovery.length).toBeGreaterThan(0);
  });

  it('모르는 오류는 unknown으로 다루고 다시 시도를 제안한다', () => {
    expect(describeCameraError(new Error('boom')).code).toBe('unknown');
    expect(describeCameraError(null).code).toBe('unknown');
    expect(describeCameraError(undefined).canRetry).toBe(true);
  });

  it('원본 오류 메시지를 화면 문구로 쓰지 않는다', () => {
    const info = describeCameraError(
      Object.assign(new Error('Requested device not found: /dev/video9'), { name: 'NotFoundError' }),
    );

    expect(info.message).not.toContain('/dev/video9');
    expect(info.recovery).not.toContain('/dev/video9');
  });

  it('모든 코드에 한국어 안내와 복구 동작이 있다', () => {
    const codes = [
      'permission-denied',
      'no-device',
      'device-busy',
      'constraints',
      'insecure-context',
      'unsupported',
      'unknown',
    ] as const;

    for (const code of codes) {
      const info = cameraErrorInfo(code);
      expect(info.code).toBe(code);
      expect(info.message).toMatch(/[가-힣]/);
      expect(info.recovery).toMatch(/[가-힣]/);
    }

    // 브라우저·주소 문제는 다시 시도해도 소용없으므로 재시도를 권하지 않는다.
    expect(cameraErrorInfo('unsupported').canRetry).toBe(false);
    expect(cameraErrorInfo('insecure-context').canRetry).toBe(false);
  });
});
