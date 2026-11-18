import React from 'react';
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
    {
      name: 'stockEventEmittedAt',
      label: '库存事件时间',
      hideInForm: true,
      tableValueType: 'dateTime',
      width: 170,
    },
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
          {record.status === 'Posted' && hasPermission('GoodsReceipt.Post') && (
            <a
              onClick={() =>
                postDocAction(`/api/goods-receipts/${record.id}/resend-stock-event`, '库存事件已补发', reload)
              }
            >
              补发库存事件
            </a>
          )}
        </>
      )}
    />
  );
}