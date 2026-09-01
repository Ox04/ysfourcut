import { describe, expect, it } from 'vitest';
import { ERROR_CODES, ERROR_MESSAGES_KO, describeError, strideBytesFor } from './contract';

describe('오류 코드 계약', () => {
  it('모든 코드에 한국어 안내가 있다', () => {
    for (const code of ERROR_CODES) {
      expect(ERROR_MESSAGES_KO[code], code).toBeTruthy();
    }
  });

  it('안내에 사진·코드·쿠키 같은 민감 값을 넣지 않는다', () => {
    for (const message of Object.values(ERROR_MESSAGES_KO)) {
      expect(message).not.toMatch(/base64|쿠키|cookie|token/i);
    }
  });

  it('모르는 코드는 서버 메시지를 그대로 쓴다', () => {
    expect(describeError({ code: 'SESSION_REQUIRED', message: '서버 문구' })).toBe(
      ERROR_MESSAGES_KO.SESSION_REQUIRED,
    );
    expect(describeError({ code: 'SOMETHING_NEW', message: '서버 문구' })).toBe('서버 문구');
    expect(describeError(null)).toBeTruthy();
  });
});

describe('strideBytesFor', () => {
  it('ceil(widthDots / 8)을 따른다', () => {
    expect(strideBytesFor(1)).toBe(1);
    expect(strideBytesFor(8)).toBe(1);
    expect(strideBytesFor(9)).toBe(2);
    expect(strideBytesFor(576)).toBe(72);
    expect(strideBytesFor(100)).toBe(13);
  });
});
