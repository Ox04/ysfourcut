import type { ReactNode } from 'react';

// 작은 표시용 프리미티브 모음. 로직 없음. 스타일은 styles/booth.css.

export type BadgeTone = 'virtual' | 'ok' | 'warn' | 'danger' | 'neutral';

/** 상시 상태 표시. 가상 모드 배지는 모든 화면에서 이 컴포넌트를 쓴다. */
export function StatusBadge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  return (
    <span className={`badge badge--${tone}`}>
      <span className="badge__dot" aria-hidden="true" />
      {children}
    </span>
  );
}

/** 종이 패널. perforated는 절취선 모티프(윗변 점선)를 붙인다. */
export function TicketPanel({
  title,
  perforated = false,
  children,
  className,
}: {
  title?: ReactNode;
  perforated?: boolean;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section
      className={['panel', perforated ? 'panel--perforated' : '', className]
        .filter(Boolean)
        .join(' ')}
    >
      {title !== undefined && <h2 className="panel__title">{title}</h2>}
      {children}
    </section>
  );
}

/** `2 / 4` 촬영 진행 표시. 숫자가 정보의 본체이고 점은 장식이다. */
export function CutProgress({ current, total = 4 }: { current: number; total?: number }) {
  return (
    <div className="cut-progress">
      <span className="cut-progress__count">
        <span className="sr-only">{`${total}컷 중 `}</span>
        {current} <span className="cut-progress__slash" aria-hidden="true">/</span>
        <span aria-hidden="true"> {total}</span>
        <span className="sr-only">{`컷째`}</span>
      </span>
      <span className="cut-progress__dots" aria-hidden="true">
        {Array.from({ length: total }, (_, index) => (
          <span
            key={index}
            className={[
              'cut-progress__dot',
              index + 1 < current ? 'is-done' : '',
              index + 1 === current ? 'is-current' : '',
            ]
              .filter(Boolean)
              .join(' ')}
          />
        ))}
      </span>
    </div>
  );
}

/** 촬영 카운트다운 표시. 시각 장식이며, 남은 초의 낭독은 촬영 화면의 안내 문구(role=status) 하나가 맡는다. */
export function Countdown({ seconds }: { seconds: number }) {
  return (
    <div className="countdown" aria-hidden="true">
      <svg className="countdown__ring" viewBox="0 0 120 120">
        <circle className="countdown__track" cx="60" cy="60" r="52" />
        <circle className="countdown__value" cx="60" cy="60" r="52" data-seconds={seconds} />
      </svg>
      <span className="countdown__number">{seconds}</span>
    </div>
  );
}

export type SegmentOption<T extends string> = { value: T; label: string };

/** 프레임 선택·결과 보기 모드 등 소수 선택지. 버튼 그룹이라 키보드로 각각 접근한다. */
export function SegmentedControl<T extends string>({
  label,
  options,
  value,
  disabled = false,
  onChange,
}: {
  label: string;
  options: readonly SegmentOption<T>[];
  value: T;
  disabled?: boolean;
  onChange: (next: T) => void;
}) {
  return (
    <div className="segmented" role="group" aria-label={label}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          className="segmented__option"
          aria-pressed={option.value === value}
          disabled={disabled}
          onClick={() => onChange(option.value)}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

/** 밝기·대비 슬라이더 행. 접근 이름은 라벨만 쓰고 값은 슬라이더의 value로 전달한다. */
export function SliderField({
  label,
  value,
  min,
  max,
  displayValue,
  disabled = false,
  onChange,
}: {
  label: string;
  value: number;
  min: number;
  max: number;
  displayValue: string;
  disabled?: boolean;
  onChange: (next: number) => void;
}) {
  return (
    <div className="slider-field">
      <span className="slider-field__label" aria-hidden="true">
        {label}
      </span>
      <input
        type="range"
        aria-label={label}
        min={min}
        max={max}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(Number(event.target.value))}
      />
      <span className="slider-field__value" aria-hidden="true">
        {displayValue}
      </span>
    </div>
  );
}
