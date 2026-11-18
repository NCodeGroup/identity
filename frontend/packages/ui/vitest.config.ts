import { defineTestConfig } from '@ncode/vite-config';

export default defineTestConfig({
  environment: 'jsdom',
  coverage: { statements: 98, branches: 97, functions: 100, lines: 98 },
});
