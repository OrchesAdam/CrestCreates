import React, { useEffect, useState } from 'react';
import { api, unwrap } from '../../api/client';
import { ProFormDigit, ProFormList, ProFormSelect, ProFormText } from '@ant-design/pro-components';
import { Space } from 'antd';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, lineArray, postDocAction } from '../../components/docUtils';
import { useMasterOptions } from '../../hooks/useMasterOptions';
import { useAuth } from '../../auth/AuthContext';

const statusOptions = [
  { label: '草稿', value: 'Draft' },
  { label: '已过账', value: 'Posted' },
];

function DeliveryStatus({ id }: { id: string }) {
  const [status, setStatus] = useState('查询中');
  useEffect(() => {
    let active = true;
    const load = async () => {
      try {
        const value = unwrap<{ status: string; consumed: boolean }>(
          (await api.get(`/api/goods-receipts/${id}/delivery-status`)).data);
        if (active) setStatus(value.consumed ? '已投递并消费' : value.status === 'Delivered' ? '已投递' : '待投递');
      } catch { if (active) setStatus('状态不可用'); }
    };
    void load();
    const timer = window.setInterval(load, 3000);
    return () => { active = false; window.clearInterval(timer); };
  }, [id]);
  return <span>{status}</span>;
}

export default function ReceiptList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();

  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    { name: 'purchaseOrderId', label: '采购订单ID', required: true, hideInTable: true },
    { name: 'supplierId', label: '供应商', type: 'select', required: true, options: options.suppliers },
    { name: 'warehouseId', label: '仓库', type: 'select', required: true, options: options.warehouses },
    { name: 'status', label: '状态', type: 'select', options: statusOptions, hideInForm: true, width: 90 },
    { name: 'postedAt', label: '过账时间', hideInForm: true, tableValueType: 'dateTime', width: 170 },
    { name: 'deliveryStatus', label: '投递状态', hideInForm: true, width: 120,
      render: (record) => record.status === 'Posted' ? <DeliveryStatus id={String(record.id)} /> : '-' },
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
          {record.status === 'Draft' && hasPermission('GoodsReceipt.Post') && (
            <a onClick={() => postDocAction(`/api/goods-receipts/${record.id}/post`, '已过账', reload)}>
              过账
            </a>
          )}
        </>
      )}
    />
  );
}