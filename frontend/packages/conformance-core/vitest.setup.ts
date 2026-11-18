import { afterEach, beforeEach } from 'vitest';
import { failOnConsole } from '@ncodegroup/identity-test-support';

// Warnings-as-errors for tests: any console.error/console.warn fails the test that logged it (ADR-1151).
failOnConsole({ beforeEach, afterEach });
