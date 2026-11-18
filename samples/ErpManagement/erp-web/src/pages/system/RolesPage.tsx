import React, { useRef } from 'react';
import { ModalForm, ProFormText, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { Button, message, Space, Tag } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';

interface RoleDto { id: string; name: string; displayName?: string; dataScope?: string; isActive: boolean; }

export default function RolesPage() {
  const actionRef = useRef<ActionType>();
  const { tenantId, hasPermission } = useAuth();
  const canManage = hasPermission('IdentityManagement.Users.Manage');
  const reload = () => actionRef.current?.reload();

  const columns: ProColumns<RoleDto>[] = [
    { title: '角色名', dataIndex: 'name', width: 160 },
    { title: '显示名', dataIndex: 'displayName' },
    { title: '数据范围', dataIndex: 'dataScope', width: 120 },
    { title: '状态', dataIndex: 'isActive', width: 90, render: (_, r) => (r.isActive ? <Tag color="green">启用</Tag> : <Tag>停用</Tag>) },
    {
      title: '操作', valueType: 'option', width: 200,
      render: (_, record) => (
        <Space>
          {canManage && (
            <ModalForm title="编辑角色" trigger={<a>编辑</a>}
              initialValues={{ displayName: record.displayName }}
              modalProps={{ destroyOnClose: true }}
              onFinish={async (v) => {
                try { await api.put(`/api/roles/${record.id}`, v); message.success('已更新'); reload(); return true; }
                catch (e) { message.error(extractError(e)); return false; }
              }}>
              <ProFormText name="displayName" label="显示名" />
            </ModalForm>
          )}
          {canManage && (
            <a onClick={async () => {
              try { await api.patch(`/api/roles/${record.id}/active`, null, { params: { isActive: !record.isActive } }); message.success('状态已切换'); reload(); }
              catch (e) { message.error(extractError(e)); }
            }}>{record.isActive ? '停用' : '启用'}</a>
          )}
        </Space>
      ),
    },
  ];

  return (
    <ProTable<RoleDto>
      headerTitle="角色管理" actionRef={actionRef} rowKey="id" columns={columns} search={false}
      pagination={{ pageSize: 10 }}
      request={async () => {
        try { const r = await api.get('/api/roles'); const d = unwrap<RoleDto[]>(r.data) ?? []; return { data: d, success: true, total: d.length }; }
        catch (e) { message.error(extractError(e)); return { data: [], success: false, total: 0 }; }
      }}
      toolBarRender={() => canManage ? [
        <ModalForm key="c" title="新建角色" trigger={<Button type="primary" icon={<PlusOutlined />}>新建</Button>}
          modalProps={{ destroyOnClose: true }}
          onFinish={async (v) => {
            try { await api.post('/api/roles', { ...v, tenantId }); message.success('已创建'); reload(); return true; }
            catch (e) { message.error(extractError(e)); return false; }
          }}>
          <ProFormText name="name" label="角色名" rules={[{ required: true }]} />
          <ProFormText name="displayName" label="显示名" />
        </ModalForm>,
      ] : []}
    />
  );
}