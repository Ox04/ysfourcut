import { useState } from 'react';

import { SegmentedControl } from '../ui/parts';
import type { ReceiptLayoutView } from '../api/contract';

export type ViewerMode = 'appearance' | 'paper' | 'content' | 'zoom';

const MODE_OPTIONS = [
  { value: 'appearance', label: '실물 느낌' },
  { value: 'paper', label: '용지 그대로' },
  { value: 'content', label: '원본' },
  { value: 'zoom', label: '픽셀 확대' },
] as const;

function toMm(dots: number, dpi: number): string {
  return ((dots * 25.4) / dpi).toFixed(1);
}

/**
 * 서버가 만든 PNG를 보여 주는 뷰어. CSS로 영수증을 다시 그리지 않는다 —
 * 이미지 원본을 1:1(또는 정수 배율)로 표시하고 받침 배경·치수만 곁들인다.
 * appearance는 투명 배경 + 그림자를 포함하므로 중간톤 받침 위에 올린다.
 */
export function ReceiptViewer({
  images,
  layout,
  animateEject = false,
}: {
  images: Partial<Record<'appearance' | 'content' | 'paper', string>>;
  layout: ReceiptLayoutView | null;
  /** rendered 직후 1회의 배출 표시 효과. 서버 완료 신호가 아니라 장식이다. */
  animateEject?: boolean;
}) {
  const [mode, setMode] = useState<ViewerMode>('appearance');
  const [ejectDone, setEjectDone] = useState(false);

  const imageKey = mode === 'zoom' ? 'content' : mode;
  const url = images[imageKey];
  const eject = animateEject && !ejectDone;

  return (
    <div className="viewer">
      <SegmentedControl label="보기 방식" options={MODE_OPTIONS} value={mode} onChange={setMode} />

      <div
        className={`viewer__stage viewer__stage--${mode}${eject ? ' viewer__stage--eject' : ''}`}
        onAnimationEnd={() => setEjectDone(true)}
      >
        {url ? (
          <div className={`viewer__scroll${mode === 'zoom' ? ' viewer__scroll--zoom' : ''}`}>
            <img
              className={`viewer__image viewer__image--${mode}`}
              src={url}
              alt={
                mode === 'appearance'
                  ? '서버가 만든 실물 느낌 가상 영수증'
                  : mode === 'paper'
                    ? '여백을 포함한 용지 전체'
                    : mode === 'zoom'
                      ? '원본 비트맵 4배 확대'
                      : '인쇄될 원본 비트맵'
              }
            />
          </div>
        ) : (
          <p className="viewer__missing" role="status">
            이 보기의 이미지가 없어요.
          </p>
        )}
      </div>

      {layout && (
        <dl className="viewer__dims">
          <div>
            <dt>용지</dt>
            <dd>
              {layout.paperWidthDots}×{layout.paperHeightDots}dot ·{' '}
              {toMm(layout.paperWidthDots, layout.dpiX)}×{toMm(layout.paperHeightDots, layout.dpiY)}mm
            </dd>
          </div>
          <div>
            <dt>내용</dt>
            <dd>
              {layout.contentWidthDots}×{layout.contentHeightDots}dot · 원점 (
              {layout.contentXDots}, {layout.contentYDots})
            </dd>
          </div>
          <div>
            <dt>해상도</dt>
            <dd>{layout.dpiX.toFixed(1)} DPI</dd>
          </div>
        </dl>
      )}
    </div>
  );
}
