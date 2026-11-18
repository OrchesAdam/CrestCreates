import React from 'react';
import { ProFormDigit, ProFormList, ProFormSelect } from '@ant-design/pro-components';
import { Space } from 'antd';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, lineArray, postDocAction, withPositions } from '../../components/docUtils';
import { useMasterOptions } from '../../hooks/useMasterOptions';
import { useAuth } from '../../auth/AuthContext';

const statusOptions = [
  { label: '草稿', value: 'Draft' },
  { label: '已确认', value: 'Confirmed' },
  { label: '部分收货', value: 'PartiallyReceived' },
  { label: '已收货', value: 'Received' },
  { label: '已关闭', value: 'Closed' },
];

const closableStatuses = ['Confirmed', 'PartiallyReceived', 'Received'];

export default function OrderList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();

  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    { name: 'requisitionId', label: '来源请购单ID', hideInTable: true },
    { name: 'supplierId', label: '供应商', type: 'select', required: true, options: options.suppliers },
    { name: 'totalAmount', label: '总金额', type: 'number', precision: 2, hideInForm: true, width: 110 },
    { name: 'status', label: '状态', type: 'select', options: statusOptions, hideInForm: true, width: 100 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="采购订单"
      apiPrefix="/api/purchase-orders"
      fields={fields}
      createPermission="PurchaseOrder.Create"
      updatePermission="PurchaseOrder.Update"
      deletePermission="PurchaseOrder.Delete"
      formExtra={
        <ProFormList
          name="lines"
          label="订单行"
          creatorButtonProps={{ creatorButtonText: '添加行' }}
          min={1}
          copyIconProps={false}
        >
          <Space align="baseline" style={{ display: 'flex', flexWrap: 'wrap' }}>
            <ProFormSelect
              name="materialId"
              label="物料"
              options={options.materials}
              rules={[{ required: true }]}
              showSearch
              width="md"
            />
            <ProFormDigit
              name="quantity"
              label="数量"
              rules={[{ required: true }]}
              fieldProps={{ precision: 4, min: 0.0001 }}
              width="sm"
            />
            <ProFormDigit name="unitPrice" label="单价" fieldProps={{ precision: 2, min: 0 }} width="sm" />
            <ProFormSelect name="warehouseId" label="仓库" options={options.warehouses} showSearch width="sm" />
            <ProFormSelect
              name="storageLocationId"
              label="库位"
              options={options.locations}
              showSearch
              width="sm"
            />
            <ProFormDigit name="position" label="序号" initialValue={1} width="xs" />
          </Space>
        </ProFormList>
      }
      transformSubmit={(values) => ({
        requisitionId: values.requisitionId,
        supplierId: values.supplierId,
        notes: values.notes,
        lines: withPositions(values),
      })}
      expandedRowRender={(record) => (
        <ul style={{ margin: 0, paddingLeft: 20 }}>
          {lineArray(record).map((line, index) => (
            <li key={String(line.id ?? index)}>
              {labelOf(options.materials, line.materialId)} × {String(line.quantity)} @ {String(line.unitPrice ?? '-')}
              {' → '}
              {labelOf(options.warehouses, line.warehouseId)}
              {line.storageLocationId ? ` / ${labelOf(options.locations, line.storageLocationId)}` : ''}
            </li>
          ))}
        </ul>
      )}
      rowActions={(record, reload) => (
        <>
          {record.status === 'Draft' && hasPermission('PurchaseOrder.Confirm') && (
            <a onClick={() => postDocAction(`/api/purchase-orders/${record.id}/confirm`, '已确认', reload)}>
              确认
            </a>
          )}
          {closableStatuses.includes(String(record.status)) && hasPermission('PurchaseOrder.Close') && (
            <a onClick={() => postDocAction(`/api/purchase-orders/${record.id}/close`, '已关闭', reload)}>
              关闭
            </a>
          )}
        </>
      )}
    />
  );
}