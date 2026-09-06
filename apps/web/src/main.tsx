import { StrictMode, Suspense, lazy } from 'react';
import './styles/tokens.css';
import './styles/base.css';
import './styles/booth.css';
import './styles/print-font.css';
import { createRoot } from 'react-dom/client';
import App from './App';

const rootElement = document.getElementById('root');
if (!rootElement) {
  throw new Error('root element not found');
}

// 디자인 시스템 v2 데모(개발 전용). DEV가 false로 접히면 청크 자체가 빌드에서 빠진다.
const designDemo = import.meta.env.DEV && window.location.hash === '#design';
const DesignSystemDemo = import.meta.env.DEV
  ? lazy(() => import('./ds/DesignSystemDemo'))
  : null;

createRoot(rootElement).render(
  <StrictMode>
    {designDemo && DesignSystemDemo ? (
      <Suspense fallback={null}>
        <DesignSystemDemo />
      </Suspense>
    ) : (
      <App />
    )}
  </StrictMode>,
);
