import React from 'react';
import CrudPage, { type FieldDef } from '../../components/CrudPage';

export default function SupplierList() {
  const fields: FieldDef[] = [
    { name: 'code', label: '供应商编码', required: true, width: 130 },
    { name: 'name', label: '供应商名称', required: true },
    { name: 'contactName', label: '联系人', width: 100 },
    { name: 'phone', label: '电话', width: 130 },
    { name: 'email', label: '邮箱', hideInTable: true },
    { name: 'address', label: '地址', hideInTable: true },
    { name: 'paymentTermsDays', label: '付款账期(天)', type: 'number', initialValue: 30, width: 120 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="供应商"
      apiPrefix="/api/supplier"
      fields={fields}
      createPermission="Supplier.Create"
      updatePermission="Supplier.Update"
      deletePermission="Supplier.Delete"
    />
  );
}