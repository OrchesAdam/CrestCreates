import { expect, test } from '@playwright/test';

interface LedgerEntry {
  sourceDocType: string;
  sourceDocId: string;
  amount: number;
  settledAmount: number;
  status: string;
  settlements: { amount: number }[];
}

test('重启后仍能登录并查看已提交的库存和结算事实', async ({ page, request }) => {
  await expect(async () => {
    const ready = await request.get('/health/ready');
    expect(ready.ok()).toBeTruthy();
    expect((await ready.text()).trim()).toBe('Healthy');
  }).toPass({ timeout: 60_000, intervals: [1_000] });
  const password = process.env.ERP_E2E_ADMIN_PASSWORD;
  if (!password) throw new Error('Set ERP_E2E_ADMIN_PASSWORD');
  await page.goto('/login');
  await page.getByPlaceholder('用户名').fill('admin');
  await page.getByPlaceholder('密码', { exact: true }).fill(password);
  await page.getByRole('button', { name: /登\s*录$/ }).click();
  await expect(page).toHaveURL(/\/dashboard$/);
  await page.goto('/inventory/balances');
  const motorRow = page.getByRole('row').filter({ hasText: 'MAT-MOTOR-01' }).filter({ hasText: 'A-01' });
  await expect(motorRow.getByRole('cell', { name: '2', exact: true })).toBeVisible();
  const assemblyRow = page.getByRole('row').filter({ hasText: 'SEMI-DRIVE-01' }).filter({ hasText: 'A-01' });
  await expect(assemblyRow.getByRole('cell', { name: '1', exact: true })).toBeVisible();
  const [ledgerResponse] = await Promise.all([
    page.waitForResponse((response) => response.request().method() === 'GET'
      && new URL(response.url()).pathname === '/api/arap-ledger/all'),
    page.goto('/finance/ledger'),
  ]);
  expect(ledgerResponse.ok()).toBeTruthy();
  const payload = await ledgerResponse.json();
  const entries: LedgerEntry[] = payload.data ?? payload;
  expect(entries).toHaveLength(2);
  for (const [sourceDocType, amount] of [['GoodsReceipt', 15500], ['SalesShipment', 60000]] as const) {
    const entry = entries.find((item) => item.sourceDocType === sourceDocType);
    expect(entry).toMatchObject({ sourceDocType, sourceDocId: expect.any(String), amount, settledAmount: amount,
      status: 'Settled', settlements: [{ amount }] });
  }
  await expect(page.getByRole('cell', { name: '已结清', exact: true })).toHaveCount(2);
});
