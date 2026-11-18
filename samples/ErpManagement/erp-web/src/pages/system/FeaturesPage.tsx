import React, { useRef } from 'react';
import { ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { message, Switch, Tag } from 'antd';
import { api, apiHost, extractError, unwrap } from '../../api/client';

interface FeatureDefDto { name: string; groupName?: string; displayName?: string; defaultValue?: string; valueType?: string | number; isVisible?: boolean; }
interface FeatureValueDto { name: string; value?: string | null; }

interface Row extends FeatureDefDto { current?: string | null; }

export default function FeaturesPage() {
  const actionRef = useRef<ActionType>();

  const columns: ProColumns<Row>[] = [
    { title: '分组', dataIndex: 'groupName', width: 140 },
    { title: '功能', dataIndex: 'displayName', width: 200 },
    { title: '键', dataIndex: 'name', width: 260 },
    { title: '默认值', dataIndex: 'defaultValue', width: 100, render: (_, r) => r.defaultValue ?? '-' },
    {
      title: '当前值', dataIndex: 'current', width: 140,
      render: (_, r) => {
        const isBool = String(r.valueType).toLowerCase().includes('bool') || r.defaultValue === 'true' || r.defaultValue === 'false';
        const val = (r.current ?? r.defaultValue ?? '') === 'true';
        if (!isBool) return <Tag>{r.current ?? r.defaultValue ?? '-'}</Tag>;
        return (
          <Switch checked={val} onChange={async (checked) => {
            try {
              // Feature global management is host-only: use the host-scoped client (no tenant header).
              await apiHost.put(`/api/features/global/${encodeURIComponent(r.name)}`, null, { params: { value: checked ? 'true' : 'false' } });
              message.success('已更新'); actionRef.current?.reload();
            } catch (e) { message.error(extractError(e)); }
          }} />
        );
      },
    },
  ];

  return (
    <ProTable<Row>
      headerTitle="功能开关（全局）" actionRef={actionRef} rowKey="name" columns={columns} search={false}
      pagination={{ pageSize: 20 }}
      request={async () => {
        try {
          const defs = unwrap<FeatureDefDto[]>(await api.get('/api/feature-definitions').then((r) => r.data)) ?? [];
          let overrides: FeatureValueDto[] = [];
          try {
            const hostResp = await apiHost.get('/api/features/global');
            overrides = unwrap<FeatureValueDto[]>(hostResp.data) ?? [];
          } catch { overrides = []; }
          const map = new Map(overrides.map((o) => [o.name, o.value]));
          const data: Row[] = defs.map((d) => ({ ...d, current: map.get(d.name) ?? null }));
          return { data, success: true, total: data.length };
        } catch (e) { message.error(extractError(e)); return { data: [], success: false, total: 0 }; }
      }}
    />
  );
}