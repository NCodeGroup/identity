import { defineTestConfig } from '@ncodegroup/identity-vite-config';

export default defineTestConfig({
  environment: 'jsdom',
  coverage: { statements: 98, branches: 97, functions: 100, lines: 98 },
});
