import react from '@vitejs/plugin-react';
import { defineConfig } from 'vitest/config';

// WSL 개발 계약: Vite는 127.0.0.1:5173, .NET 가상 호스트는 127.0.0.1:4317.
// /api 전체를 프록시하되 Origin은 위장하지 않는다 (WSL_DEVELOPMENT.md 4절).
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:4317',
        changeOrigin: true,
      },
    },
  },
  preview: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts'],
  },
});
