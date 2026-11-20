import React from 'react';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { useMasterOptions } from '../../hooks/useMasterOptions';

const movementTypeOptions = [
  { label: '收货', value: 'Receipt' },
  { label: '发料', value: 'Issue' },
  { label: '调拨出', value: 'TransferOut' },
  { label: '调拨入', value: 'TransferIn' },
  { label: '盘盈', value: 'CountGain' },
  { label: '盘亏', value: 'CountLoss' },
];

export default function TransactionList() {
  const options = useMasterOptions();

  const fields: FieldDef[] = [
    { name: 'materialId', label: '物料', type: 'select', options: options.materials },
    { name: 'warehouseId', label: '仓库', type: 'select', options: options.warehouses },
    { name: 'storageLocationId', label: '库位', type: 'select', options: options.locations },
    { name: 'signedQuantity', label: '变动数量', type: 'number', precision: 4, width: 120 },
    { name: 'balanceAfter', label: '变动后余额', type: 'number', precision: 4, width: 120 },
    { name: 'movementType', label: '移动类型', type: 'select', options: movementTypeOptions, width: 100 },
    { name: 'sourceDocType', label: '来源单据类型', width: 140 },
    { name: 'sourceDocId', label: '来源单据 ID', width: 310 },
    { name: 'sourceOperationId', label: '来源操作 ID', width: 310 },
  ];

  return <CrudPage title="库存流水" apiPrefix="/api/stock-transactions" fields={fields} readOnly />;
}