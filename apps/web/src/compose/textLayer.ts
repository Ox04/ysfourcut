// 한글·날짜 글자를 목표 dot 크기의 캔버스에 그려 1비트 마스크로 만든다.
// 폰트에 의존하므로 메인 스레드에서만 실행하고, 결과 마스크만 합성에 넘긴다.
// 안티앨리어싱 결과는 여기서 흑백으로 확정한다.

import type { StripLayout, TextMask } from '@ysfourcut/imaging';
import { printFontStack } from './fonts';

/** 회색은 남기지 않는다. 이 값보다 어두우면 검정으로 확정한다. */
const TEXT_THRESHOLD = 160;

export type StripTextContent = {
  /** 머리말(브랜드 줄) */
  title: string;
  /** 하단 문구. 비어 있으면 그리지 않는다. */
  caption: string;
  /** 촬영 세션 시작 시 고정한 날짜 문자열 */
  dateText: string;
};

type Line = { text: string; fontDots: number };

function createContext(width: number, height: number): CanvasRenderingContext2D | null {
  const canvas = document.createElement('canvas');
  canvas.width = Math.max(1, width);
  canvas.height = Math.max(1, height);

  const context = canvas.getContext('2d', { willReadFrequently: true });
  if (!context) return null;

  context.fillStyle = '#fff';
  context.fillRect(0, 0, canvas.width, canvas.height);
  context.fillStyle = '#000';
  context.textAlign = 'center';
  context.textBaseline = 'middle';
  return context;
}

/** 칸 안에 들어가도록 글자 크기를 줄인다. 잘라내지 않는다. */
function fittedFontSize(
  context: CanvasRenderingContext2D,
  text: string,
  requestedDots: number,
  maxWidth: number,
): number {
  context.font = `700 ${requestedDots}px ${printFontStack()}`;
  const measured = context.measureText(text).width;
  if (measured <= maxWidth || measured === 0) return requestedDots;

  return Math.max(6, Math.floor(requestedDots * (maxWidth / measured)));
}

function toMask(context: CanvasRenderingContext2D, x: number, y: number): TextMask {
  const { width, height } = context.canvas;
  const { data } = context.getImageData(0, 0, width, height);
  const bits = new Uint8Array(width * height);

  for (let index = 0; index < bits.length; index += 1) {
    const offset = index * 4;
    const alpha = data[offset + 3] / 255;
    const luminance =
      (0.299 * data[offset] + 0.587 * data[offset + 1] + 0.114 * data[offset + 2]) * alpha +
      255 * (1 - alpha);
    bits[index] = luminance < TEXT_THRESHOLD ? 1 : 0;
  }

  return { widthDots: width, heightDots: height, bits, x, y };
}

/** 여러 줄을 세로 가운데로 모아 그린다. */
function drawLines(context: CanvasRenderingContext2D, lines: Line[], padding: number): void {
  const usable = context.canvas.width - padding * 2;
  const gap = Math.max(1, Math.round(lines[0].fontDots * 0.2));
  const sizes = lines.map((line) => fittedFontSize(context, line.text, line.fontDots, usable));
  const totalHeight = sizes.reduce((sum, size) => sum + size, 0) + gap * (lines.length - 1);

  let cursor = (context.canvas.height - totalHeight) / 2;
  lines.forEach((line, index) => {
    const size = sizes[index];
    context.font = `700 ${size}px ${printFontStack()}`;
    context.fillText(line.text, context.canvas.width / 2, cursor + size / 2, usable);
    cursor += size + gap;
  });
}

/**
 * 머리말·하단 문구·날짜 마스크를 만든다.
 * 좌표는 배치가 정한 글자 영역이며 구분선·사진과 겹치지 않는다.
 */
export function renderStripTextMasks(layout: StripLayout, content: StripTextContent): TextMask[] {
  const masks: TextMask[] = [];
  const padding = Math.max(1, Math.round(layout.ruleDots * 2));

  const header = createContext(layout.headerTextRect.width, layout.headerTextRect.height);
  if (header && content.title) {
    drawLines(header, [{ text: content.title, fontDots: layout.headerFontDots }], padding);
    masks.push(toMask(header, layout.headerTextRect.x, layout.headerTextRect.y));
  }

  const footerLines: Line[] = [];
  if (content.caption) {
    footerLines.push({ text: content.caption, fontDots: layout.captionFontDots });
  }
  footerLines.push({ text: content.dateText, fontDots: layout.dateFontDots });

  const footer = createContext(layout.footerTextRect.width, layout.footerTextRect.height);
  if (footer) {
    drawLines(footer, footerLines, padding);
    masks.push(toMask(footer, layout.footerTextRect.x, layout.footerTextRect.y));
  }

  return masks;
}
