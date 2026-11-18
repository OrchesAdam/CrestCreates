import React from 'react';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { useMasterOptions } from '../../hooks/useMasterOptions';

export default function MaterialList() {
  const options = useMasterOptions();

  const fields: FieldDef[] = [
    { name: 'code', label: '物料编码', required: true, width: 140 },
    { name: 'name', label: '物料名称', required: true },
    { name: 'specification', label: '规格型号', hideInTable: true },
    {
      name: 'materialType',
      label: '物料类型',
      type: 'select',
      required: true,
      initialValue: 'Purchased',
      options: [
        { label: '采购件', value: 'Purchased' },
        { label: '自制件', value: 'Manufactured' },
        { label: '虚拟件', value: 'Virtual' },
      ],
      width: 100,
    },
    { name: 'categoryId', label: '分类', type: 'select', options: options.categories, hideInTable: true },
    { name: 'baseUnitId', label: '基本单位', type: 'select', required: true, options: options.units, hideInTable: true },
    { name: 'defaultWarehouseId', label: '默认仓库', type: 'select', options: options.warehouses, hideInTable: true },
    { name: 'safetyStock', label: '安全库存', type: 'number', precision: 2, initialValue: 0, width: 110 },
    { name: 'leadTimeDays', label: '提前期(天)', type: 'number', initialValue: 0, width: 110 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="物料"
      apiPrefix="/api/material"
      fields={fields}
      createPermission="Material.Create"
      updatePermission="Material.Update"
      deletePermission="Material.Delete"
    />
  );
}