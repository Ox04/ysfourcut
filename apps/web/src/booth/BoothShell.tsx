import type { ReactNode } from 'react';
import { VIRTUAL_FAULT_LABELS_KO } from '../api/contract';
import { StatusBadge } from '../ui/parts';
import type { DeviceReadiness } from './types';

export type BoothStep = 'start' | 'capture' | 'edit' | 'result';

const STEPS: { id: BoothStep; label: string }[] = [
  { id: 'start', label: '시작' },
  { id: 'capture', label: '촬영' },
  { id: 'edit', label: '편집' },
  { id: 'result', label: '결과' },
];

/**
 * 모든 부스 화면의 공통 틀. 로고, 단계 표시, 가상 모드 상시 배지.
 * 가상/실물 표시는 어느 화면에서도 사라지지 않는다.
 */
export function BoothShell({
  step,
  device,
  children,
}: {
  step: BoothStep;
  device: DeviceReadiness;
  children: ReactNode;
}) {
  return (
    <div className="booth">
      <header className="booth__header">
        <span className="booth__logo">YS Fourcut</span>

        <ol className="booth__steps" aria-label="진행 단계">
          {STEPS.map((item) => (
            <li
              key={item.id}
              className={`booth__step${item.id === step ? ' is-current' : ''}`}
              aria-current={item.id === step ? 'step' : undefined}
            >
              {item.label}
            </li>
          ))}
        </ol>

        <div className="booth__status">
          <StatusBadge tone="virtual">
            {device.printerMode === 'virtual' ? '가상 출력' : '실물 출력'}
          </StatusBadge>
          {device.printReady ? (
            <StatusBadge tone="ok">프린터 준비됨</StatusBadge>
          ) : (
            <StatusBadge tone="warn">출력 불가</StatusBadge>
          )}
          {/* 주입된 모의 오류는 어느 화면에서도 숨기지 않는다(실제 장치 상태가 아니다). */}
          {device.injectedFault && (
            <StatusBadge tone="danger">
              모의 오류: {VIRTUAL_FAULT_LABELS_KO[device.injectedFault]} · 다음 작업 1회
            </StatusBadge>
          )}
        </div>
      </header>

      <main className="booth__main">
        {step !== 'start' && (
          <h1 className="sr-only">{STEPS.find((item) => item.id === step)?.label} — YS Fourcut</h1>
        )}
        {children}
      </main>
    </div>
  );
}
