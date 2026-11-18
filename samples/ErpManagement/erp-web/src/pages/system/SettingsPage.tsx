import React, { useRef } from 'react';
import { ModalForm, ProFormText, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { message, Popconfirm, Space, Tag } from 'antd';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';

interface SettingDto {
  name: string; groupName?: string; displayName?: string; description?: string;
  valueType?: string | number; isEncrypted: boolean; value?: string | null; maskedValue?: string | null;
  scope?: string | number; hasValue: boolean;
}

export default function SettingsPage() {
  const actionRef = useRef<ActionType>();
  const { hasPermission } = useAuth();
  const canManage = hasPermission('SettingManagement.Manage');
  const reload = () => actionRef.current?.reload();

  const columns: ProColumns<SettingDto>[] = [
    { title: '分组', dataIndex: 'groupName', width: 140 },
    { title: '名称', dataIndex: 'displayName' },
    { title: '键', dataIndex: 'name', width: 280 },
    {
      title: '值', dataIndex: 'value', width: 220,
      render: (_, r) => r.isEncrypted ? <Tag>***（加密）</Tag> : (r.value ?? <span style={{ color: '#999' }}>（未设置）</span>),
    },
    {
      title: '操作', valueType: 'option', width: 160,
      render: (_, record) => (
        <Space>
          {canManage && !record.isEncrypted && (
            <ModalForm title="修改（当前租户）" trigger={<a>修改</a>}
              initialValues={{ value: record.value ?? '' }}
              modalProps={{ destroyOnClose: true }}
              onFinish={async (v) => {
                try { await api.put(`/api/settings/current-tenant/${encodeURIComponent(record.name)}`, { value: v.value }); message.success('已保存'); reload(); return true; }
                catch (e) { message.error(extractError(e)); return false; }
              }}>
              <ProFormText name="value" label="值" />
            </ModalForm>
          )}
          {canManage && record.hasValue && (
            <Popconfirm title="恢复默认（删除租户覆盖）？" onConfirm={async () => {
              try { await api.delete(`/api/settings/current-tenant/${encodeURIComponent(record.name)}`); message.success('已恢复默认'); reload(); }
              catch (e) { message.error(extractError(e)); }
            }}><a style={{ color: '#ff4d4f' }}>恢复默认</a></Popconfirm>
          )}
        </Space>
      ),
    },
  ];

  return (
    <ProTable<SettingDto>
      headerTitle="参数设置（当前租户）" actionRef={actionRef} rowKey="name" columns={columns} search={false}
      pagination={{ pageSize: 20 }}
      request={async () => {
        try { const r = await api.get('/api/settings/current'); const d = unwrap<SettingDto[]>(r.data) ?? []; return { data: d, success: true, total: d.length }; }
        catch (e) { message.error(extractError(e)); return { data: [], success: false, total: 0 }; }
      }}
    />
  );
}