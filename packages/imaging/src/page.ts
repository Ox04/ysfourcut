// 최종 1비트 페이지와 그리기 프리미티브. 픽셀당 0(흰색)/1(검정)이고 행 우선이다.
// 안티앨리어싱이 없으므로 프레임·선은 항상 선명한 흑백이다.

export type MonoPage = {
  widthDots: number;
  heightDots: number;
  /** 길이 widthDots × heightDots. 1 = 검정. */
  pixels: Uint8Array;
};

/** 흰 종이 한 장. 넓은 검정 배경을 기본값으로 두지 않는다. */
export function createMonoPage(widthDots: number, heightDots: number): MonoPage {
  return { widthDots, heightDots, pixels: new Uint8Array(widthDots * heightDots) };
}

export function fillRect(
  page: MonoPage,
  x: number,
  y: number,
  width: number,
  height: number,
  value: 0 | 1 = 1,
): void {
  const x0 = Math.max(0, x);
  const y0 = Math.max(0, y);
  const x1 = Math.min(page.widthDots, x + width);
  const y1 = Math.min(page.heightDots, y + height);

  for (let py = y0; py < y1; py += 1) {
    page.pixels.fill(value, py * page.widthDots + x0, py * page.widthDots + x1);
  }
}

/** 안쪽으로 그리는 테두리. */
export function strokeRect(
  page: MonoPage,
  x: number,
  y: number,
  width: number,
  height: number,
  thickness: number,
): void {
  const t = Math.max(1, Math.round(thickness));
  fillRect(page, x, y, width, t);
  fillRect(page, x, y + height - t, width, t);
  fillRect(page, x, y, t, height);
  fillRect(page, x + width - t, y, t, height);
}

/** 가로 점선. 영수증 프레임의 절취선 모티프에 쓴다. */
export function dashedHLine(
  page: MonoPage,
  x: number,
  y: number,
  width: number,
  thickness: number,
  dash: number,
  gap: number,
): void {
  const step = Math.max(1, dash + gap);
  for (let offset = 0; offset < width; offset += step) {
    fillRect(page, x + offset, y, Math.min(dash, width - offset), Math.max(1, thickness));
  }
}

/** 사진 비트를 그대로 옮긴다(덮어쓰기). */
export function blitBits(
  page: MonoPage,
  bits: Uint8Array,
  width: number,
  height: number,
  x: number,
  y: number,
): void {
  for (let row = 0; row < height; row += 1) {
    const py = y + row;
    if (py < 0 || py >= page.heightDots) continue;
    for (let column = 0; column < width; column += 1) {
      const px = x + column;
      if (px < 0 || px >= page.widthDots) continue;
      page.pixels[py * page.widthDots + px] = bits[row * width + column] ? 1 : 0;
    }
  }
}

/** 글자 마스크를 얹는다(검정만 더한다). 사진 디더링 뒤에 호출한다. */
export function blitMask(
  page: MonoPage,
  mask: Uint8Array,
  width: number,
  height: number,
  x: number,
  y: number,
): void {
  for (let row = 0; row < height; row += 1) {
    const py = y + row;
    if (py < 0 || py >= page.heightDots) continue;
    for (let column = 0; column < width; column += 1) {
      if (!mask[row * width + column]) continue;
      const px = x + column;
      if (px < 0 || px >= page.widthDots) continue;
      page.pixels[py * page.widthDots + px] = 1;
    }
  }
}
