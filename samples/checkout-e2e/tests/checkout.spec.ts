import { test, expect } from '@playwright/test';

test.describe('checkout', () => {
  test('shows the cart total', async ({ page }) => {
    await page.goto('/cart');
    await expect(page.getByTestId('total')).toHaveText('$42.00');
  });

  test('applies a discount code', async ({ page }) => {
    await page.goto('/cart');
    await page.getByLabel('Discount code').fill('SAVE10');
    await expect(page.getByTestId('total')).toHaveText('$37.80');
  });

  // Skipped: the payment provider sandbox is flaky in CI.
  test.skip('completes payment', async ({ page }) => {
    await page.goto('/checkout');
    await page.getByRole('button', { name: 'Pay now' }).click();
    await expect(page).toHaveURL(/\/confirmation/);
  });

  // Known-broken on WebKit - tracked in CHK-204.
  test.fixme('remembers the address on WebKit', async ({ page }) => {
    await page.goto('/checkout');
    await expect(page.getByLabel('Address')).not.toBeEmpty();
  });

  // test('legacy guest checkout', async ({ page }) => {
  //   await page.goto('/guest');
  // });
});
