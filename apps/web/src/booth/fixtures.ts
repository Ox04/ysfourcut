// 개발 미리보기 전용 인공 샘플. 실제 사진·촬영 결과가 아니며 서버로 보내지 않는다.
// 결과 화면의 "출력 성공" 상태는 fixture로 만들지 않는다 — 실제 서버 PNG가 있어야만 보여 준다.

import type { CutSlot } from './types';

/** 4:3 인공 샘플 컷. 외부 자산 없이 SVG 데이터 URL로 만든다. */
export function sampleCutImage(index: number): string {
  const hues = [36, 28, 42, 22];
  const hue = hues[(index - 1) % hues.length];
  const svg = `
<svg xmlns="http://www.w3.org/2000/svg" width="440" height="330" viewBox="0 0 440 330">
  <defs>
    <linearGradient id="g" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="hsl(${hue} 22% 78%)"/>
      <stop offset="1" stop-color="hsl(${hue} 18% 58%)"/>
    </linearGradient>
  </defs>
  <rect width="440" height="330" fill="url(#g)"/>
  <circle cx="220" cy="132" r="52" fill="hsl(${hue} 20% 88%)"/>
  <rect x="150" y="196" width="140" height="96" rx="46" fill="hsl(${hue} 20% 88%)"/>
  <text x="20" y="308" font-family="sans-serif" font-size="26" fill="hsl(${hue} 25% 34%)">샘플 ${index}</text>
</svg>`;
  return `data:image/svg+xml;charset=utf-8,${encodeURIComponent(svg)}`;
}

export function sampleSlots(filledCount: number): CutSlot[] {
  return [1, 2, 3, 4].map((index) => ({
    index,
    imageUrl: index <= filledCount ? sampleCutImage(index) : null,
  }));
}

/** 긴 한국어 문구가 잘리지 않는지 확인하기 위한 미리보기 값 */
export const LONG_CAPTION_SAMPLE = '스물네 글자까지 들어가는 아주 긴 문구 예시';
export const LONG_FAILURE_SAMPLE =
  '가상 영수증을 만드는 데 너무 오래 걸렸어요. 잠시 뒤 다시 시도해 주세요. 문제가 계속되면 로컬 프로그램을 다시 시작해 주세요.';
