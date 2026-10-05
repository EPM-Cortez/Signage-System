import assert from 'node:assert/strict';
import test from 'node:test';
import { validateSettings } from './adapter.js';

test('settings require bounded width and explicit slide numbers', () => {
  const settings = validateSettings({ outputWidth: 1920, fontDirectories: [], requestedSourceSlideNumbers: [1, 3], timeoutSeconds: 300, diagnosticVerbosity: 'normal' });
  assert.deepEqual(settings.requestedSourceSlideNumbers, [1, 3]);
  assert.throws(() => validateSettings({ ...settings, outputWidth: 10 }), /width/i);
  assert.throws(() => validateSettings({ ...settings, useSystemFonts: 'yes' }), /system font/i);
  assert.throws(() => validateSettings({ ...settings, fitTextToBox: 'yes' }), /text fitting/i);
  assert.equal(validateSettings({ ...settings, fitTextToBox: false }).fitTextToBox, false);
  assert.throws(() => validateSettings({ ...settings, fontMapping: { Arial: null } }), /mapping/i);
  assert.deepEqual(validateSettings({ ...settings, fontMapping: { Arial: 'Arimo' }, useSystemFonts: false }).fontMapping, { Arial: 'Arimo' });
});
