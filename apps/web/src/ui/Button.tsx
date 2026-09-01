import type { ComponentPropsWithoutRef } from 'react';

type ButtonProps = ComponentPropsWithoutRef<'button'> & {
  /** primary = 포인트색 핵심 동작, secondary = 잉크 외곽선, quiet = 저강조 */
  variant?: 'primary' | 'secondary' | 'quiet';
  /** lg = 촬영 시작·가상 출력급(60px), md = 일반 동작(48px) */
  size?: 'lg' | 'md';
};

export function Button({ variant = 'secondary', size = 'md', className, ...rest }: ButtonProps) {
  const classes = ['btn', `btn--${variant}`, `btn--${size}`, className].filter(Boolean).join(' ');
  return <button type="button" className={classes} {...rest} />;
}
