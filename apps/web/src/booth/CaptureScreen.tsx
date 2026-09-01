import type { ReactNode } from 'react';
import { Button } from '../ui/Button';
import { Countdown, CutProgress } from '../ui/parts';
import type { CutSlot } from './types';

/**
 * 촬영 화면. preview 자리에 08번이 실제 카메라(<video>)를 꽂는다.
 * 카운트다운·플래시는 표시일 뿐 촬영 로직은 호출자 소유다.
 */
export function CaptureScreen({
  preview,
  slots,
  currentCut,
  countdownSeconds,
  isFlashing = false,
  captureError = null,
  onCancel,
}: {
  /** 카메라 미리보기 노드. 크롭·반전은 카메라 쪽이 책임진다. */
  preview: ReactNode;
  slots: CutSlot[];
  /** 지금 찍는 컷 (1~4) */
  currentCut: number;
  /** null이면 카운트다운 없음 */
  countdownSeconds: number | null;
  /** 셔터 직후 짧은 피드백 */
  isFlashing?: boolean;
  /** 프레임을 받지 못하는 등 촬영이 실패했을 때의 한국어 안내 */
  captureError?: string | null;
  onCancel: () => void;
}) {
  return (
    <div className="capture">
      <div className="capture__stage">
        <div className="capture__preview" role="group" aria-label="카메라 미리보기">
          {preview}
          {isFlashing && <div className="capture__flash" aria-hidden="true" />}
          {countdownSeconds !== null && (
            <div className="capture__countdown">
              <Countdown seconds={countdownSeconds} />
            </div>
          )}
        </div>

        <div className="capture__bar">
          <CutProgress current={currentCut} total={slots.length} />
          <p className={`capture__hint${captureError ? ' capture__hint--error' : ''}`} role="status">
            {captureError ??
              (countdownSeconds !== null
                ? `${countdownSeconds}초 뒤에 찍어요. 카메라를 봐 주세요!`
                : `${currentCut}컷째 사진을 저장하는 중…`)}
          </p>
          <Button variant="quiet" onClick={onCancel}>
            처음으로
          </Button>
        </div>
      </div>

      <ol className="capture__slots" aria-label="찍은 컷">
        {slots.map((slot) => (
          <li
            key={slot.index}
            className={`capture__slot${slot.index === currentCut ? ' is-current' : ''}`}
          >
            {slot.imageUrl ? (
              <img src={slot.imageUrl} alt={`${slot.index}컷째 사진`} />
            ) : (
              <span className="capture__slot-empty" aria-hidden="true">
                {slot.index}
              </span>
            )}
          </li>
        ))}
      </ol>
    </div>
  );
}
