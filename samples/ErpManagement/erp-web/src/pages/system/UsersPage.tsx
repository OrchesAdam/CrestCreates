import React, { useRef, useState } from 'react';
import {
  ModalForm, ProFormSelect, ProFormSwitch, ProFormText, ProTable,
  type ActionType, type ProColumns,
} from '@ant-design/pro-components';
import { Button, message, Space, Tag } from 'antd';
import { PlusOutlined } from '@ant-design/icons';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';

interface UserDto { id: string; userName: string; email: string; phone?: string; isActive: boolean; isSuperAdmin: boolean; }

export default function UsersPage() {
  const actionRef = useRef<ActionType>();
  const { tenantId, hasPermission } = useAuth();
  const [roles, setRoles] = useState<{ label: string; value: string }[]>([]);
  const reload = () => actionRef.current?.reload();

  const loadRoles = async () => {
    try {
      const resp = await api.get('/api/roles');
      const data = unwrap<{ id: string; name: string }[]>(resp.data) ?? [];
      setRoles(data.map((r) => ({ label: r.name, value: r.id })));
    } catch { /* ignore */ }
  };

  const canManage = hasPermission('IdentityManagement.Users.Manage');

  const columns: ProColumns<UserDto>[] = [
    { title: '用户名', dataIndex: 'userName', width: 140 },
    { title: '邮箱', dataIndex: 'email' },
    { title: '电话', dataIndex: 'phone', width: 140 },
    { title: '超级管理员', dataIndex: 'isSuperAdmin', width: 110, render: (_, r) => (r.isSuperAdmin ? <Tag color="gold">是</Tag> : '否') },
    { title: '状态', dataIndex: 'isActive', width: 90, render: (_, r) => (r.isActive ? <Tag color="green">启用</Tag> : <Tag>停用</Tag>) },
    {
      title: '操作', valueType: 'option', width: 280,
      render: (_, record) => (
        <Space>
          {canManage && (
            <ModalForm
              title="编辑用户"
              trigger={<a>编辑</a>}
              initialValues={{ email: record.email, phone: record.phone, isSuperAdmin: record.isSuperAdmin }}
              modalProps={{ destroyOnClose: true }}
              onFinish={async (v) => {
                try {
                  await api.put(`/api/users/${record.id}`, v);
                  message.success('已更新'); reload(); return true;
                } catch (e) { message.error(extractError(e)); return false; }
              }}
            >
              <ProFormText name="email" label="邮箱" rules={[{ required: true, type: 'email' }]} />
              <ProFormText name="phone" label="电话" />
              <ProFormSwitch name="isSuperAdmin" label="超级管理员" />
            </ModalForm>
          )}
          {canManage && (
            <a onClick={async () => {
              try {
                await api.patch(`/api/users/${record.id}/active`, null, { params: { isActive: !record.isActive } });
                message.success('状态已切换'); reload();
              } catch (e) { message.error(extractError(e)); }
            }}>{record.isActive ? '停用' : '启用'}</a>
          )}
          {canManage && (
            <ModalForm
              title="分配角色"
              trigger={<a onClick={loadRoles}>分配角色</a>}
              modalProps={{ destroyOnClose: true }}
              onFinish={async (v) => {
                try {
                  await api.post(`/api/users/${record.id}/roles/${v.roleId}`);
                  message.success('角色已分配'); return true;
                } catch (e) { message.error(extractError(e)); return false; }
              }}
            >
              <ProFormSelect name="roleId" label="角色" options={roles} showSearch rules={[{ required: true }]} />
            </ModalForm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <ProTable<UserDto>
      headerTitle="用户管理"
      actionRef={actionRef}
      rowKey="id"
      columns={columns}
      search={false}
      pagination={{ pageSize: 10 }}
      request={async () => {
        try {
          const resp = await api.get('/api/users');
          const data = unwrap<UserDto[]>(resp.data) ?? [];
          return { data, success: true, total: data.length };
        } catch (e) { message.error(extractError(e)); return { data: [], success: false, total: 0 }; }
      }}
      toolBarRender={() => canManage ? [
        <ModalForm
          key="create" title="新建用户"
          trigger={<Button type="primary" icon={<PlusOutlined />}>新建</Button>}
          modalProps={{ destroyOnClose: true }}
          onFinish={async (v) => {
            try {
              await api.post('/api/users', { ...v, tenantId });
              message.success('已创建'); reload(); return true;
            } catch (e) { message.error(extractError(e)); return false; }
          }}
        >
          <ProFormText name="userName" label="用户名" rules={[{ required: true }]} />
          <ProFormText name="email" label="邮箱" rules={[{ required: true, type: 'email' }]} />
          <ProFormText.Password name="password" label="初始密码" rules={[{ required: true, min: 8 }]} />
          <ProFormSwitch name="isSuperAdmin" label="超级管理员" initialValue={false} />
        </ModalForm>,
      ] : []}
    />
  );
}