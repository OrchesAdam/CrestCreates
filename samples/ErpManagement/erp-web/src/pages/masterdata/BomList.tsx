import React, { useRef } from 'react';
import {
  ModalForm,
  ProFormDigit,
  ProFormList,
  ProFormSelect,
  ProFormText,
  ProFormTextArea,
  ProTable,
  type ActionType,
  type ProColumns,
} from '@ant-design/pro-components';
import { Button, message, Popconfirm, Space, Tag } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import { useMasterOptions } from '../../hooks/useMasterOptions';

const statusMeta: Record<string, { text: string; color: string }> = {
  Draft: { text: '草稿', color: 'default' },
  Active: { text: '已启用', color: 'green' },
  Disabled: { text: '已停用', color: 'red' },
};

const ACTIVE = 'Active';

export default function BomList() {
  const actionRef = useRef<ActionType>();
  const options = useMasterOptions();
  const { hasPermission } = useAuth();
  const reload = () => actionRef.current?.reload();

  const materialLabel = (id: string) =>
    options.materials.find((m) => m.value === id)?.label ?? id;

  const columns: ProColumns<Record<string, unknown>>[] = [
    { title: '父项物料', dataIndex: 'materialId', render: (_, r) => materialLabel(String(r.materialId)) },
    { title: '版本', dataIndex: 'version', width: 100 },
    {
      title: '状态',
      dataIndex: 'status',
      width: 100,
      render: (_, r) => {
        const meta = statusMeta[String(r.status)] ?? { text: String(r.status), color: 'default' };
        return <Tag color={meta.color}>{meta.text}</Tag>;
      },
    },
    { title: '行数', dataIndex: 'lines', width: 80, render: (_, r) => (r.lines as unknown[])?.length ?? 0 },
    { title: '创建时间', dataIndex: 'creationTime', valueType: 'dateTime', width: 170 },
    {
      title: '操作',
      valueType: 'option',
      width: 220,
      render: (_, record) => (
        <Space>
          {hasPermission('Bom.Update') && (
            <ModalForm
              title="编辑 BOM"
              trigger={<a>编辑</a>}
              initialValues={{
                version: record.version,
                notes: record.notes,
                lines: (record.lines as Record<string, unknown>[]) ?? [],
              }}
              modalProps={{ destroyOnClose: true }}
              onFinish={async (values) => {
                try {
                  await api.put(`/api/bom/${record.id}`, values);
                  message.success('更新成功');
                  reload();
                  return true;
                } catch (e) {
                  message.error(extractError(e));
                  return false;
                }
              }}
            >
              <ProFormText name="version" label="版本" rules={[{ required: true }]} />
              <ProFormTextArea name="notes" label="备注" />
              <LineFields materialOptions={options.materials} />
            </ModalForm>
          )}
          {hasPermission('Bom.Activate') && record.status !== ACTIVE && (
            <a
              onClick={async () => {
                try {
                  await api.post(`/api/bom/activate/${record.id}`);
                  message.success('已启用');
                  reload();
                } catch (e) {
                  message.error(extractError(e));
                }
              }}
            >
              启用
            </a>
          )}
          {hasPermission('Bom.Activate') && record.status === ACTIVE && (
            <a
              onClick={async () => {
                try {
                  await api.post(`/api/bom/disable/${record.id}`);
                  message.success('已停用');
                  reload();
                } catch (e) {
                  message.error(extractError(e));
                }
              }}
            >
              停用
            </a>
          )}
          {hasPermission('Bom.Delete') && (
            <Popconfirm
              title="确认删除该 BOM？"
              onConfirm={async () => {
                try {
                  await api.delete(`/api/bom/${record.id}`, {
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
    },
  ];

  return (
    <ProTable<Record<string, unknown>>
      headerTitle="BOM（物料清单）"
      actionRef={actionRef}
      rowKey="id"
      columns={columns}
      search={false}
      pagination={{ pageSize: 10 }}
      expandable={{
        expandedRowRender: (record) => (
          <ul style={{ margin: 0, paddingLeft: 20 }}>
            {((record.lines as Record<string, unknown>[]) ?? []).map((line) => (
              <li key={String(line.id)}>
                {materialLabel(String(line.componentMaterialId))} × {String(line.quantity)}
              </li>
            ))}
          </ul>
        ),
      }}
      request={async () => {
        try {
          const resp = await api.get('/api/bom/all');
          const payload = unwrap<Record<string, unknown>[]>(resp.data);
          const data = Array.isArray(payload) ? payload : [];
          return { data, success: true, total: data.length };
        } catch (e) {
          message.error(extractError(e));
          return { data: [], success: false, total: 0 };
        }
      }}
      toolBarRender={() =>
        hasPermission('Bom.Create')
          ? [
              <ModalForm
                key="create"
                title="新建 BOM"
                trigger={
                  <Button type="primary" icon={<PlusOutlined />}>
                    新建
                  </Button>
                }
                modalProps={{ destroyOnClose: true }}
                onFinish={async (values) => {
                  try {
                    await api.post('/api/bom', values);
                    message.success('创建成功');
                    reload();
                    return true;
                  } catch (e) {
                    message.error(extractError(e));
                    return false;
                  }
                }}
              >
                <ProFormSelect
                  name="materialId"
                  label="父项物料"
                  options={options.materials}
                  rules={[{ required: true }]}
                  showSearch
                />
                <ProFormText name="version" label="版本" initialValue="V1.0" rules={[{ required: true }]} />
                <ProFormTextArea name="notes" label="备注" />
                <LineFields materialOptions={options.materials} />
              </ModalForm>,
            ]
          : []
      }
    />
  );
}

function LineFields({ materialOptions }: { materialOptions: { label: string; value: string }[] }) {
  return (
    <ProFormList
      name="lines"
      label="组件行"
      creatorButtonProps={{ creatorButtonText: '添加组件' }}
      min={1}
      copyIconProps={false}
    >
      <Space align="baseline" style={{ display: 'flex' }}>
        <ProFormSelect
          name="componentMaterialId"
          label="组件物料"
          options={materialOptions}
          rules={[{ required: true }]}
          showSearch
          width="md"
        />
        <ProFormDigit name="quantity" label="用量" rules={[{ required: true }]} fieldProps={{ precision: 4, min: 0.0001 }} width="sm" />
        <ProFormDigit name="position" label="序号" initialValue={1} width="xs" />
      </Space>
    </ProFormList>
  );
}