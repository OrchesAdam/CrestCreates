import { useEffect, useRef, useState } from 'react';
import { api, unwrap } from '../../api/client';
import { ProFormDigit, ProFormList, ProFormSelect, ProFormText } from '@ant-design/pro-components';
import { Button, Space } from 'antd';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, lineArray, postDocAction } from '../../components/docUtils';
import { useMasterOptions } from '../../hooks/useMasterOptions';
import { useAuth } from '../../auth/AuthContext';

const statusOptions = [
  { label: '草稿', value: 'Draft' },
  { label: '已过账', value: 'Posted' },
];

const deliveryLabels: Record<string, string> = {
  Pending: '待投递',
  InFlight: '投递中',
  Delivered: '已投递',
  Consumed: '已投递并消费',
  DeadLettered: '投递失败（死信）',
  NotAppended: '未写入投递记录',
};

const retryableFailureCodes = new Set(['ATTEMPT_BUDGET_EXHAUSTED', 'DELIVERY_ATTEMPT_BUDGET_EXHAUSTED']);

interface DeliveryInfo {
  status: string;
  consumed: boolean;
  failureCode?: string | null;
}

function DeliveryStatus({ id, canRetry }: { id: string; canRetry: boolean }) {
  const url = `/api/goods-receipts/${id}`;
  const [delivery, setDelivery] = useState<DeliveryInfo | null>();
  const [retrying, setRetrying] = useState(false);
  const retryInProgress = useRef(false);

  useEffect(() => {
    if (retrying) return;
    const controller = new AbortController();
    let timer: number;
    const load = async () => {
      try {
        const value = unwrap<DeliveryInfo>(
          (await api.get(`${url}/delivery-status`, { signal: controller.signal })).data);
        if (!controller.signal.aborted) setDelivery(value);
      } catch {
        if (!controller.signal.aborted) setDelivery(null);
      } finally {
        if (!controller.signal.aborted) timer = window.setTimeout(load, 3000);
      }
    };
    void load();
    return () => {
      controller.abort();
      window.clearTimeout(timer);
    };
  }, [url, retrying]);

  const status = delivery?.status === 'Delivered' && delivery.consumed ? 'Consumed' : delivery?.status;
  const retryAvailable = canRetry && status === 'DeadLettered' && retryableFailureCodes.has(delivery?.failureCode ?? '');
  const retry = async () => {
    if (retryInProgress.current || !retryAvailable) return;
    retryInProgress.current = true;
    setRetrying(true);
    try {
      await postDocAction(`${url}/retry-delivery`, '已申请重新投递');
    } finally {
      setDelivery(undefined);
      retryInProgress.current = false;
      setRetrying(false);
    }
  };

  return (
    <Space direction="vertical" size={0}>
      <span>{status ? (deliveryLabels[status] ?? status) : delivery === null ? '状态不可用' : '查询中'}</span>
      {delivery?.failureCode && <code>{delivery.failureCode}</code>}
      {(retryAvailable || retrying) && (
        <Button type="link" size="small" loading={retrying} onClick={() => void retry()}>重新投递</Button>
      )}
    </Space>
  );
}

export default function ReceiptList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();
  const canPost = hasPermission('GoodsReceipt.Post');

  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    { name: 'purchaseOrderId', label: '采购订单ID', required: true, hideInTable: true },
    { name: 'supplierId', label: '供应商', type: 'select', required: true, options: options.suppliers },
    { name: 'warehouseId', label: '仓库', type: 'select', required: true, options: options.warehouses },
    { name: 'status', label: '状态', type: 'select', options: statusOptions, hideInForm: true, width: 90 },
    { name: 'postedAt', label: '过账时间', hideInForm: true, tableValueType: 'dateTime', width: 170 },
    { name: 'deliveryStatus', label: '投递状态', hideInForm: true, width: 160,
      render: (record) => record.status === 'Posted' ? <DeliveryStatus id={String(record.id)} canRetry={canPost} /> : '-' },
  ];

  return (
    <CrudPage
      title="收货单"
      apiPrefix="/api/goods-receipts"
      fields={fields}
      createPermission="GoodsReceipt.Create"
      updatePermission="GoodsReceipt.Update"
      deletePermission="GoodsReceipt.Delete"
      formExtra={
        <ProFormList
          name="lines"
          label="收货行"
          creatorButtonProps={{ creatorButtonText: '添加行' }}
          min={1}
          copyIconProps={false}
        >
          <Space align="baseline" style={{ display: 'flex', flexWrap: 'wrap' }}>
            <ProFormText
              name="purchaseOrderLineId"
              label="采购订单行ID"
              rules={[{ required: true }]}
              width="md"
            />
            <ProFormSelect
              name="materialId"
              label="物料"
              options={options.materials}
              rules={[{ required: true }]}
              showSearch
              width="md"
            />
            <ProFormSelect
              name="storageLocationId"
              label="库位"
              options={options.locations}
              showSearch
              width="sm"
            />
            <ProFormDigit
              name="quantity"
              label="数量"
              rules={[{ required: true }]}
              fieldProps={{ precision: 4, min: 0.0001 }}
              width="sm"
            />
            <ProFormDigit name="unitPrice" label="单价" fieldProps={{ precision: 2, min: 0 }} width="sm" />
          </Space>
        </ProFormList>
      }
      transformSubmit={(values) => ({
        purchaseOrderId: values.purchaseOrderId,
        supplierId: values.supplierId,
        warehouseId: values.warehouseId,
        lines: lineArray(values),
      })}
      expandedRowRender={(record) => (
        <ul style={{ margin: 0, paddingLeft: 20 }}>
          {lineArray(record).map((line, index) => (
            <li key={String(line.id ?? index)}>
              {labelOf(options.materials, line.materialId)} × {String(line.quantity)}
              {line.storageLocationId ? ` @ ${labelOf(options.locations, line.storageLocationId)}` : ''}
            </li>
          ))}
        </ul>
      )}
      rowActions={(record, reload) => (
        <>
          {record.status === 'Draft' && canPost && (
            <a onClick={() => postDocAction(`/api/goods-receipts/${record.id}/post`, '已过账', reload)}>
              过账
            </a>
          )}
        </>
      )}
    />
  );
}