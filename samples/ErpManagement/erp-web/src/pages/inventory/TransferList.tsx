import React from 'react';
import { ProFormDigit, ProFormList, ProFormSelect } from '@ant-design/pro-components';
import { Space } from 'antd';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, lineArray, postDocAction } from '../../components/docUtils';
import { useMasterOptions } from '../../hooks/useMasterOptions';
import { useAuth } from '../../auth/AuthContext';

const statusOptions = [
  { label: '草稿', value: 'Draft' },
  { label: '已过账', value: 'Posted' },
];

export default function TransferList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();

  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    { name: 'status', label: '状态', type: 'select', options: statusOptions, hideInForm: true, width: 100 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="调拨单"
      apiPrefix="/api/stock-transfers"
      fields={fields}
      createPermission="StockTransfer.Create"
      updatePermission="StockTransfer.Update"
      deletePermission="StockTransfer.Delete"
      formExtra={
        <ProFormList
          name="lines"
          label="调拨行"
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
            <ProFormSelect
              name="fromWarehouseId"
              label="调出仓库"
              options={options.warehouses}
              rules={[{ required: true }]}
              showSearch
              width="sm"
            />
            <ProFormSelect
              name="fromStorageLocationId"
              label="调出库位"
              options={options.locations}
              showSearch
              width="sm"
            />
            <ProFormSelect
              name="toWarehouseId"
              label="调入仓库"
              options={options.warehouses}
              rules={[{ required: true }]}
              showSearch
              width="sm"
            />
            <ProFormSelect
              name="toStorageLocationId"
              label="调入库位"
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
          </Space>
        </ProFormList>
      }
      transformSubmit={(values) => ({
        notes: values.notes,
        lines: lineArray(values),
      })}
      expandedRowRender={(record) => (
        <ul style={{ margin: 0, paddingLeft: 20 }}>
          {lineArray(record).map((line, index) => (
            <li key={String(line.id ?? index)}>
              {labelOf(options.materials, line.materialId)} × {String(line.quantity)}：
              {labelOf(options.warehouses, line.fromWarehouseId)}
              {line.fromStorageLocationId ? `/${labelOf(options.locations, line.fromStorageLocationId)}` : ''}
              {' → '}
              {labelOf(options.warehouses, line.toWarehouseId)}
              {line.toStorageLocationId ? `/${labelOf(options.locations, line.toStorageLocationId)}` : ''}
            </li>
          ))}
        </ul>
      )}
      rowActions={(record, reload) => (
        <>
          {record.status === 'Draft' && hasPermission('StockTransfer.Post') && (
            <a onClick={() => postDocAction(`/api/stock-transfers/${record.id}/post`, '已过账', reload)}>
              过账
            </a>
          )}
        </>
      )}
    />
  );
}