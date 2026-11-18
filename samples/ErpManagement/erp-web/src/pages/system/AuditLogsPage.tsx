import React, { useRef } from 'react';
import { ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { message, Tag } from 'antd';
import { api, extractError, unwrap } from '../../api/client';

interface AuditRow {
  id: string; executionTime: string; duration: number; userId?: string; userName?: string;
  httpMethod?: string; url?: string; serviceName?: string; methodName?: string; status: number; clientIpAddress?: string;
}
interface Paged { items: AuditRow[]; totalCount: number; }

export default function AuditLogsPage() {
  const actionRef = useRef<ActionType>();

  const columns: ProColumns<AuditRow>[] = [
    { title: '关键字', dataIndex: 'keyword', hideInTable: true },
    { title: '状态', dataIndex: 'status', hideInTable: true, valueType: 'select',
      valueEnum: { '0': { text: '成功' }, '1': { text: '失败' } } },
    { title: 'HTTP 方法', dataIndex: 'httpMethod', hideInTable: true, valueType: 'select',
      valueEnum: { GET: { text: 'GET' }, POST: { text: 'POST' }, PUT: { text: 'PUT' }, DELETE: { text: 'DELETE' }, PATCH: { text: 'PATCH' } } },
    { title: '时间', dataIndex: 'executionTime', valueType: 'dateTime', width: 170, search: false },
    { title: '方法', dataIndex: 'httpMethod', width: 80, search: false },
    { title: 'URL', dataIndex: 'url', ellipsis: true, search: false },
    { title: '用户', dataIndex: 'userName', width: 110, search: false },
    { title: '服务/方法', dataIndex: 'serviceName', width: 200, search: false,
      render: (_, r) => `${r.serviceName ?? ''}${r.methodName ? '.' + r.methodName : ''}` },
    { title: '耗时(ms)', dataIndex: 'duration', width: 100, search: false },
    { title: '状态', dataIndex: 'status', width: 90, search: false,
      render: (_, r) => (r.status === 0 ? <Tag color="green">成功</Tag> : <Tag color="red">失败</Tag>) },
    { title: 'IP', dataIndex: 'clientIpAddress', width: 130, search: false },
  ];

  return (
    <ProTable<AuditRow>
      headerTitle="审计日志" actionRef={actionRef} rowKey="id" columns={columns}
      pagination={{ pageSize: 15 }}
      request={async (params) => {
        const pageIndex = (params.current ?? 1) - 1;
        const pageSize = params.pageSize ?? 15;
        try {
          const r = await api.get('/api/audit-logs', {
            params: {
              pageIndex, pageSize,
              keyword: params.keyword || undefined,
              status: params.status ?? undefined,
              httpMethod: params.httpMethod || undefined,
            },
          });
          const p = unwrap<Paged>(r.data);
          return { data: p?.items ?? [], success: true, total: p?.totalCount ?? 0 };
        } catch (e) { message.error(extractError(e)); return { data: [], success: false, total: 0 }; }
      }}
    />
  );
}