import { useEffect, useRef, useState } from 'react';
import './ds.css';
import {
  Button,
  CodeInput,
  Countdown,
  CutProgress,
  Dialog,
  Keyboard,
  Keypad,
  Notice,
  Panel,
  SegmentedControl,
  Select,
  SliderField,
  Spinner,
  StatusBadge,
  TextField,
} from './components';

/* 디자인 시스템 v2 데모 — 개발 전용(#design 해시). 운영 번들에는 들어가지 않는다.
   목적: 토큰·컴포넌트를 실제 화면과 따로 검수한다. 테마는 쿨 그레이 단일(2026-09-07 확정). */

/* 시맨틱 토큰 → 데모에 보여줄 이름 순서. ds.css의 --ds-* 와 1:1 */
const COLOR_TOKENS = [
  'bg',
  'layer',
  'sunken',
  'field',
  'text',
  'soft',
  'disabled',
  'line',
  'line-strong',
  'ink',
  'ink-hover',
  'on-ink',
  'accent',
  'accent-hover',
  'accent-tint',
  'on-accent',
  'ok',
  'warn',
  'danger',
  'danger-tint',
  'focus',
  'viewer',
  'viewer-deep',
] as const;

const TYPE_SCALE = [
  { cls: 'text-display', name: 'display · 42', sample: '네컷 사진' },
  { cls: 'text-title', name: 'title · 28', sample: '촬영을 시작할까요?' },
  { cls: 'text-heading', name: 'heading · 20', sample: '프린터 프로필' },
  { cls: 'text-body-lg', name: 'body-lg · 18 (키오스크 본문)', sample: '정면을 바라보고 자세를 잡아 주세요.' },
  { cls: 'text-body', name: 'body · 16', sample: '가상 영수증을 만드는 중이에요. 잠시만 기다려 주세요.' },
  { cls: 'text-label', name: 'label · 14', sample: '가상 출력 · AHAPOS 미검증' },
  { cls: 'text-caption', name: 'caption · 12', sample: '576 × 1788 dot · 203.2 DPI' },
] as const;

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="flex flex-col gap-4">
      <h2 className="border-b border-line pb-2 text-heading font-bold">{title}</h2>
      {children}
    </section>
  );
}

export default function DesignSystemDemo() {
  const rootRef = useRef<HTMLDivElement>(null);
  const [resolved, setResolved] = useState<Record<string, string>>({});
  const [frame, setFrame] = useState<'basic' | 'date'>('basic');
  const [brightness, setBrightness] = useState(0);
  const [caption, setCaption] = useState('');
  const [profile, setProfile] = useState('virtual-80');
  const [pin, setPin] = useState('');
  const [email, setEmail] = useState('');
  const [dialogOpen, setDialogOpen] = useState(false);

  // 검수용: 각 토큰이 실제로 어떤 값인지 hex로 표시한다.
  useEffect(() => {
    if (!rootRef.current) return;
    const style = getComputedStyle(rootRef.current);
    setResolved(
      Object.fromEntries(
        COLOR_TOKENS.map((name) => [name, style.getPropertyValue(`--ds-${name}`).trim()]),
      ),
    );
  }, []);

  return (
    <div ref={rootRef} className="ds-root min-h-screen bg-bg font-sans text-body text-text">
      <header className="sticky top-0 z-10 flex flex-wrap items-center justify-between gap-4 border-b border-line bg-bg px-8 py-4">
        <div>
          <h1 className="text-heading font-bold">YS Fourcut 디자인 시스템 v2</h1>
          <p className="text-caption text-soft">
            쿨 그레이(확정) · 모서리 0 · 경계선 구획 · Stone 중립 + 셔터 버밀리언 · Wanted Sans
          </p>
        </div>
      </header>

      <main className="mx-auto flex max-w-5xl flex-col gap-12 px-8 py-10">
        <Section title="색 토큰">
          <div className="grid grid-cols-2 gap-x-8 gap-y-1 sm:grid-cols-3 lg:grid-cols-4">
            {COLOR_TOKENS.map((name) => (
              <div key={name} className="flex items-center gap-3 py-1">
                <span
                  className="size-8 shrink-0 border border-line"
                  style={{ background: `var(--ds-${name})` }}
                />
                <span className="flex flex-col">
                  <code className="text-label">--ds-{name}</code>
                  <code className="text-caption text-soft">{resolved[name]}</code>
                </span>
              </div>
            ))}
          </div>
        </Section>

        <Section title="타이포그래피">
          <div className="flex flex-col gap-3">
            {TYPE_SCALE.map((row) => (
              <div key={row.cls} className="flex flex-wrap items-baseline gap-x-6 gap-y-1">
                <code className="w-56 shrink-0 text-caption text-soft">{row.name}</code>
                <span className={`${row.cls} font-semibold`}>{row.sample}</span>
              </div>
            ))}
            <p className="text-caption text-soft">
              숫자·치수는 font-mono: <code className="font-mono">640 × 1876 px · 80 × 234.5 mm</code>
            </p>
          </div>
        </Section>

        <Section title="버튼">
          <div className="flex flex-wrap items-center gap-4">
            <Button variant="accent" size="lg">
              촬영 시작
            </Button>
            <Button variant="primary" size="lg">
              가상 출력
            </Button>
            <Button variant="primary">다음 촬영</Button>
            <Button variant="secondary">PNG 저장</Button>
            <Button variant="ghost">다시 확인</Button>
            <Button variant="primary" disabled>
              전송 중…
            </Button>
            <Button variant="secondary" disabled>
              비활성
            </Button>
          </div>
          <p className="text-caption text-soft">
            accent(셔터 레드)는 촬영 시작·출력 같은 핵심 순간 전용, 나머지 동작은 모노크롬.
            md 48px / lg 60px 터치 크기. 포커스 링은 Tab으로 확인.
          </p>
        </Section>

        <Section title="상태 배지">
          <div className="flex flex-wrap gap-3">
            <StatusBadge tone="virtual">가상 출력 모드</StatusBadge>
            <StatusBadge tone="ok">카메라 준비됨</StatusBadge>
            <StatusBadge tone="warn">프로필 미검증</StatusBadge>
            <StatusBadge tone="danger">출력 실패</StatusBadge>
            <StatusBadge tone="neutral">대기 중</StatusBadge>
          </div>
        </Section>

        <Section title="패널">
          <div className="grid gap-6 lg:grid-cols-2">
            <Panel title="기본 패널">
              <p className="text-body-lg">
                그림자 없이 1px 경계선으로 구획한다. 배경은 layer, 눌린 영역은 sunken.
              </p>
              <div className="flex gap-3">
                <Button variant="primary">동작</Button>
                <Button variant="ghost">취소</Button>
              </div>
            </Panel>
            <Panel title="절취선 패널" perforated>
              <p className="text-body-lg">
                영수증 절취선(굵은 점선 윗변)은 이 시스템에서 유지하는 유일한 장식 모티프다.
              </p>
              <StatusBadge tone="virtual">가상 출력 모드</StatusBadge>
            </Panel>
          </div>
        </Section>

        <Section title="컨트롤">
          <Panel>
            <SegmentedControl
              label="프레임"
              options={[
                { value: 'basic', label: '기본 프레임' },
                { value: 'date', label: '날짜 프레임' },
              ]}
              value={frame}
              onChange={setFrame}
            />
            <SliderField
              label="밝기"
              min={-50}
              max={50}
              value={brightness}
              displayValue={String(brightness)}
              onChange={setBrightness}
            />
            <Select
              label="용지 프로필"
              options={[
                { value: 'virtual-80', label: '가상 80mm · 640dot' },
                { value: 'virtual-58', label: '가상 58mm · 384dot' },
              ]}
              value={profile}
              onChange={setProfile}
            />
            <TextField
              label="영수증 문구"
              placeholder="예: 오늘도 좋은 하루"
              maxLength={24}
              value={caption}
              hint={`${caption.length} / 24자`}
              onChange={(event) => setCaption(event.target.value)}
            />
            <p className="text-caption text-soft">
              터치 기준: 모든 컨트롤 히트 영역 48px 이상, 슬라이더 썸 28px. 소수 선택지는
              세그먼트, 목록형은 Select — 열릴 때만 부드럽게 내려오고 항목도 48px.
            </p>
          </Panel>
        </Section>

        <Section title="키패드 · 인증번호">
          <Panel>
            <CodeInput label="운영자 PIN" value={pin} onChange={setPin} />
            <Keypad
              onDigit={(digit) => setPin((current) => (current + digit).slice(0, 6))}
              onBackspace={() => setPin((current) => current.slice(0, -1))}
              onClear={() => setPin('')}
            />
            <p className="text-caption text-soft">
              칸을 탭하면 하드웨어 키보드로도 입력된다(one-time-code 자동완성 지원).
              키패드 키 60px, 하단 전체 지움 · 0 · ⌫.
            </p>
          </Panel>
        </Section>

        <Section title="화상 키보드">
          <Panel>
            <TextField
              label="이메일"
              placeholder="you@example.com"
              inputMode="none"
              autoCapitalize="none"
              spellCheck={false}
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
            <Keyboard
              onKey={(char) => setEmail((current) => current + char)}
              onBackspace={() => setEmail((current) => current.slice(0, -1))}
            />
            <p className="text-caption text-soft">
              65% 배열에서 Ctrl/Alt/Fn/Win/화살표만 뺐다. 입력창은 inputMode=&quot;none&quot;이라
              OS 소프트 키보드가 뜨지 않고, 화상 키보드는 포커스를 뺏지 않아 물리 키보드와
              동시에 동작한다. Shift는 원샷 — 숫자·기호 키캡도 함께 바뀐다(⇧를 눌러 보세요).
              Caps는 글자만 지속. 한글 문구 입력은 물리 키보드용(조합 입력은 후속).
            </p>
          </Panel>
        </Section>

        <Section title="안내 · 대화상자">
          <div className="flex flex-col gap-3">
            <Notice tone="info" title="가상 출력 모드">
              실물 프린터 없이 서버가 만든 영수증 PNG로 결과를 보여줍니다.
            </Notice>
            <Notice tone="ok">인쇄 작업이 큐에 전달됐어요. 종이를 확인해 주세요.</Notice>
            <Notice tone="warn" title="프로필 미검증">
              작은 패턴 인쇄로 후보 설정을 먼저 확인해 주세요.
            </Notice>
            <Notice tone="danger" title="출력 실패">
              설정을 확인한 뒤 다시 시도해 주세요. 사진과 편집 내용은 남아 있어요.
            </Notice>
            <div className="flex flex-wrap items-center gap-6">
              <Spinner label="가상 영수증을 만드는 중…" />
              <Button variant="secondary" onClick={() => setDialogOpen(true)}>
                재출력 확인 대화상자 열기
              </Button>
            </div>
          </div>
          <Dialog open={dialogOpen} title="다시 출력할까요?" onClose={() => setDialogOpen(false)}>
            <p className="text-soft">
              같은 영수증이 한 장 더 출력될 수 있어요. 이미 나온 종이를 확인해 주세요.
            </p>
            <div className="flex justify-end gap-3">
              <Button variant="ghost" onClick={() => setDialogOpen(false)}>
                취소
              </Button>
              <Button variant="primary" onClick={() => setDialogOpen(false)}>
                다시 출력
              </Button>
            </div>
          </Dialog>
        </Section>

        <Section title="진행 · 카운트다운">
          <div className="flex flex-wrap items-center gap-10">
            <CutProgress current={2} />
            <Countdown seconds={3} />
            <Countdown seconds={1} />
          </div>
          <p className="text-caption text-soft">마지막 1초는 셔터 레드로 전환.</p>
        </Section>

        <Section title="결과 뷰어 받침">
          <div className="flex items-center justify-center bg-viewer p-10">
            <div className="flex h-64 w-28 items-center justify-center bg-[#faf7f1] text-caption text-[#211d16]">
              영수증 PNG 자리
            </div>
          </div>
          <p className="text-caption text-soft">
            서버 appearance PNG는 배경이 투명해 중간톤(viewer) 받침 위에 올린다 — 규칙 유지.
          </p>
        </Section>
      </main>
    </div>
  );
}
