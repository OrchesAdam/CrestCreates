import React, { useRef, useState } from 'react';
import { ModalForm, ProFormSelect, ProFormText, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { Button, Card, Input, message, Popconfirm, Segmented, Space, Tag } from 'antd';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';

interface GrantDto { permissionName: string; providerType?: number | string; providerKey?: string; scope?: number | string; tenantId?: string; }
type Provider = 'user' | 'role';

export default function PermissionsPage() {
  const actionRef = useRef<ActionType>();
  const { tenantId, hasPermission } = useAuth();
  const canManage = hasPermission('PermissionManagement.Manage');
  const [provider, setProvider] = useState<Provider>('user');
  const [key, setKey] = useState('');
  const [effective, setEffective] = useState<string[]>([]);
  const reload = () => actionRef.current?.reload();

  const base = provider === 'user' ? `/api/permission-grants/user/${encodeURIComponent(key)}` : `/api/permission-grants/role/${encodeURIComponent(key)}`;

  const loadEffective = async () => {
    if (provider !== 'user' || !key) { setEffective([]); return; }
    try {
      const r = await api.get(`/api/permission-grants/effective/${encodeURIComponent(key)}`, { params: { tenantId } });
      setEffective(unwrap<{ permissions: string[] }>(r.data)?.permissions ?? []);
    } catch { setEffective([]); }
  };

  const columns: ProColumns<GrantDto>[] = [
    { title: '权限名', dataIndex: 'permissionName' },
    { title: '范围', dataIndex: 'scope', width: 120, render: (_, r) => <Tag>{String(r.scope)}</Tag> },
    { title: '租户', dataIndex: 'tenantId', width: 300 },
    {
      title: '操作', valueType: 'option', width: 100,
      render: (_, record) => canManage ? (
        <Popconfirm title="撤销该权限？" onConfirm={async () => {
          try { await api.delete(base, { data: { permissionName: record.permissionName, scope: record.scope, tenantId: record.tenantId } }); message.success('已撤销'); reload(); loadEffective(); }
          catch (e) { message.error(extractError(e)); }
        }}><a style={{ color: '#ff4d4f' }}>撤销</a></Popconfirm>
      ) : null,
    },
  ];

  return (
    <Card>
      <Space style={{ marginBottom: 16 }}>
        <Segmented options={[{ label: '按用户', value: 'user' }, { label: '按角色', value: 'role' }]}
          value={provider} onChange={(v) => { setProvider(v as Provider); setEffective([]); }} />
        <Input style={{ width: 320 }} placeholder={provider === 'user' ? '用户 ID(GUID)' : '角色名'}
          value={key} onChange={(e) => setKey(e.target.value)} />
        <Button onClick={() => { reload(); loadEffective(); }}>加载</Button>
      </Space>
      <ProTable<GrantDto>
        headerTitle={`授权列表（${provider === 'user' ? '用户' : '角色'}）`}
        actionRef={actionRef} rowKey="permissionName" columns={columns} search={false} pagination={{ pageSize: 10 }}
        request={async () => {
          if (!key) return { data: [], success: true, total: 0 };
          try { const r = await api.get(base); const d = unwrap<GrantDto[]>(r.data) ?? []; return { data: d, success: true, total: d.length }; }
          catch (e) { message.error(extractError(e)); return { data: [], success: false, total: 0 }; }
        }}
        toolBarRender={() => canManage && key ? [
          <ModalForm key="g" title="授予权限" trigger={<Button type="primary">授予权限</Button>}
            modalProps={{ destroyOnClose: true }}
            onFinish={async (v) => {
              try {
                await api.post(base, { permissionName: v.permissionName, scope: v.scope, tenantId: v.scope === 'Tenant' ? tenantId : null });
                message.success('已授予'); reload(); loadEffective(); return true;
              } catch (e) { message.error(extractError(e)); return false; }
            }}>
            <ProFormText name="permissionName" label="权限名" rules={[{ required: true }]} placeholder="如 IdentityManagement.Users.Read" />
            <ProFormSelect name="scope" label="范围" initialValue="Tenant"
              options={[{ label: '全局 Global', value: 'Global' }, { label: '租户 Tenant', value: 'Tenant' }]} />
          </ModalForm>,
        ] : []}
      />
      {provider === 'user' && effective.length > 0 && (
        <Card size="small" title="有效权限" style={{ marginTop: 16 }}>
          {effective.map((p) => <Tag key={p} color="blue">{p}</Tag>)}
        </Card>
      )}
    </Card>
  );
}