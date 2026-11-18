import React from 'react';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { useMasterOptions } from '../../hooks/useMasterOptions';

export default function CategoryList() {
  const options = useMasterOptions();

  const fields: FieldDef[] = [
    { name: 'code', label: '分类编码', required: true, width: 140 },
    { name: 'name', label: '分类名称', required: true },
    { name: 'parentId', label: '上级分类', type: 'select', options: options.categories },
    { name: 'sortOrder', label: '排序', type: 'number', initialValue: 0, width: 90 },
  ];

  return (
    <CrudPage
      title="物料分类"
      apiPrefix="/api/material-category"
      fields={fields}
      createPermission="MaterialCategory.Create"
      updatePermission="MaterialCategory.Update"
      deletePermission="MaterialCategory.Delete"
    />
  );
}