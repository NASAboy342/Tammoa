import { expect, test } from '@playwright/test'

test('home shows the backend version (frontend ↔ backend wiring)', async ({ page }) => {
  await page.goto('/#/')
  await expect(page.getByTestId('version')).toContainText('Tammoa v')
})

test('theme toggle switches data-theme', async ({ page }) => {
  await page.goto('/#/')
  await page.getByTestId('theme-toggle').click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark')
  await page.getByTestId('theme-toggle').click()
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
})
