import test from 'node:test';
import assert from 'node:assert/strict';
import { sanitizeToolArgs } from './hexQueryClient';

test('sanitizeToolArgs keeps well-formed string args', () => {
  const result = sanitizeToolArgs(['--file', 'VbLib/BillingPage.aspx.vb']);
  assert.deepEqual(result, ['--file', 'VbLib/BillingPage.aspx.vb']);
});

test('sanitizeToolArgs drops non-array input', () => {
  assert.deepEqual(sanitizeToolArgs(undefined), []);
  assert.deepEqual(sanitizeToolArgs('--file'), []);
  assert.deepEqual(sanitizeToolArgs({ args: ['--file'] }), []);
});

test('sanitizeToolArgs strips non-string items and blank entries', () => {
  const result = sanitizeToolArgs(['--search', 42, null, '   ', 'CalculateTax']);
  assert.deepEqual(result, ['--search', 'CalculateTax']);
});

test('sanitizeToolArgs blocks attempts to override the forced --cache flag', () => {
  const result = sanitizeToolArgs(['--overview', '--cache', '/etc/passwd']);
  assert.deepEqual(result, ['--overview']);
});

test('sanitizeToolArgs caps the number of args and their length', () => {
  const many = Array.from({ length: 20 }, (_, i) => `--flag${i}`);
  assert.equal(sanitizeToolArgs(many).length, 12);

  const long = 'x'.repeat(1000);
  const result = sanitizeToolArgs([long]);
  assert.equal(result[0].length, 300);
});
