import { expect, test } from '@playwright/test';
import { resolve } from 'node:path';

test.describe.configure({ mode: 'serial' });

test('teacher publishes an authorised PowerPoint with keyboard-reachable controls', async ({ page }) => {
  await page.goto('/dev-login?asRole=teacher');
  await expect(page).toHaveURL(/\/Publish$/);
  await expect(page.getByRole('heading', { name: 'Publish to screens' })).toBeVisible();
  await expect(page.getByText('Staff Room', { exact: true })).toHaveCount(0);

  const fileInput = page.getByLabel('PowerPoint');
  await fileInput.setInputFiles(resolve(import.meta.dirname, '../../../output/playwright/fixtures/playwright-welcome.pptx'));
  await page.getByLabel('Presentation name').fill('Playwright welcome');
  const publish = page.getByRole('button', { name: 'Publish' });
  await publish.focus();
  await expect(publish).toBeFocused();
  await publish.press('Enter');

  await expect(page.getByRole('heading', { name: 'Published' })).toBeVisible({ timeout: 60_000 });
  await expect(page.getByRole('link', { name: 'Open preview' })).toBeVisible();
});

test('paired player downloads, reloads offline, then loses server access when revoked', async ({ context }) => {
  const player = await context.newPage();
  await player.goto('/player/');
  const codeElement = player.locator('#pairing-code');
  await expect(codeElement).toBeVisible();
  const code = (await codeElement.textContent())?.trim();
  expect(code).toMatch(/^\d{6}$/);

  const admin = await context.newPage();
  await admin.goto('/dev-login?asRole=admin');
  await admin.goto('/Admin/Pairing');
  await admin.getByLabel('Pairing code').fill(code!);
  await admin.getByLabel('Device name').fill('Playwright display');
  await admin.getByLabel('Screen group').selectOption({ label: 'Reception' });
  await admin.getByRole('button', { name: 'Approve device' }).click();
  await expect(admin.getByText('Device approved. It will begin downloading its assigned content shortly.')).toBeVisible();

  await expect(player.locator('img.media.active')).toBeVisible({ timeout: 60_000 });
  await player.reload();
  await expect.poll(() => player.evaluate(() => Boolean(navigator.serviceWorker.controller))).toBe(true);
  await expect(player.locator('img.media.active')).toBeVisible();

  await context.setOffline(true);
  await player.reload();
  await expect(player.locator('img.media.active')).toBeVisible();
  await context.setOffline(false);

  await admin.goto('/Admin/Devices');
  const row = admin.getByRole('row').filter({ hasText: 'Playwright display' });
  await row.getByRole('button', { name: 'More actions for Playwright display' }).click();
  await row.getByRole('button', { name: 'Revoke access', exact: true }).click();
  await expect(admin.getByText('Device revoked.')).toBeVisible();
  await player.reload();
  await expect(player.locator('#pairing-code')).toBeVisible({ timeout: 20_000 });
});
