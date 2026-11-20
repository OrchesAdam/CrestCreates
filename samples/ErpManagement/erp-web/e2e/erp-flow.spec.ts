import { expect, test, type Locator, type Page, type Response } from '@playwright/test';

interface Document {
  id: string;
  number: string;
  lines: { id: string; position: number }[];
}

const supplier = 'SUP-001 - 华东金属材料有限公司';
const customer = 'CUS-001 - 北方机械制造有限公司';
const warehouse = 'WH-RAW - 原材料仓';
const rawLocation = 'A-01 - A 区 01 架';
const transferLocation = 'A-02 - A 区 02 架';
const motor = 'MAT-MOTOR-01 - 伺服电机 750W';
const housing = 'MAT-HOUSING-01 - 铝合金壳体';
const assembly = 'SEMI-DRIVE-01 - 驱动组件';

function waitForResponse(page: Page, method: 'GET' | 'POST', path: string | RegExp) {
  return page.waitForResponse((response) => {
    const actualPath = new URL(response.url()).pathname;
    return response.request().method() === method
      && (typeof path === 'string' ? actualPath === path : path.test(actualPath));
  });
}

async function json<T>(response: Response): Promise<T> {
  expect(response.ok(), `${response.request().method()} ${new URL(response.url()).pathname} returned ${response.status()}`).toBeTruthy();
  expect(response.headers()['content-type']).toMatch(/json/);
  const body = await response.json();
  return body?.data ?? body;
}

async function post<T = Document>(page: Page, path: string | RegExp, action: () => Promise<void>) {
  const [response] = await Promise.all([waitForResponse(page, 'POST', path), action()]);
  return json<T>(response);
}

async function visit<T>(page: Page, path: string, listPath: string) {
  const [response] = await Promise.all([waitForResponse(page, 'GET', listPath), page.goto(path)]);
  return json<T>(response);
}

async function login(page: Page, username: string, passwordVariable: string) {
  const password = process.env[passwordVariable];
  if (!password) throw new Error(`Set ${passwordVariable} to the password seeded by the target ERP deployment`);
  await page.goto('/login');
  await page.getByPlaceholder('用户名').fill(username);
  await page.getByPlaceholder('密码', { exact: true }).fill(password);
  const response = waitForResponse(page, 'POST', '/connect/token');
  await page.getByRole('button', { name: /^登\s*录$/ }).click();
  const token = await response;
  expect(token.ok(), `Login for ${username} returned ${token.status()}`).toBeTruthy();
  await expect(page).toHaveURL(/\/dashboard$/);
}

function row(page: Page, identity: string) {
  return page.getByRole('row').filter({ has: page.getByRole('cell', { name: identity, exact: true }) });
}

function confirm(dialog: Locator) {
  return dialog.getByRole('button', { name: /^确\s*[认定]$/ }).click();
}

async function openCreate(page: Page, title: string) {
  await page.getByRole('button', { name: /新\s*建$/ }).click();
  const dialog = page.getByRole('dialog', { name: `新建${title}` });
  await expect(dialog).toBeVisible();
  return dialog;
}

async function create(page: Page, dialog: Locator, path: string) {
  const doc = await post(page, path, () => confirm(dialog));
  await expect(dialog).toBeHidden();
  await expect(row(page, doc.number)).toBeVisible();
  return doc;
}

async function fill(dialog: Locator, label: string, value: string | number, index = 0) {
  await dialog.getByLabel(label, { exact: true }).nth(index).fill(String(value));
}

async function select(page: Page, dialog: Locator, label: string, value: string | RegExp, index = 0) {
  const input = dialog.getByLabel(label, { exact: true }).nth(index);
  const option = page.locator('.ant-select-dropdown:visible .ant-select-item-option')
    .getByText(value, { exact: true }).first();
  // Re-open and retry: async option loading and dropdown re-renders can close the panel mid-click.
  await expect(async () => {
    await input.click();
    await expect(option).toBeVisible({ timeout: 3_000 });
    await option.click({ timeout: 3_000 });
  }).toPass({ timeout: 20_000 });
}

async function addLine(dialog: Locator, index = 0) {
  if (await dialog.getByLabel('数量', { exact: true }).count() <= index) {
    await dialog.getByRole('button', { name: /添加行$/ }).click();
  }
}

async function action(page: Page, doc: Document, prefix: string, verb: string, label: string, status: string) {
  const docRow = row(page, doc.number);
  const result = await post(page, `${prefix}/${doc.id}/${verb}`, () => docRow.getByText(label, { exact: true }).click());
  await expect(docRow.getByRole('cell', { name: status, exact: true })).toBeVisible();
  return result;
}

async function approve(page: Page, doc: Document, prefix: string) {
  const taskRow = row(page, doc.number);
  // Workflow activation is asynchronous. Re-read via the actual approval page until it displays the task.
  await expect(async () => {
    await visit(page, '/approvals', `${prefix}/tasks`);
    await expect(taskRow).toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 45_000 });
  await taskRow.getByText('批准', { exact: true }).click();
  const dialog = page.getByRole('dialog', { name: `批准 ${doc.number}` });
  await fill(dialog, '审批意见', `验收批准 ${doc.number}`);
  await post(page, new RegExp(`^${prefix}/tasks/[^/]+/approve$`), () => confirm(dialog));
  await expect(dialog).toBeHidden();
  await expect(taskRow).toHaveCount(0);
}

async function awaitApproval(page: Page, doc: Document, route: string, prefix: string) {
  await expect(async () => {
    await visit(page, route, `${prefix}/all`);
    await expect(row(page, doc.number).getByRole('cell', { name: '已批准', exact: true }))
      .toBeVisible({ timeout: 1_500 });
  }).toPass({ timeout: 45_000 });
}

async function stock(page: Page, material: string, location: string, quantity: number) {
  await visit(page, '/inventory/balances', '/api/stock-balances/all');
  const balance = row(page, material).filter({ has: page.getByRole('cell', { name: location, exact: true }) });
  await expect(balance.getByRole('cell', { name: String(quantity), exact: true })).toBeVisible();
}

test('真实授权浏览器链：请购审批、收货、库存、生产、销售审批发货与结算', async ({ browser, page, request }) => {
  const ready = await request.get('/health/ready');
  if (process.env.ERP_E2E_BROKER_RECOVERY === 'true') {
    expect(ready.status()).toBe(503);
    expect((await ready.text()).trim()).toBe('Unhealthy');
  } else {
    expect(ready.ok()).toBeTruthy();
    expect((await ready.text()).trim()).toBe('Healthy');
  }

  await page.goto('/inventory/balances');
  await expect(page).toHaveURL(/\/login$/);
  await login(page, 'admin', 'ERP_E2E_ADMIN_PASSWORD');

  const procurement = await browser.newContext({ baseURL: test.info().project.use.baseURL, ignoreHTTPSErrors: true });
  const sales = await browser.newContext({ baseURL: test.info().project.use.baseURL, ignoreHTTPSErrors: true });
  try {
    const purchaseApprover = await procurement.newPage();
    const salesApprover = await sales.newPage();
    await login(purchaseApprover, 'erp-approver', 'ERP_E2E_PROCUREMENT_PASSWORD');
    await login(salesApprover, 'erp-sales-approver', 'ERP_E2E_SALES_PASSWORD');

    const receipt = await test.step('管理员请购、采购经理审批、转单确认及收货过账', async () => {
      const route = '/procurement/requisitions';
      const requisitions = '/api/purchase-requisitions';
      await visit(page, route, `${requisitions}/all`);
      const dialog = await openCreate(page, '请购单');
      await select(page, dialog, '供应商', supplier);
      for (const [index, material, price] of [[0, motor, 3000], [1, housing, 100]] as const) {
        await addLine(dialog, index);
        await select(page, dialog, '物料', material, index);
        await fill(dialog, '数量', 5, index);
        await fill(dialog, '单价', price, index);
        await fill(dialog, '序号', index + 1, index);
      }
      const requisition = await create(page, dialog, requisitions);
      await action(page, requisition, requisitions, 'submit', '提交', '待审批');
      await approve(purchaseApprover, requisition, '/api/approval-center');
      await purchaseApprover.getByRole('tab', { name: '销售审批' }).click();
      await expect(purchaseApprover.getByText('无权查看销售审批', { exact: true })).toBeVisible();
      await awaitApproval(page, requisition, route, requisitions);
      const order = await action(page, requisition, requisitions, 'convert', '转单', '已转单');
      const orders = '/api/purchase-orders';
      await visit(page, '/procurement/orders', `${orders}/all`);
      await action(page, order, orders, 'confirm', '确认', '已确认');

      const receipts = '/api/goods-receipts';
      await visit(page, '/procurement/receipts', `${receipts}/all`);
      const receiptForm = await openCreate(page, '收货单');
      // The current receipt UI requires raw order and order-line IDs. Use the response to the UI's 转单 action.
      await fill(receiptForm, '采购订单ID', order.id);
      await select(page, receiptForm, '供应商', supplier);
      await select(page, receiptForm, '仓库', warehouse);
      for (const [index, material, price] of [[0, motor, 3000], [1, housing, 100]] as const) {
        await addLine(receiptForm, index);
        const line = order.lines.find((item) => item.position === index + 1);
        expect(line, `采购订单第 ${index + 1} 行`).toBeDefined();
        await fill(receiptForm, '采购订单行ID', line!.id, index);
        await select(page, receiptForm, '物料', material, index);
        await select(page, receiptForm, '库位', rawLocation, index);
        await fill(receiptForm, '数量', 5, index);
        await fill(receiptForm, '单价', price, index);
      }
      const created = await create(page, receiptForm, receipts);
      const posted = await action(page, created, receipts, 'post', '过账', '已过账');
      if (process.env.ERP_E2E_BROKER_RECOVERY === 'true') {
        test.info().annotations.push({ type: 'receipt-id', description: created.id });
      }
      return posted;
    });

    await test.step('库存余额、调拨和盘点过账', async () => {
      await stock(page, motor, rawLocation, 5);
      const transfers = '/api/stock-transfers';
      await visit(page, '/inventory/transfers', `${transfers}/all`);
      const transferForm = await openCreate(page, '调拨单');
      await addLine(transferForm);
      await select(page, transferForm, '物料', motor);
      await select(page, transferForm, '调出仓库', warehouse);
      await select(page, transferForm, '调出库位', rawLocation);
      await select(page, transferForm, '调入仓库', warehouse);
      await select(page, transferForm, '调入库位', transferLocation);
      await fill(transferForm, '数量', 1);
      const transfer = await create(page, transferForm, transfers);
      await action(page, transfer, transfers, 'post', '过账', '已过账');

      const counts = '/api/stock-counts';
      await visit(page, '/inventory/counts', `${counts}/all`);
      const countForm = await openCreate(page, '盘点单');
      await select(page, countForm, '仓库', warehouse);
      await countForm.getByRole('button', { name: /添加行$/ }).click();
      await select(page, countForm, '物料', motor);
      await select(page, countForm, '库位', transferLocation);
      await fill(countForm, '实盘数量', 2);
      const count = await create(page, countForm, counts);
      await action(page, count, counts, 'post', '过账', '已过账');
      await stock(page, motor, transferLocation, 2);
    });

    const workOrder = await test.step('生产工单下达、BOM 领料、实际报工入库', async () => {
      const prefix = '/api/work-orders';
      await visit(page, '/production/work-orders', `${prefix}/all`);
      const workForm = await openCreate(page, '生产工单');
      await select(page, workForm, '生产物料', assembly);
      await fill(workForm, '计划数量', 2);
      const work = await create(page, workForm, prefix);
      await action(page, work, prefix, 'release', '下达', '已下达');
      const workRow = row(page, work.number);
      await workRow.getByText('报工', { exact: true }).click();
      const report = page.getByRole('dialog', { name: `报工 ${work.number}` });
      await fill(report, '报工数量', 2);
      await post(page, `${prefix}/${work.id}/report`, () => confirm(report));
      await expect(workRow.getByRole('cell', { name: '已完工', exact: true })).toBeVisible();
      await workRow.getByRole('button', { name: '展开行' }).click();
      await expect(page.getByText(`${motor}：需求 2，已领 2`, { exact: true })).toBeVisible();
      await expect(page.getByText(/Manual · 数量 2/)).toBeVisible();
      await stock(page, assembly, rawLocation, 2);
      return work;
    });

    const shipment = await test.step('销售经理审批超额订单、选择订单行并发货过账', async () => {
      const route = '/sales/orders';
      const orders = '/api/sales-orders';
      await visit(page, route, `${orders}/all`);
      const salesForm = await openCreate(page, '销售订单');
      await select(page, salesForm, '客户', customer);
      await select(page, salesForm, '物料', assembly);
      await select(page, salesForm, '仓库', warehouse);
      await select(page, salesForm, '库位', rawLocation);
      await fill(salesForm, '数量', 1);
      await fill(salesForm, '单价', 60000);
      const order = await create(page, salesForm, orders);
      await action(page, order, orders, 'submit', '提交', '待审批');
      await approve(salesApprover, order, '/api/sales-approval');
      await salesApprover.getByRole('tab', { name: '请购审批' }).click();
      await expect(salesApprover.getByText('无权查看请购审批', { exact: true })).toBeVisible();
      await awaitApproval(page, order, route, orders);

      const shipments = '/api/sales-shipments';
      await visit(page, '/sales/shipments', `${shipments}/all`);
      const shipForm = await openCreate(page, '销售发货单');
      await select(page, shipForm, '销售订单', order.number);
      await expect(shipForm.getByText(customer, { exact: true })).toBeVisible();
      await select(page, shipForm, '仓库', warehouse);
      await shipForm.getByRole('button', { name: '添加发货行' }).click();
      await select(page, shipForm, '订单行', /SEMI-DRIVE-01.*待发 1/);
      await fill(shipForm, '发货数量', 1);
      const created = await create(page, shipForm, shipments);
      await action(page, created, shipments, 'post', '过账', '已过账');
      await visit(page, route, `${orders}/all`);
      await expect(row(page, order.number).getByRole('cell', { name: '已发货', exact: true })).toBeVisible();
      await stock(page, assembly, rawLocation, 1);
      return created;
    });

    await test.step('库存流水与应收、应付全额结算', async () => {
      const movements = await visit<{ sourceDocId: string; signedQuantity: number }[]>(
        page, '/inventory/transactions', '/api/stock-transactions/all');
      await expect(page.getByRole('columnheader', { name: '变动数量' })).toBeVisible();
      expect(movements.filter((item) => item.sourceDocId === receipt.id).map((item) => item.signedQuantity))
        .toEqual([5, 5]);
      expect(movements.filter((item) => item.sourceDocId === workOrder.id).map((item) => item.signedQuantity).sort())
        .toEqual([-2, -2, 2]);
      expect(movements.filter((item) => item.sourceDocId === shipment.id).map((item) => item.signedQuantity))
        .toEqual([-1]);

      const prefix = '/api/arap-ledger';
      const entries = await visit<{ id: string; sourceDocId: string; amount: number }[]>(page, '/finance/ledger', `${prefix}/all`);
      for (const [doc, direction, amount] of [[receipt, '应付', 15500], [shipment, '应收', 60000]] as const) {
        const entry = entries.find((item) => item.sourceDocId === doc.id);
        expect(entry?.amount).toBe(amount);
        const entryRow = row(page, doc.id);
        await expect(entryRow.getByRole('cell', { name: direction, exact: true })).toBeVisible();
        await expect(entryRow.getByRole('cell', { name: '未结算', exact: true })).toBeVisible();
        await entryRow.getByText('结算', { exact: true }).click();
        const dialog = page.getByRole('dialog', { name: '结算', exact: true });
        await fill(dialog, '结算金额', amount);
        await post(page, `${prefix}/${entry!.id}/settle`, () => confirm(dialog));
        await expect(entryRow.getByRole('cell', { name: '已结清', exact: true })).toBeVisible();
      }
    });
  } finally {
    await Promise.all([procurement.close(), sales.close()]);
  }
});
