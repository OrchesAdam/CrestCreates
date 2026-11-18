import React from 'react';
import { Tabs } from 'antd';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { useMasterOptions } from '../../hooks/useMasterOptions';

export default function WarehouseList() {
  const options = useMasterOptions();

  const warehouseFields: FieldDef[] = [
    { name: 'code', label: '仓库编码', required: true, width: 140 },
    { name: 'name', label: '仓库名称', required: true },
    { name: 'address', label: '地址' },
    { name: 'allowNegativeStock', label: '允许负库存', type: 'switch', initialValue: false, width: 120 },
  ];

  const locationFields: FieldDef[] = [
    { name: 'warehouseId', label: '所属仓库', type: 'select', required: true, options: options.warehouses },
    { name: 'code', label: '库位编码', required: true, width: 140 },
    { name: 'name', label: '库位名称', required: true },
  ];

  return (
    <Tabs
      defaultActiveKey="warehouses"
      items={[
        {
          key: 'warehouses',
          label: '仓库',
          children: (
            <CrudPage
              title="仓库"
              apiPrefix="/api/warehouse"
              fields={warehouseFields}
              createPermission="Warehouse.Create"
              updatePermission="Warehouse.Update"
              deletePermission="Warehouse.Delete"
            />
          ),
        },
        {
          key: 'locations',
          label: '库位',
          children: (
            <CrudPage
              title="库位"
              apiPrefix="/api/storage-location"
              fields={locationFields}
              createPermission="StorageLocation.Create"
              updatePermission="StorageLocation.Update"
              deletePermission="StorageLocation.Delete"
            />
          ),
        },
      ]}
    />
  );
}