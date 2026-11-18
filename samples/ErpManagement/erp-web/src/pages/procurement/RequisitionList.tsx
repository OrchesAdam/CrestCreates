import React from 'react';
import { ProFormDigit, ProFormList, ProFormSelect } from '@ant-design/pro-components';
import { Space } from 'antd';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, lineArray, postDocAction, withPositions } from '../../components/docUtils';
import { useMasterOptions } from '../../hooks/useMasterOptions';
import { useAuth } from '../../auth/AuthContext';

const statusOptions = [
  { label: '草稿', value: 'Draft' },
  { label: '待审批', value: 'PendingApproval' },
  { label: '已批准', value: 'Approved' },
  { label: '已驳回', value: 'Rejected' },
  { label: '已转单', value: 'Converted' },
];

export default function RequisitionList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();

  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    { name: 'requesterId', label: '请购人', hideInTable: true },
    { name: 'supplierId', label: '供应商', type: 'select', options: options.suppliers },
    { name: 'totalAmount', label: '总金额', type: 'number', precision: 2, hideInForm: true, width: 110 },
    { name: 'status', label: '状态', type: 'select', options: statusOptions, hideInForm: true, width: 100 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="请购单"
      apiPrefix="/api/purchase-requisitions"
      fields={fields}
      createPermission="PurchaseRequisition.Create"
      updatePermission="PurchaseRequisition.Update"
      deletePermission="PurchaseRequisition.Delete"
      formExtra={
        <ProFormList
          name="lines"
          label="请购行"
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
            <ProFormDigit
              name="quantity"
              label="数量"
              rules={[{ required: true }]}
              fieldProps={{ precision: 4, min: 0.0001 }}
              width="sm"
            />
            <ProFormDigit name="unitPrice" label="单价" fieldProps={{ precision: 2, min: 0 }} width="sm" />
            <ProFormDigit name="position" label="序号" initialValue={1} width="xs" />
          </Space>
        </ProFormList>
      }
      transformSubmit={(values) => ({
        requesterId: values.requesterId,
        supplierId: values.supplierId,
        notes: values.notes,
        lines: withPositions(values),
      })}
      expandedRowRender={(record) => (
        <ul style={{ margin: 0, paddingLeft: 20 }}>
          {lineArray(record).map((line, index) => (
            <li key={String(line.id ?? index)}>
              {labelOf(options.materials, line.materialId)} × {String(line.quantity)} @ {String(line.unitPrice ?? '-')}
            </li>
          ))}
        </ul>
      )}
      rowActions={(record, reload) => (
        <>
          {record.status === 'Draft' && hasPermission('PurchaseRequisition.Submit') && (
            <a
              onClick={() =>
                postDocAction(`/api/purchase-requisitions/${record.id}/submit`, '已提交审批', reload)
              }
            >
              提交
            </a>
          )}
          {record.status === 'Approved' && hasPermission('PurchaseRequisition.Convert') && (
            <a
              onClick={() =>
                postDocAction(`/api/purchase-requisitions/${record.id}/convert`, '已转采购订单', reload)
              }
            >
              转单
            </a>
          )}
        </>
      )}
    />
  );
}