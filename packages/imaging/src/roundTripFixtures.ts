// 서버(03번 렌더러)와의 비트 계약을 실제로 맞춰 보기 위한 인공 fixture.
// 개인 사진을 쓰지 않는다. 여기서 만든 값을 그대로 .NET 시험이 읽어 픽셀을 비교한다.
// 갱신: `YSFOURCUT_UPDATE_FIXTURES=1 npm run test --workspace packages/imaging`

import { composeStripPage, type TextMask } from './compose';
import { ditherFloydSteinberg, ditherOrdered } from './dither';
import { createStripLayout } from './layout';
import { fillRect, createMonoPage, type MonoPage } from './page';
import { rgbaToGrayscale } from './tone';
import { packMonoBitmap, toBase64 } from './index';

export type RoundTripFixture = {
  name: string;
  /** 무엇을 확인하는 fixture인지 */
  purpose: string;
  widthDots: number;
  heightDots: number;
  strideBytes: number;
  bitOrder: 'msb-first';
  blackBit: 1;
  dataBase64: string;
  /** 기대 픽셀. '#' = 검정, '.' = 흰색. 위에서 아래로 한 줄씩. */
  expectedRows: string[];
};

function toRows(pixels: Uint8Array, width: number, height: number): string[] {
  const rows: string[] = [];
  for (let y = 0; y < height; y += 1) {
    let row = '';
    for (let x = 0; x < width; x += 1) {
      row += pixels[y * width + x] ? '#' : '.';
    }
    rows.push(row);
  }
  return rows;
}

function fixture(name: string, purpose: string, pixels: Uint8Array, width: number, height: number): RoundTripFixture {
  const bitmap = packMonoBitmap(pixels, width, height);
  return {
    name,
    purpose,
    widthDots: bitmap.widthDots,
    heightDots: bitmap.heightDots,
    strideBytes: bitmap.strideBytes,
    bitOrder: 'msb-first',
    blackBit: 1,
    dataBase64: toBase64(bitmap.data),
    expectedRows: toRows(pixels, width, height),
  };
}

function constantGray(width: number, height: number, value: number): Uint8Array {
  return new Uint8Array(width * height).fill(value);
}

function gradientGray(width: number, height: number): Uint8Array {
  const gray = new Uint8Array(width * height);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      gray[y * width + x] = Math.round((x / Math.max(1, width - 1)) * 255);
    }
  }
  return gray;
}

/** 왼쪽 절반은 완전 투명(RGB는 검정), 오른쪽 절반은 불투명 검정. */
function transparentBackgroundRgba(width: number, height: number): Uint8Array {
  const rgba = new Uint8Array(width * height * 4);
  for (let y = 0; y < height; y += 1) {
    for (let x = 0; x < width; x += 1) {
      const offset = (y * width + x) * 4;
      rgba[offset] = 0;
      rgba[offset + 1] = 0;
      rgba[offset + 2] = 0;
      rgba[offset + 3] = x < width / 2 ? 0 : 255;
    }
  }
  return rgba;
}

/** 상하 방향·MSB-first·마지막 열을 한 번에 드러내는 홀수 폭 패턴. */
function directionalOddWidth(width: number, height: number): Uint8Array {
  const pixels = new Uint8Array(width * height);
  pixels.fill(1, 0, width); // 첫 행 전부 검정, 마지막 행은 흰색으로 남긴다
  for (let y = 1; y < height; y += 1) {
    pixels[y * width] = y % 2 === 1 ? 1 : 0; // x=0 (0x80)
    pixels[y * width + 7] = y === 2 ? 1 : 0; // x=7 (0x01)
    pixels[y * width + width - 1] = y === 3 ? 1 : 0; // 마지막 열(패딩 직전)
  }
  return pixels;
}

/** 좁은 폭에서 만든 실제 네컷 페이지. 사진 → 프레임 → 글자 순서를 그대로 거친다. */
function miniStripPage(): MonoPage {
  const layout = createStripLayout({ contentWidthDots: 96, frame: 'receipt' });
  const { photoWidthDots: w, photoHeightDots: h } = layout;

  const photoBits = [
    ditherFloydSteinberg(constantGray(w, h, 0), w, h),
    ditherFloydSteinberg(constantGray(w, h, 255), w, h),
    ditherFloydSteinberg(constantGray(w, h, 128), w, h),
    ditherOrdered(gradientGray(w, h), w, h),
  ];

  // 글자 대신 같은 자리에 인공 마스크를 얹는다(브라우저 폰트에 의존하지 않기 위해).
  const maskPage = createMonoPage(layout.footerTextRect.width, layout.footerTextRect.height);
  fillRect(maskPage, 2, 1, Math.max(1, maskPage.widthDots - 4), Math.max(1, maskPage.heightDots - 3), 1);
  const textMask: TextMask = {
    widthDots: maskPage.widthDots,
    heightDots: maskPage.heightDots,
    bits: maskPage.pixels,
    x: layout.footerTextRect.x,
    y: layout.footerTextRect.y,
  };

  return composeStripPage({ layout, photoBits, textMasks: [textMask] });
}

/** 결정적인 fixture 모음. 같은 코드면 항상 같은 값이 나온다. */
export function buildRoundTripFixtures(): RoundTripFixture[] {
  const black = new Uint8Array(16 * 8).fill(1);
  const white = new Uint8Array(16 * 8);

  const grayFlat = ditherFloydSteinberg(constantGray(13, 9, 128), 13, 9);
  const grayOrdered = ditherOrdered(gradientGray(17, 12), 17, 12);

  const transparentGray = rgbaToGrayscale(transparentBackgroundRgba(12, 6), 12 * 6);
  const transparent = ditherFloydSteinberg(transparentGray, 12, 6);

  const odd = directionalOddWidth(9, 5);
  const strip = miniStripPage();

  return [
    fixture('all-black-16x8', '전면 검정: 모든 비트가 1이고 stride가 정확히 채워진다', black, 16, 8),
    fixture('all-white-16x8', '전면 흰색: 모든 비트가 0이다', white, 16, 8),
    fixture('gray-50-floyd-13x9', '중간 회색을 Floyd–Steinberg로 디더링한 홀수 폭', grayFlat, 13, 9),
    fixture('gray-gradient-ordered-17x12', '그라데이션을 ordered로 디더링한 홀수 폭', grayOrdered, 17, 12),
    fixture('transparent-background-12x6', '투명 배경은 흰 종이로 확정된다(왼쪽 절반 흰색)', transparent, 12, 6),
    fixture('odd-width-9x5-directional', '홀수 폭·MSB-first·상하 방향·흰 padding', odd, 9, 5),
    fixture('strip-mini-96', '좁은 폭의 실제 네컷 합성 결과(사진→프레임→글자)', strip.pixels, strip.widthDots, strip.heightDots),
  ];
}

/** 저장용 JSON 문자열. .NET 시험이 읽는 파일과 정확히 같은 내용이다. */
export function serializeRoundTripFixtures(): string {
  const fixtures = buildRoundTripFixtures();
  return `${JSON.stringify(
    {
      note: '인공 fixture. packages/imaging의 pack 결과이며 개인 사진이 아니다. 갱신: YSFOURCUT_UPDATE_FIXTURES=1 npm run test --workspace packages/imaging',
      strideRule: 'ceil(widthDots / 8)',
      fixtures,
    },
    null,
    2,
  )}\n`;
}
