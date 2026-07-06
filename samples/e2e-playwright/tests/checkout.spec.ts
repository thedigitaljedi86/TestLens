import { test, expect } from '@playwright/test';

test.describe('checkout flow', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('/');
  });

  test('shows an empty cart by default', async ({ page }) => {
    await expect(page.getByTestId('cart-count')).toHaveText('0');
  });

  test('adds an item to the cart', async ({ page }) => {
    await page.getByRole('button', { name: 'Add to cart' }).click();
    await expect(page.getByTestId('cart-count')).toHaveText('1');
  });

  test.skip('applies a discount code', async ({ page }) => {
    // Pricing service stub not ready yet.
  });

  test.fixme('completes payment on webkit', async ({ page }) => {
    // Broken on webkit - tracked in E2E-204.
  });

  test.only('renders the receipt', async ({ page }) => {
    await expect(page.getByTestId('receipt')).toBeVisible();
  });

  // test('emails the receipt', async ({ page }) => {
  //   await expect(page.getByTestId('receipt-email')).toBeVisible();
  // });
});
