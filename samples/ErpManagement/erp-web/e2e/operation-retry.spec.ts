import { expect, test } from '@playwright/test';

interface SettlementRequest {
  amount: number;
  operationId: string;
}

test('结算拒绝后可修正，响应丢失后同页及刷新均以原请求重试', async ({ page }) => {
  const userId = '1260a378-24f9-482a-92a5-adcfaaec1e2b';
  const tenantId = '1e6998ca-8592-46c2-a42e-50494d51d8f1';
  const ledgerId = '1cd8c7d2-60ff-4eed-9544-691b188c4430';
  const tokenPayload = Buffer.from(JSON.stringify({ sub: userId, tenantid: tenantId })).toString('base64url');
  await page.addInitScript(({ token }) => {
    localStorage.setItem('erp_access_token', token);
    localStorage.setItem('erp_tenant_id', 'demo-tenant-alias');
  }, { token: `fixture.${tokenPayload}.fixture` });

  await page.route('**/api/permission-grants/me', (route) => route.fulfill({
    json: { userId, tenantId, permissions: ['ArApLedger.Get', 'ArApLedger.Settle'] },
  }));

  let settledAmount = 0;
  const ledger = () => ({
    id: ledgerId,
    direction: 'Payable',
    sourceDocType: 'GoodsReceipt',
    amount: 10,
    settledAmount,
    status: settledAmount ? 'PartiallySettled' : 'Open',
  });
  await page.route('**/api/arap-ledger/all', (route) => route.fulfill({ json: [ledger()] }));
  const submissions: SettlementRequest[] = [];
  await page.route(`**/api/arap-ledger/${ledgerId}/settle`, async (route) => {
    expect(route.request().headers()['x-tenant-id']).toBe(tenantId);
    const submission: SettlementRequest = route.request().postDataJSON();
    submissions.push(submission);
    switch (submissions.length) {
      case 1:
        await route.fulfill({ status: 400, json: { message: '结算金额被拒绝' } });
        return;
      case 2:
        settledAmount += submission.amount;
        await route.abort();
        return;
      case 3:
        await route.abort();
        return;
      case 4:
        await route.fulfill({ json: ledger() });
        return;
      default:
        settledAmount += submission.amount;
        await route.fulfill({ json: ledger() });
    }
  });

  await page.goto('/finance/ledger');
  const startSettlement = page.getByText('结算', { exact: true });
  const dialog = page.getByRole('dialog', { name: '结算' });
  const amount = dialog.getByLabel('结算金额', { exact: true });
  const submit = dialog.getByRole('button', { name: /^确\s*[认定]$/ });
  const retry = page.getByRole('button', { name: '重试结算（4）' });

  await startSettlement.click();
  await amount.fill('3');
  await submit.click();
  await expect(page.getByText('结算金额被拒绝', { exact: true })).toBeVisible();
  await expect(dialog).toBeVisible();
  await expect(retry).toHaveCount(0);

  await amount.fill('4');
  await submit.click();
  await expect(dialog).toBeHidden();
  await expect(retry).toBeVisible();
  await retry.click();
  await expect.poll(() => submissions.length).toBe(3);
  await expect(retry).not.toHaveClass(/ant-btn-loading/);
  expect(submissions[2]).toEqual(submissions[1]);

  await page.reload();
  await expect(page.getByRole('cell', { name: '部分结算', exact: true })).toBeVisible();
  await expect(startSettlement).toHaveCount(0);
  await retry.click();
  await expect(retry).toHaveCount(0);
  expect(submissions[3]).toEqual(submissions[1]);
  expect(submissions[1]).toMatchObject({ amount: 4, operationId: expect.any(String) });
  expect(submissions[0].operationId).not.toBe(submissions[1].operationId);

  await startSettlement.click();
  await amount.fill('4');
  await submit.click();
  await expect(dialog).toBeHidden();
  expect(submissions[4]).toMatchObject({ amount: 4, operationId: expect.any(String) });
  expect(submissions[4].operationId).not.toBe(submissions[1].operationId);
  expect(settledAmount).toBe(8);
});

test('两个标签页刷新后重试同一笔未确认结算只发送一次', async ({ browser, baseURL }) => {
  const userId = '535216b1-c351-4627-b06d-71761222a0f9';
  const tenantId = '139353a7-b66c-43a4-a86e-fd2c2c76d337';
  const ledgerId = '5e68a70d-b996-403c-8175-9491c393b4a6';
  const token = `fixture.${Buffer.from(JSON.stringify({ sub: userId, tenantid: tenantId })).toString('base64url')}.fixture`;
  const context = await browser.newContext({ baseURL });
  const operationId = 'e822187b-3871-4d08-b63c-07f891d28781';
  const pendingKey = `erp.pending-operation:${JSON.stringify([userId, tenantId, `/api/arap-ledger/${ledgerId}/settle`])}`;
  await context.addInitScript(({ token, pendingKey, operationId }) => {
    localStorage.setItem('erp_access_token', token);
    localStorage.setItem(pendingKey, JSON.stringify({ operationId, values: { amount: 4 } }));
  }, { token, pendingKey, operationId });
  try {
    const page = await context.newPage();
    const sibling = await context.newPage();
    await context.route('**/api/permission-grants/me', route => route.fulfill({
      json: { userId, tenantId, permissions: ['ArApLedger.Get', 'ArApLedger.Settle'] },
    }));
    await context.route('**/api/arap-ledger/all', route => route.fulfill({ json: [{
      id: ledgerId, direction: 'Payable', sourceDocType: 'GoodsReceipt', amount: 10, settledAmount: 4, status: 'PartiallySettled',
    }] }));
    const requests: SettlementRequest[] = [];
    let releaseResponse!: () => void;
    const responseGate = new Promise<void>(resolve => { releaseResponse = resolve; });
    await context.route(`**/api/arap-ledger/${ledgerId}/settle`, async route => {
      requests.push(route.request().postDataJSON());
      await responseGate;
      await route.fulfill({ json: { id: ledgerId, amount: 10, settledAmount: 8, status: 'PartiallySettled' } });
    });
    await page.goto('/finance/ledger');
    await sibling.goto('/finance/ledger');
    const retry = page.getByRole('button', { name: '重试结算（4）' });
    const siblingRetry = sibling.getByRole('button', { name: '重试结算（4）' });
    await Promise.all([retry.click(), siblingRetry.click()]);
    await expect.poll(() => requests.length).toBe(1);
    expect(requests[0]).toEqual({ amount: 4, operationId });
    releaseResponse();
    await expect(retry).toHaveCount(0);
    await expect(siblingRetry).toHaveCount(0);
  } finally {
    await context.close();
  }
});
