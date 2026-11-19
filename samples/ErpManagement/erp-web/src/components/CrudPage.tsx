import React, { useRef } from 'react';
import {
  ModalForm,
  ProFormDigit,
  ProFormSelect,
  ProFormSwitch,
  ProFormText,
  ProFormTextArea,
  ProTable,
  type ActionType,
  type ProColumns,
} from '@ant-design/pro-components';
import { Button, message, Popconfirm, Space } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { api, extractError, unwrap } from '../api/client';
import { useAuth } from '../auth/AuthContext';

export interface FieldDef {
  name: string;
  label: string;
  type?: 'text' | 'number' | 'select' | 'switch' | 'textarea';
  options?: { label: string; value: string | number | boolean }[];
  required?: boolean;
  hideInTable?: boolean;
  hideInForm?: boolean;
  width?: number;
  initialValue?: unknown;
  precision?: number;
  min?: number;
  tableValueType?: 'dateTime' | 'date' | 'money';
  render?: (record: Record<string, unknown>) => React.ReactNode;
}

export interface CrudPageProps {
  title: string;
  apiPrefix: string;
  fields: FieldDef[];
  createPermission?: string;
  updatePermission?: string;
  deletePermission?: string;
  canModifyRow?: (record: Record<string, unknown>) => boolean;
  readOnly?: boolean;
  formExtra?: React.ReactNode;
  expandedRowRender?: (record: Record<string, unknown>) => React.ReactNode;
  rowActions?: (record: Record<string, unknown>, reload: () => void) => React.ReactNode;
  transformSubmit?: (values: Record<string, unknown>, editing: boolean) => Record<string, unknown>;
}

export default function CrudPage({
  title,
  apiPrefix,
  fields,
  createPermission,
  updatePermission,
  deletePermission,
  canModifyRow,
  readOnly,
  formExtra,
  expandedRowRender,
  rowActions,
  transformSubmit,
}: CrudPageProps) {
  const actionRef = useRef<ActionType>();
  const { hasPermission } = useAuth();
  const reload = () => actionRef.current?.reload();

  const canCreate = !readOnly && (!createPermission || hasPermission(createPermission));
  const canUpdate = !readOnly && (!updatePermission || hasPermission(updatePermission));
  const canDelete = !readOnly && (!deletePermission || hasPermission(deletePermission));

  const columns: ProColumns<Record<string, unknown>>[] = fields
    .filter((f) => !f.hideInTable)
    .map((f) => ({
      title: f.label,
      dataIndex: f.name,
      width: f.width,
      valueType: f.tableValueType ?? (f.type === 'switch' ? 'select' : undefined),
      valueEnum: f.options
        ? Object.fromEntries(f.options.map((o) => [String(o.value), { text: o.label }]))
        : undefined,
      render: f.render ? (_, record) => f.render?.(record) : f.type === 'switch' ? (_, record) => (record[f.name] ? '是' : '否') : undefined,
    }));

  columns.push({
    title: '创建时间',
    dataIndex: 'creationTime',
    valueType: 'dateTime',
    width: 170,
    search: false,
  });

  if (canUpdate || canDelete || rowActions) {
    columns.push({
      title: '操作',
      valueType: 'option',
      width: 160,
      render: (_, record) => (
        <Space key="actions">
          {canUpdate && (canModifyRow?.(record) ?? true) && (
            <ModalForm
              title={`编辑${title}`}
              trigger={<a>编辑</a>}
              initialValues={record}
              modalProps={{ destroyOnClose: true }}
              onFinish={async (values) => {
                try {
                  const body = transformSubmit ? transformSubmit(values, true) : values;
                  await api.put(`${apiPrefix}/${record.id}`, {
                    ...body,
                    concurrencyStamp: record.concurrencyStamp,
                  }, { headers: { 'If-Match': String(record.concurrencyStamp ?? '') } });
                  message.success('更新成功');
                  reload();
                  return true;
                } catch (e) {
                  message.error(extractError(e));
                  return false;
                }
              }}
            >
              {fields.filter((f) => !f.hideInForm).map(renderFormField)}
              {formExtra}
            </ModalForm>
          )}
          {rowActions?.(record, reload)}
          {canDelete && (canModifyRow?.(record) ?? true) && (
            <Popconfirm
              title={`确认删除该${title}？`}
              onConfirm={async () => {
                try {
                  await api.delete(`${apiPrefix}/${record.id}`, {
                    headers: { 'If-Match': String(record.concurrencyStamp ?? '') },
                  });
                  message.success('删除成功');
                  reload();
                } catch (e) {
                  message.error(extractError(e));
                }
              }}
            >
              <a style={{ color: '#ff4d4f' }}>删除</a>
            </Popconfirm>
          )}
        </Space>
      ),
    });
  }

  return (
    <ProTable<Record<string, unknown>>
      headerTitle={title}
      actionRef={actionRef}
      rowKey="id"
      columns={columns}
      search={false}
      pagination={{ pageSize: 10, showSizeChanger: true }}
      expandable={expandedRowRender ? { expandedRowRender } : undefined}
      request={async () => {
        try {
          const resp = await api.get(`${apiPrefix}/all`);
          const payload = unwrap<Record<string, unknown>[]>(resp.data);
          const data = Array.isArray(payload) ? payload : [];
          return { data, success: true, total: data.length };
        } catch (e) {
          message.error(extractError(e));
          return { data: [], success: false, total: 0 };
        }
      }}
      toolBarRender={() =>
        canCreate
          ? [
              <ModalForm
                key="create"
                title={`新建${title}`}
                trigger={
                  <Button type="primary" icon={<PlusOutlined />}>
                    新建
                  </Button>
                }
                modalProps={{ destroyOnClose: true }}
                onFinish={async (values) => {
                  try {
                    const body = transformSubmit ? transformSubmit(values, false) : values;
                    await api.post(apiPrefix, body);
                    message.success('创建成功');
                    reload();
                    return true;
                  } catch (e) {
                    message.error(extractError(e));
                    return false;
                  }
                }}
              >
                {fields.filter((f) => !f.hideInForm).map(renderFormField)}
                {formExtra}
              </ModalForm>,
            ]
          : []
      }
    />
  );
}

function renderFormField(f: FieldDef) {
  const rules = f.required ? [{ required: true, message: `${f.label}必填` }] : [];
  const common = { name: f.name, label: f.label, rules, initialValue: f.initialValue };
  switch (f.type) {
    case 'number':
      return <ProFormDigit key={f.name} {...common} fieldProps={{ precision: f.precision ?? 0, min: f.min }} />;
    case 'select':
      return <ProFormSelect key={f.name} {...common} options={f.options} />;
    case 'switch':
      return <ProFormSwitch key={f.name} {...common} />;
    case 'textarea':
      return <ProFormTextArea key={f.name} {...common} />;
    default:
      return <ProFormText key={f.name} {...common} />;
  }
}