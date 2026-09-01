import { StrictMode } from 'react';
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

createRoot(rootElement).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
