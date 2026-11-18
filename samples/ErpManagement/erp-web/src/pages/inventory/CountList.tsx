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

export default function CountList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();

  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    { name: 'warehouseId', label: '仓库', type: 'select', required: true, options: options.warehouses },
    { name: 'status', label: '状态', type: 'select', options: statusOptions, hideInForm: true, width: 100 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="盘点单"
      apiPrefix="/api/stock-counts"
      fields={fields}
      createPermission="StockCount.Create"
      updatePermission="StockCount.Update"
      deletePermission="StockCount.Delete"
      formExtra={
        <ProFormList
          name="lines"
          label="盘点行"
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
              name="storageLocationId"
              label="库位"
              options={options.locations}
              showSearch
              width="sm"
            />
            <ProFormDigit
              name="bookQuantity"
              label="账面数量"
              rules={[{ required: true }]}
              fieldProps={{ precision: 4 }}
              width="sm"
            />
            <ProFormDigit
              name="countedQuantity"
              label="实盘数量"
              rules={[{ required: true }]}
              fieldProps={{ precision: 4 }}
              width="sm"
            />
          </Space>
        </ProFormList>
      }
      transformSubmit={(values) => ({
        warehouseId: values.warehouseId,
        notes: values.notes,
        lines: lineArray(values),
      })}
      expandedRowRender={(record) => (
        <ul style={{ margin: 0, paddingLeft: 20 }}>
          {lineArray(record).map((line, index) => (
            <li key={String(line.id ?? index)}>
              {labelOf(options.materials, line.materialId)}
              {line.storageLocationId ? ` @ ${labelOf(options.locations, line.storageLocationId)}` : ''}：
              账面 {String(line.bookQuantity)} / 实盘 {String(line.countedQuantity)}
            </li>
          ))}
        </ul>
      )}
      rowActions={(record, reload) => (
        <>
          {record.status === 'Draft' && hasPermission('StockCount.Post') && (
            <a onClick={() => postDocAction(`/api/stock-counts/${record.id}/post`, '已过账', reload)}>
              过账
            </a>
          )}
        </>
      )}
    />
  );
}