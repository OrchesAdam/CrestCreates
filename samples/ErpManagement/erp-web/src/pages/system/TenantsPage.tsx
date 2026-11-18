import React, { useRef } from 'react';
import { ModalForm, ProFormText, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { Button, message, Space, Tag } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';

interface TenantDto { id: string; name: string; displayName?: string; isActive: boolean; initializationStatus?: string; }

export default function TenantsPage() {
  const actionRef = useRef<ActionType>();
  const { hasPermission } = useAuth();
  const canManage = hasPermission('TenantManagement.Update');
  const reload = () => actionRef.current?.reload();

  const columns: ProColumns<TenantDto>[] = [
    { title: '租户名', dataIndex: 'name', width: 160 },
    { title: '显示名', dataIndex: 'displayName' },
    { title: '初始化', dataIndex: 'initializationStatus', width: 140 },
    { title: '状态', dataIndex: 'isActive', width: 90, render: (_, r) => (r.isActive ? <Tag color="green">启用</Tag> : <Tag>停用</Tag>) },
    {
      title: '操作', valueType: 'option', width: 140,
      render: (_, record) => (
        <Space>
          {canManage && (
            <a onClick={async () => {
              try { await api.patch(`/api/tenants/${encodeURIComponent(record.name)}/active`, null, { params: { isActive: !record.isActive } }); message.success('状态已切换'); reload(); }
              catch (e) { message.error(extractError(e)); }
            }}>{record.isActive ? '停用' : '启用'}</a>
          )}
        </Space>
      ),
    },
  ];

  return (
    <ProTable<TenantDto>
      headerTitle="租户管理" actionRef={actionRef} rowKey="id" columns={columns} search={false}
      pagination={{ pageSize: 10 }}
      request={async () => {
        try { const r = await api.get('/api/tenants'); const d = unwrap<TenantDto[]>(r.data) ?? []; return { data: d, success: true, total: d.length }; }
        catch (e) { message.error(extractError(e)); return { data: [], success: false, total: 0 }; }
      }}
      toolBarRender={() => canManage ? [
        <ModalForm key="c" title="新建租户" trigger={<Button type="primary" icon={<PlusOutlined />}>新建</Button>}
          modalProps={{ destroyOnClose: true }}
          onFinish={async (v) => {
            try { await api.post('/api/tenants', v); message.success('已创建'); reload(); return true; }
            catch (e) { message.error(extractError(e)); return false; }
          }}>
          <ProFormText name="name" label="租户名" rules={[{ required: true }]} />
          <ProFormText name="displayName" label="显示名" />
        </ModalForm>,
      ] : []}
    />
  );
}