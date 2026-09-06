import type { ComponentPropsWithoutRef, ReactNode } from 'react';

/* 디자인 시스템 v2 컴포넌트. 스타일은 ds.css의 시맨틱 토큰을 Tailwind 유틸리티로만 참조한다.
   기존 ui/(07번)와 별도 — 데모 검수 통과 후 실제 화면 적용 단계에서 교체한다. */

function cx(...parts: Array<string | false | undefined>) {
  return parts.filter(Boolean).join(' ');
}

/* ── Button ──────────────────────────────────────────────────────── */

type ButtonProps = ComponentPropsWithoutRef<'button'> & {
  /** primary = 잉크 단색 기본 동작, accent = 셔터 레드(촬영·출력 핵심 순간 전용),
      secondary = 잉크 외곽선, ghost = 저강조 */
  variant?: 'primary' | 'accent' | 'secondary' | 'ghost';
  /** lg = 촬영 시작·가상 출력급(60px), md = 일반 동작(48px) */
  size?: 'lg' | 'md';
};

const buttonVariant: Record<NonNullable<ButtonProps['variant']>, string> = {
  primary: 'bg-ink text-on-ink hover:bg-ink-hover disabled:bg-sunken disabled:text-disabled',
  accent: 'bg-accent text-on-accent hover:bg-accent-hover disabled:bg-sunken disabled:text-disabled',
  secondary:
    'border border-ink text-text hover:bg-sunken disabled:border-line disabled:text-disabled',
  ghost: 'text-soft hover:bg-sunken hover:text-text disabled:text-disabled',
};

export function Button({ variant = 'secondary', size = 'md', className, ...rest }: ButtonProps) {
  return (
    <button
      type="button"
      className={cx(
        'inline-flex select-none items-center justify-center gap-2 font-semibold transition-colors duration-100 disabled:cursor-not-allowed',
        size === 'lg' ? 'min-h-15 px-7 text-body-lg' : 'min-h-12 px-5 text-body',
        buttonVariant[variant],
        className,
      )}
      {...rest}
    />
  );
}

/* ── StatusBadge ─────────────────────────────────────────────────── */

export type BadgeTone = 'virtual' | 'ok' | 'warn' | 'danger' | 'neutral';

const badgeTone: Record<BadgeTone, string> = {
  virtual: 'border-accent bg-accent-tint text-accent',
  ok: 'border-ok text-ok',
  warn: 'border-warn text-warn',
  danger: 'border-danger bg-danger-tint text-danger',
  neutral: 'border-line-strong text-soft',
};

export function StatusBadge({ tone, children }: { tone: BadgeTone; children: ReactNode }) {
  return (
    <span
      className={cx(
        'inline-flex items-center gap-1.5 border px-2 py-1 text-label font-medium',
        badgeTone[tone],
      )}
    >
      <span className="size-1.5 bg-current" aria-hidden="true" />
      {children}
    </span>
  );
}

/* ── Panel ───────────────────────────────────────────────────────── */

/** 평면 패널. 그림자 없이 1px 경계선으로 구획한다.
    perforated는 영수증 절취선 모티프(굵은 점선 윗변) — 유지하는 유일한 장식이다. */
export function Panel({
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
    <section className={cx('border border-line bg-layer', className)}>
      {perforated && (
        <div className="mx-4 border-t-2 border-dashed border-line-strong" aria-hidden="true" />
      )}
      <div className="flex flex-col gap-4 p-6">
        {title !== undefined && <h2 className="text-heading font-bold">{title}</h2>}
        {children}
      </div>
    </section>
  );
}

/* ── SegmentedControl ────────────────────────────────────────────── */

export type SegmentOption<T extends string> = { value: T; label: string };

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
    <div className="inline-flex border border-ink" role="group" aria-label={label}>
      {options.map((option, index) => (
        <button
          key={option.value}
          type="button"
          className={cx(
            'min-h-12 px-4 text-label font-medium transition-colors duration-100',
            'aria-pressed:bg-ink aria-pressed:text-on-ink',
            'not-aria-pressed:text-soft not-aria-pressed:hover:bg-sunken',
            'disabled:cursor-not-allowed disabled:text-disabled',
            index > 0 && 'border-l border-l-ink',
          )}
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

/* ── SliderField ─────────────────────────────────────────────────── */

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
    <div className="flex min-h-12 items-center gap-4">
      <span className="w-16 shrink-0 text-label text-soft" aria-hidden="true">
        {label}
      </span>
      <input
        type="range"
        className="w-full accent-accent disabled:opacity-40"
        aria-label={label}
        min={min}
        max={max}
        value={value}
        disabled={disabled}
        onChange={(event) => onChange(Number(event.target.value))}
      />
      <span className="w-12 shrink-0 text-right font-mono text-label" aria-hidden="true">
        {displayValue}
      </span>
    </div>
  );
}

/* ── TextField ───────────────────────────────────────────────────── */

export function TextField({
  label,
  hint,
  className,
  ...rest
}: ComponentPropsWithoutRef<'input'> & { label: string; hint?: string }) {
  return (
    <label className={cx('flex flex-col gap-1.5', className)}>
      <span className="text-label font-medium text-soft">{label}</span>
      <input
        className="min-h-12 border border-line-strong bg-field px-4 text-body placeholder:text-disabled disabled:border-line disabled:text-disabled"
        {...rest}
      />
      {hint && <span className="text-caption text-soft">{hint}</span>}
    </label>
  );
}

/* ── CutProgress ─────────────────────────────────────────────────── */

/** `2 / 4` 촬영 진행. 숫자가 정보의 본체, 사각형 네 개는 장식이다. */
export function CutProgress({ current, total = 4 }: { current: number; total?: number }) {
  return (
    <div className="flex items-center gap-3">
      <span className="font-mono text-heading font-bold tabular-nums">
        <span className="sr-only">{`${total}컷 중 `}</span>
        {current}
        <span className="text-soft" aria-hidden="true">
          {' / '}
        </span>
        <span aria-hidden="true">{total}</span>
        <span className="sr-only">컷째</span>
      </span>
      <span className="flex gap-1.5" aria-hidden="true">
        {Array.from({ length: total }, (_, index) => (
          <span
            key={index}
            className={cx(
              'size-3',
              index + 1 < current && 'bg-ink',
              index + 1 === current && 'border-2 border-accent bg-accent-tint',
              index + 1 > current && 'border border-line-strong',
            )}
          />
        ))}
      </span>
    </div>
  );
}

/* ── Countdown ───────────────────────────────────────────────────── */

/** 촬영 카운트다운. 시각 장식(aria-hidden)이며 낭독은 촬영 화면의 role=status가 맡는다. */
export function Countdown({ seconds }: { seconds: number }) {
  return (
    <div
      className={cx(
        'flex size-28 items-center justify-center border-4',
        seconds <= 1 ? 'border-accent text-accent' : 'border-ink text-text',
      )}
      aria-hidden="true"
    >
      <span className="text-display font-bold tabular-nums">{seconds}</span>
    </div>
  );
}
