import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it } from 'vitest';
import { buildRoundTripFixtures, serializeRoundTripFixtures } from './roundTripFixtures';

// .NET 시험이 읽는 fixture 파일을 만드는 곳이자, 파일이 코드와 어긋나지 않는지 확인하는 곳이다.
// 갱신: `YSFOURCUT_UPDATE_FIXTURES=1 npm run test --workspace packages/imaging`
const FIXTURE_PATH = fileURLToPath(
  new URL('../../../tests/print-host/fixtures/web-bitmap-fixtures.json', import.meta.url),
);

describe('round-trip fixture', () => {
  it('.NET 시험이 읽는 파일이 지금 코드의 pack 결과와 같다', () => {
    const generated = serializeRoundTripFixtures();

    if (process.env.YSFOURCUT_UPDATE_FIXTURES) {
      writeFileSync(FIXTURE_PATH, generated, 'utf8');
    }

    expect(readFileSync(FIXTURE_PATH, 'utf8')).toBe(generated);
  });

  it('검정·흰색·회색·투명 배경·홀수 폭·네컷 합성을 모두 담는다', () => {
    const names = buildRoundTripFixtures().map((fixture) => fixture.name);

    expect(names).toEqual([
      'all-black-16x8',
      'all-white-16x8',
      'gray-50-floyd-13x9',
      'gray-gradient-ordered-17x12',
      'transparent-background-12x6',
      'odd-width-9x5-directional',
      'strip-mini-96',
    ]);
  });

  it('모든 fixture가 stride 계약과 흰 padding을 지킨다', () => {
    for (const fixture of buildRoundTripFixtures()) {
      expect(fixture.strideBytes).toBe(Math.ceil(fixture.widthDots / 8));
      expect(fixture.bitOrder).toBe('msb-first');
      expect(fixture.blackBit).toBe(1);
      expect(fixture.expectedRows).toHaveLength(fixture.heightDots);

      const data = Uint8Array.from(Buffer.from(fixture.dataBase64, 'base64'));
      expect(data.length).toBe(fixture.strideBytes * fixture.heightDots);

      const usedBits = fixture.widthDots % 8;
      if (usedBits !== 0) {
        const paddingMask = 0xff >> usedBits;
        for (let y = 0; y < fixture.heightDots; y += 1) {
          expect(data[y * fixture.strideBytes + fixture.strideBytes - 1] & paddingMask).toBe(0);
        }
      }

      // 기대 픽셀과 실제 비트가 같은지 여기서도 확인한다(.NET 쪽과 같은 규칙).
      fixture.expectedRows.forEach((row, y) => {
        expect(row).toHaveLength(fixture.widthDots);
        for (let x = 0; x < fixture.widthDots; x += 1) {
          const bit = (data[y * fixture.strideBytes + (x >> 3)] >> (7 - (x % 8))) & 1;
          expect(bit).toBe(row[x] === '#' ? 1 : 0);
        }
      });
    }
  });
});
