import React from 'react';
import CrudPage, { type FieldDef } from '../../components/CrudPage';

export default function UnitList() {
  const fields: FieldDef[] = [
    { name: 'code', label: '单位编码', required: true, width: 140 },
    { name: 'name', label: '单位名称', required: true },
    { name: 'precision', label: '小数精度', type: 'number', initialValue: 2, width: 110 },
  ];

  return (
    <CrudPage
      title="计量单位"
      apiPrefix="/api/unit-of-measure"
      fields={fields}
      createPermission="UnitOfMeasure.Create"
      updatePermission="UnitOfMeasure.Update"
      deletePermission="UnitOfMeasure.Delete"
    />
  );
}