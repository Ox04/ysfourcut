import { describe, expect, it, vi } from 'vitest';
import { pairingUrl, takeBootstrapCodeFromLocation } from './client';

type LocationLike = Pick<Location, 'hash' | 'pathname' | 'search'>;

function fakeHistory() {
  const calls: string[] = [];
  const history = {
    replaceState: vi.fn((_state: unknown, _title: string, url: string) => {
      calls.push(url);
    }),
  } as unknown as History;

  return { history, calls };
}

describe('takeBootstrapCodeFromLocation', () => {
  it('fragment의 코드를 꺼내고 주소에서 즉시 지운다', () => {
    const location: LocationLike = { hash: '#bootstrap=abc123', pathname: '/', search: '' };
    const { history, calls } = fakeHistory();

    const code = takeBootstrapCodeFromLocation(location as Location, history);

    expect(code).toBe('abc123');
    expect(calls).toEqual(['/']);
    // 지운 주소에는 코드가 남지 않는다.
    expect(calls[0]).not.toContain('abc123');
  });

  it('다른 fragment 값은 보존한다', () => {
    const location: LocationLike = { hash: '#bootstrap=abc&view=paper', pathname: '/print', search: '?x=1' };
    const { history, calls } = fakeHistory();

    expect(takeBootstrapCodeFromLocation(location as Location, history)).toBe('abc');
    expect(calls).toEqual(['/print?x=1#view=paper']);
  });

  it('코드가 없으면 주소를 건드리지 않는다', () => {
    const location: LocationLike = { hash: '', pathname: '/', search: '' };
    const { history, calls } = fakeHistory();

    expect(takeBootstrapCodeFromLocation(location as Location, history)).toBeNull();
    expect(calls).toHaveLength(0);
  });
});

describe('pairingUrl', () => {
  it('개발에서는 호스트 주소, 운영에서는 같은 출처를 쓴다', () => {
    expect(pairingUrl(true)).toBe('http://127.0.0.1:4317/pair');
    expect(pairingUrl(false)).toBe('/pair');
  });
});
