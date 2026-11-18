import React from 'react';
import CrudPage, { type FieldDef } from '../../components/CrudPage';

export default function CustomerList() {
  const fields: FieldDef[] = [
    { name: 'code', label: '客户编码', required: true, width: 130 },
    { name: 'name', label: '客户名称', required: true },
    { name: 'contactName', label: '联系人', width: 100 },
    { name: 'phone', label: '电话', width: 130 },
    { name: 'email', label: '邮箱', hideInTable: true },
    { name: 'address', label: '地址', hideInTable: true },
    { name: 'creditLimit', label: '信用额度', type: 'number', precision: 2, initialValue: 0, width: 120 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="客户"
      apiPrefix="/api/customer"
      fields={fields}
      createPermission="Customer.Create"
      updatePermission="Customer.Update"
      deletePermission="Customer.Delete"
    />
  );
}