import type { StripLayout } from '@ysfourcut/imaging';
import { DITHER_LABELS_KO } from '@ysfourcut/imaging';
import { Button } from '../ui/Button';
import { SegmentedControl, SliderField, TicketPanel } from '../ui/parts';
import { CAPTION_MAX_LENGTH, type CutSlot, type EditSettings } from './types';

const FRAME_OPTIONS = [
  { value: 'basic', label: '기본 프레임' },
  { value: 'receipt', label: '영수증 프레임' },
] as const;

const DITHER_OPTIONS = [
  { value: 'floyd-steinberg', label: DITHER_LABELS_KO['floyd-steinberg'] },
  { value: 'ordered', label: DITHER_LABELS_KO.ordered },
] as const;

/** 09번 합성 결과. 미리보기·PNG 저장·서버 전달이 모두 이 비트맵 하나를 쓴다. */
export type StripPreview = {
  /** 최종 1비트 페이지를 PNG로 인코딩한 blob URL */
  url: string;
  layout: StripLayout;
};

/**
 * 편집 화면. 왼쪽은 **최종 인쇄 비트맵 그대로의 미리보기**, 오른쪽은 조절.
 * DOM을 캡처해 만들지 않는다. 결과 화면의 서버 PNG는 이 비트맵을 받아 만든다.
 */
export function EditScreen({
  slots,
  settings,
  sessionDate,
  preview,
  isComposing = false,
  previewMessage,
  printReady,
  printBlockedReason,
  busy = false,
  onChange,
  onRetakeCut,
  onRetakeAll,
  onSavePng,
  onPrint,
}: {
  slots: CutSlot[];
  settings: EditSettings;
  /** 촬영 세션 시작 시 고정한 날짜(로컬 기준, YYYY-MM-DD). 재인쇄 때 바뀌지 않는다. */
  sessionDate: string;
  /** 합성이 끝난 최종 비트맵. 없으면 준비 중이거나 만들 수 없는 상태다. */
  preview: StripPreview | null;
  isComposing?: boolean;
  /** 미리보기를 못 만드는 이유나 진행 안내 */
  previewMessage?: string;
  printReady: boolean;
  printBlockedReason?: string;
  /** 전송 중에는 연타·설정 변경을 막는다 */
  busy?: boolean;
  onChange: (next: EditSettings) => void;
  onRetakeCut: (index: number) => void;
  onRetakeAll: () => void;
  onSavePng: () => void;
  onPrint: () => void;
}) {
  const percent = (value: number, total: number) => `${(value / total) * 100}%`;

  return (
    <div className="edit">
      <figure className={`edit__strip edit__strip--${settings.frame}`} aria-label="네컷 편집 미리보기">
        {preview ? (
          <div className="edit__stage">
            <img className="edit__stage-image" src={preview.url} alt="최종 인쇄 미리보기 네컷" />
            {preview.layout.cuts.map((cut, index) => (
              <button
                key={slots[index]?.index ?? index + 1}
                type="button"
                className="edit__retake"
                aria-label={`${index + 1}컷 다시 찍기`}
                disabled={busy}
                style={{
                  top: percent(cut.y + cut.height, preview.layout.heightDots),
                  right: percent(preview.layout.widthDots - (cut.x + cut.width), preview.layout.widthDots),
                }}
                onClick={() => onRetakeCut(slots[index]?.index ?? index + 1)}
              >
                다시 찍기
              </button>
            ))}
          </div>
        ) : (
          <div className="edit__stage edit__stage--empty" role="status">
            <span>{previewMessage ?? (isComposing ? '네컷을 만드는 중이에요…' : '미리보기를 준비하고 있어요.')}</span>
          </div>
        )}
        <figcaption className="edit__caption-preview">
          <span>{sessionDate}</span>
          <span className="edit__caption-date">
            {preview
              ? `${preview.layout.widthDots} × ${preview.layout.heightDots} dot`
              : isComposing
                ? '만드는 중'
                : '대기'}
          </span>
        </figcaption>
      </figure>

      <div className="edit__controls">
        <TicketPanel title="꾸미기">
          <SegmentedControl
            label="프레임"
            options={FRAME_OPTIONS}
            value={settings.frame}
            disabled={busy}
            onChange={(frame) => onChange({ ...settings, frame })}
          />
          <SliderField
            label="밝기"
            min={-100}
            max={100}
            value={settings.brightness}
            displayValue={settings.brightness > 0 ? `+${settings.brightness}` : `${settings.brightness}`}
            disabled={busy}
            onChange={(brightness) => onChange({ ...settings, brightness })}
          />
          <SliderField
            label="대비"
            min={-100}
            max={100}
            value={settings.contrast}
            displayValue={settings.contrast > 0 ? `+${settings.contrast}` : `${settings.contrast}`}
            disabled={busy}
            onChange={(contrast) => onChange({ ...settings, contrast })}
          />
          <SegmentedControl
            label="사진 표현"
            options={DITHER_OPTIONS}
            value={settings.dither}
            disabled={busy}
            onChange={(dither) => onChange({ ...settings, dither })}
          />
          <div className="edit__caption-field">
            <span aria-hidden="true">하단 문구</span>
            <input
              type="text"
              aria-label="하단 문구"
              maxLength={CAPTION_MAX_LENGTH}
              value={settings.caption}
              placeholder="짧은 문구 (최대 24자)"
              disabled={busy}
              onChange={(event) => onChange({ ...settings, caption: event.target.value })}
            />
            <span className="edit__caption-count" aria-hidden="true">
              {settings.caption.length} / {CAPTION_MAX_LENGTH}
            </span>
          </div>
          <p className="edit__note">
            미리보기 스트립이 실제로 보낼 흑백 이미지예요. 종이 느낌은 결과 화면의 서버 영수증에서 확인해요.
          </p>
        </TicketPanel>

        <div className="edit__actions">
          <Button variant="quiet" disabled={busy} onClick={onRetakeAll}>
            전체 다시 찍기
          </Button>
          <Button variant="quiet" disabled={busy || preview === null} onClick={onSavePng}>
            PNG로 저장
          </Button>
          <Button
            variant="primary"
            size="lg"
            disabled={busy || !printReady || preview === null}
            onClick={onPrint}
            aria-describedby={!printReady ? 'edit-print-blocked' : undefined}
          >
            {busy ? '보내는 중…' : '가상 출력'}
          </Button>
        </div>
        {!printReady && (
          <p id="edit-print-blocked" className="edit__blocked" role="status">
            {printBlockedReason ?? '지금은 출력할 수 없어요. 촬영·저장은 가능해요.'}
          </p>
        )}
      </div>
    </div>
  );
}
