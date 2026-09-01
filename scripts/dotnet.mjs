#!/usr/bin/env node
// npm 스크립트에서 dotnet을 부르기 위한 얇은 래퍼. 인수를 그대로 전달한다.

import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { DOTNET_NOT_FOUND_MESSAGE, resolveDotnetBin } from './lib/dotnet.mjs';

const rootDir = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const dotnetBin = resolveDotnetBin();

if (!dotnetBin) {
  console.error(`[dotnet] ${DOTNET_NOT_FOUND_MESSAGE}`);
  process.exit(1);
}

const child = spawn(dotnetBin, process.argv.slice(2), {
  cwd: rootDir,
  env: { ...process.env, DOTNET_ROOT: process.env.DOTNET_ROOT ?? path.dirname(dotnetBin) },
  stdio: 'inherit',
});

child.on('exit', (code, signal) => {
  process.exit(signal ? 1 : (code ?? 0));
});

child.on('error', (error) => {
  console.error(`[dotnet] 실행 실패: ${error.message}`);
  process.exit(1);
});
