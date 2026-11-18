import React from 'react';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { useMasterOptions } from '../../hooks/useMasterOptions';

export default function BalanceList() {
  const options = useMasterOptions();

  const fields: FieldDef[] = [
    { name: 'materialId', label: '物料', type: 'select', options: options.materials },
    { name: 'warehouseId', label: '仓库', type: 'select', options: options.warehouses },
    { name: 'storageLocationId', label: '库位', type: 'select', options: options.locations },
    { name: 'quantity', label: '数量', type: 'number', precision: 4, width: 140 },
  ];

  return <CrudPage title="库存余额" apiPrefix="/api/stock-balances" fields={fields} readOnly />;
}