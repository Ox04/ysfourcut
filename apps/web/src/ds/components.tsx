import { useEffect, useId, useRef, useState } from 'react';
import type {
  CSSProperties,
  ComponentPropsWithoutRef,
  KeyboardEvent as ReactKeyboardEvent,
  ReactNode,
} from 'react';

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
        className="ds-range"
        style={{ '--fill': `${((value - min) / (max - min)) * 100}%` } as CSSProperties}
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

/* ── Select (커스텀 드롭다운) ────────────────────────────────────── */

/** 소수 선택지는 SegmentedControl, 목록이 길거나 자리가 좁으면 Select.
    listbox 패턴 커스텀 드롭다운 — 열릴 때 ds-drop으로 부드럽게 내려오고(닫힘은 즉시),
    reduced-motion에서는 애니메이션이 꺼진다. 항목 터치 크기 48px. */
export function Select<T extends string>({
  label,
  options,
  value,
  disabled = false,
  onChange,
  className,
}: {
  label: string;
  options: readonly SegmentOption<T>[];
  value: T;
  disabled?: boolean;
  onChange: (next: T) => void;
  className?: string;
}) {
  const listId = useId();
  const rootRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const selectedIndex = options.findIndex((option) => option.value === value);
  const [activeIndex, setActiveIndex] = useState(Math.max(0, selectedIndex));

  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener('pointerdown', onPointerDown);
    return () => document.removeEventListener('pointerdown', onPointerDown);
  }, [open]);

  const openList = () => {
    setActiveIndex(Math.max(0, selectedIndex));
    setOpen(true);
  };
  const commit = (index: number) => {
    onChange(options[index].value);
    setOpen(false);
  };
  const onKeyDown = (event: ReactKeyboardEvent<HTMLButtonElement>) => {
    if (!open) {
      if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(event.key)) {
        event.preventDefault();
        openList();
      }
      return;
    }
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
      event.preventDefault();
      const delta = event.key === 'ArrowDown' ? 1 : -1;
      setActiveIndex((index) => Math.min(options.length - 1, Math.max(0, index + delta)));
    } else if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      commit(activeIndex);
    } else if (event.key === 'Escape' || event.key === 'Tab') {
      setOpen(false);
    }
  };

  return (
    <div ref={rootRef} className={cx('flex flex-col gap-1.5', className)}>
      <span className="text-label font-medium text-soft" id={`${listId}-label`}>
        {label}
      </span>
      <div className="relative">
        <button
          type="button"
          id={`${listId}-button`}
          disabled={disabled}
          aria-haspopup="listbox"
          aria-expanded={open}
          aria-labelledby={`${listId}-label ${listId}-button`}
          aria-activedescendant={open ? `${listId}-${activeIndex}` : undefined}
          className="flex min-h-12 w-full items-center justify-between gap-2 border border-line-strong bg-field px-4 text-body disabled:border-line disabled:text-disabled"
          onClick={() => (open ? setOpen(false) : openList())}
          onKeyDown={onKeyDown}
        >
          <span>{options[selectedIndex]?.label}</span>
          <svg
            className={cx('shrink-0 text-soft transition-transform duration-100', open && 'rotate-180')}
            width="16"
            height="16"
            viewBox="0 0 16 16"
            aria-hidden="true"
          >
            <path d="M3 6l5 5 5-5" fill="none" stroke="currentColor" strokeWidth="2" />
          </svg>
        </button>
        {open && (
          <ul
            role="listbox"
            id={listId}
            aria-labelledby={`${listId}-label`}
            className="ds-drop absolute inset-x-0 top-full z-10 mt-1 border border-line-strong bg-layer"
          >
            {options.map((option, index) => (
              <li
                key={option.value}
                id={`${listId}-${index}`}
                role="option"
                aria-selected={option.value === value}
                className={cx(
                  'flex min-h-12 cursor-pointer items-center px-4 text-body',
                  index === activeIndex && 'bg-sunken',
                  option.value === value && 'font-semibold',
                )}
                onPointerEnter={() => setActiveIndex(index)}
                onClick={() => commit(index)}
              >
                {option.label}
              </li>
            ))}
          </ul>
        )}
      </div>
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

/* ── Keypad ──────────────────────────────────────────────────────── */

/** 터치 숫자 키패드. PIN·인증번호 입력에서 CodeInput과 함께 쓴다. 키 60px.
    지우기는 ⌫ 하나만 둔다 — 전체 지움이 필요하면 호출부가 별도 동작으로 제공한다. */
export function Keypad({
  onDigit,
  onBackspace,
  disabled = false,
}: {
  onDigit: (digit: string) => void;
  onBackspace: () => void;
  disabled?: boolean;
}) {
  const key =
    'min-h-15 border border-line-strong bg-layer text-heading font-semibold hover:bg-sunken disabled:cursor-not-allowed disabled:text-disabled';
  return (
    <div className="grid w-64 grid-cols-3 gap-2">
      {['1', '2', '3', '4', '5', '6', '7', '8', '9'].map((digit) => (
        <button key={digit} type="button" className={key} disabled={disabled} onClick={() => onDigit(digit)}>
          {digit}
        </button>
      ))}
      <span aria-hidden="true" />
      <button type="button" className={key} disabled={disabled} onClick={() => onDigit('0')}>
        0
      </button>
      <button
        type="button"
        className={key}
        disabled={disabled}
        aria-label="한 글자 지우기"
        onClick={onBackspace}
      >
        ⌫
      </button>
    </div>
  );
}

/* ── Keyboard ────────────────────────────────────────────────────── */

const KEYBOARD_ROWS = ['1234567890', 'qwertyuiop', 'asdfghjkl', 'zxcvbnm'] as const;

/** 화상 QWERTY 키보드(라틴/이메일용). OS 소프트 키보드가 키오스크 화면을 깨는 것을
    피하기 위한 것으로, 대상 입력창에는 inputMode="none"을 줘 OS 키보드만 억제한다.
    키를 pointerdown에서 preventDefault해 입력창 포커스를 뺏지 않으므로 물리 키보드와
    동시에 동작한다. 숫자열 상시 노출(레이어 전환 없음), Shift는 원샷(한 글자 뒤 해제).
    한글 조합 입력은 범위 밖 — 문구 입력은 물리 키보드/후속 과제. */
export function Keyboard({
  onKey,
  onBackspace,
  disabled = false,
  className,
}: {
  onKey: (char: string) => void;
  onBackspace: () => void;
  disabled?: boolean;
  className?: string;
}) {
  const [shift, setShift] = useState(false);
  const key =
    'min-h-12 flex-1 border border-line-strong bg-layer text-body font-medium hover:bg-sunken disabled:cursor-not-allowed disabled:text-disabled';
  const emit = (char: string) => {
    onKey(shift ? char.toUpperCase() : char);
    if (shift) setShift(false);
  };
  // 포커스 유지 트릭: 버튼이 focus를 가져가면 물리 키보드 입력이 끊긴다.
  const keepFocus = (event: { preventDefault: () => void }) => event.preventDefault();

  return (
    <div className={cx('flex w-full max-w-2xl flex-col gap-2', className)} role="group" aria-label="화상 키보드">
      {KEYBOARD_ROWS.slice(0, 2).map((row) => (
        <div key={row} className="flex gap-2">
          {[...row].map((char) => (
            <button key={char} type="button" className={key} disabled={disabled} onPointerDown={keepFocus} onClick={() => emit(char)}>
              {shift ? char.toUpperCase() : char}
            </button>
          ))}
        </div>
      ))}
      <div className="flex gap-2">
        <span className="flex-[0.5]" aria-hidden="true" />
        {[...KEYBOARD_ROWS[2]].map((char) => (
          <button key={char} type="button" className={key} disabled={disabled} onPointerDown={keepFocus} onClick={() => emit(char)}>
            {shift ? char.toUpperCase() : char}
          </button>
        ))}
        <span className="flex-[0.5]" aria-hidden="true" />
      </div>
      <div className="flex gap-2">
        <button
          type="button"
          className={cx(key, 'flex-[1.5]', shift && 'border-ink bg-ink text-on-ink hover:bg-ink-hover')}
          disabled={disabled}
          aria-pressed={shift}
          onPointerDown={keepFocus}
          onClick={() => setShift((current) => !current)}
        >
          ⇧
        </button>
        {[...KEYBOARD_ROWS[3]].map((char) => (
          <button key={char} type="button" className={key} disabled={disabled} onPointerDown={keepFocus} onClick={() => emit(char)}>
            {shift ? char.toUpperCase() : char}
          </button>
        ))}
        <button
          type="button"
          className={cx(key, 'flex-[1.5]')}
          disabled={disabled}
          aria-label="한 글자 지우기"
          onPointerDown={keepFocus}
          onClick={onBackspace}
        >
          ⌫
        </button>
      </div>
      <div className="flex gap-2">
        {['@', '.', '-', '_'].map((char) => (
          <button key={char} type="button" className={cx(key, 'max-w-16')} disabled={disabled} onPointerDown={keepFocus} onClick={() => onKey(char)}>
            {char}
          </button>
        ))}
        <button
          type="button"
          className={cx(key, 'flex-[4]')}
          disabled={disabled}
          aria-label="띄어쓰기"
          onPointerDown={keepFocus}
          onClick={() => onKey(' ')}
        >
          스페이스
        </button>
      </div>
    </div>
  );
}

/* ── CodeInput ───────────────────────────────────────────────────── */

/** 인증번호/PIN 6칸 입력(shadcn InputOTP류). 실제 입력은 투명한 input 하나가 받아
    하드웨어 키보드·IME 붙여넣기도 동작하고, Keypad로는 value를 밖에서 조작한다. */
export function CodeInput({
  label,
  length = 6,
  value,
  onChange,
  className,
}: {
  label: string;
  length?: number;
  value: string;
  onChange: (next: string) => void;
  className?: string;
}) {
  const [focused, setFocused] = useState(false);
  const digits = value.slice(0, length);
  const activeIndex = Math.min(digits.length, length - 1);
  return (
    <label className={cx('flex w-fit flex-col gap-1.5', className)}>
      <span className="text-label font-medium text-soft">{label}</span>
      <span className="relative inline-flex gap-2">
        <input
          className="absolute inset-0 cursor-pointer opacity-0"
          inputMode="numeric"
          autoComplete="one-time-code"
          aria-label={label}
          value={digits}
          maxLength={length}
          onChange={(event) => onChange(event.target.value.replace(/\D/g, '').slice(0, length))}
          onFocus={() => setFocused(true)}
          onBlur={() => setFocused(false)}
        />
        {Array.from({ length }, (_, index) => (
          <span
            key={index}
            aria-hidden="true"
            className={cx(
              'flex size-14 items-center justify-center border bg-field text-heading font-bold tabular-nums',
              focused && index === activeIndex ? 'border-2 border-ink' : 'border-line-strong',
            )}
          >
            {digits[index] ?? ''}
          </span>
        ))}
      </span>
    </label>
  );
}

/* ── Dialog ──────────────────────────────────────────────────────── */

/** 확인 대화상자. 네이티브 <dialog>라 포커스 가둠·ESC 닫기·최상위 표시를 브라우저가 맡는다. */
export function Dialog({
  open,
  title,
  children,
  onClose,
}: {
  open: boolean;
  title: ReactNode;
  children: ReactNode;
  onClose: () => void;
}) {
  const ref = useRef<HTMLDialogElement>(null);
  useEffect(() => {
    const dialog = ref.current;
    if (!dialog) return;
    if (open && !dialog.open) dialog.showModal();
    else if (!open && dialog.open) dialog.close();
  }, [open]);
  return (
    <dialog
      ref={ref}
      onClose={onClose}
      className="ds-drop m-auto w-full max-w-sm border-2 border-ink bg-layer p-6 text-body text-text backdrop:bg-[rgb(0_0_0/0.55)]"
    >
      <div className="flex flex-col gap-4">
        <h2 className="text-heading font-bold">{title}</h2>
        {children}
      </div>
    </dialog>
  );
}

/* ── Notice ──────────────────────────────────────────────────────── */

export type NoticeTone = 'info' | 'ok' | 'warn' | 'danger';

const noticeTone: Record<NoticeTone, string> = {
  info: 'border-l-ink',
  ok: 'border-l-ok',
  warn: 'border-l-warn',
  danger: 'border-l-danger',
};

/** 인라인 안내 블록. 라이브 영역이 필요하면 호출부가 role=status/alert를 붙인다. */
export function Notice({
  tone = 'info',
  title,
  children,
  className,
  ...rest
}: ComponentPropsWithoutRef<'div'> & { tone?: NoticeTone; title?: ReactNode }) {
  return (
    <div
      className={cx('border border-line border-l-4 bg-layer p-4', noticeTone[tone], className)}
      {...rest}
    >
      {title !== undefined && <p className="text-body font-semibold">{title}</p>}
      <div className="text-body text-soft">{children}</div>
    </div>
  );
}

/* ── Spinner ─────────────────────────────────────────────────────── */

/** 진행 표시. 문구가 정보의 본체(role=status는 호출부), 원은 장식이며 reduced-motion에서 멈춘다. */
export function Spinner({ label }: { label?: ReactNode }) {
  return (
    <span className="inline-flex items-center gap-3">
      <span
        className="size-6 animate-spin rounded-[50%] border-2 border-line border-t-ink"
        aria-hidden="true"
      />
      {label && <span className="text-body text-soft">{label}</span>}
    </span>
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
