import { useRef } from 'react';
import { ModalForm, ProFormTextArea, ProTable, type ActionType, type ProColumns } from '@ant-design/pro-components';
import { message, Space, Tabs } from 'antd';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import { postDocAction } from '../../components/docUtils';

interface ApprovalTask {
  humanTaskId: string;
  requisitionNumber?: string;
  salesOrderNumber?: string;
  amount: number;
  requesterId: string;
  createdAt: string;
}

const approvalTypes = {
  requisition: { prefix: '/api/approval-center', permission: 'HumanTask', numberField: 'requisitionNumber', numberTitle: '请购单号' },
  sales: { prefix: '/api/sales-approval', permission: 'SalesApproval', numberField: 'salesOrderNumber', numberTitle: '销售订单号' },
} as const;

function ApprovalTasks({ type }: { type: keyof typeof approvalTypes }) {
  const actionRef = useRef<ActionType>();
  const { hasPermission } = useAuth();
  const { prefix, permission, numberField, numberTitle } = approvalTypes[type];
  const reload = () => actionRef.current?.reload();
  const canDecide = hasPermission(`${permission}.Complete`);

  const decisionForm = (task: ApprovalTask, action: 'approve' | 'reject') => {
    const label = action === 'approve' ? '批准' : '驳回';
    return (
      <ModalForm<{ comment: string }>
        key={action}
        title={`${label} ${task[numberField] ?? ''}`}
        trigger={<a style={action === 'reject' ? { color: '#ff4d4f' } : undefined}>{label}</a>}
        modalProps={{ destroyOnClose: true }}
        onFinish={(values) => postDocAction(
          `${prefix}/tasks/${encodeURIComponent(task.humanTaskId)}/${action}`,
          '决策已提交，单据状态异步回写',
          reload,
          { comment: values.comment.trim() },
        )}
      >
        <ProFormTextArea name="comment" label="审批意见"
          rules={[{ required: true, whitespace: true, message: '审批意见必填' }]}
          fieldProps={{ rows: 3, maxLength: 1000, showCount: true }} />
      </ModalForm>
    );
  };

  const columns: ProColumns<ApprovalTask>[] = [
    { title: numberTitle, dataIndex: numberField, width: 180 },
    { title: '金额', dataIndex: 'amount', valueType: 'money', width: 130 },
    { title: '申请人', dataIndex: 'requesterId' },
    { title: '创建时间', dataIndex: 'createdAt', valueType: 'dateTime', width: 170 },
  ];
  if (canDecide) {
    columns.push({
      title: '操作', valueType: 'option', width: 140,
      render: (_, task) => <Space>{decisionForm(task, 'approve')}{decisionForm(task, 'reject')}</Space>,
    });
  }

  return (
    <ProTable<ApprovalTask>
      actionRef={actionRef}
      rowKey="humanTaskId"
      columns={columns}
      search={false}
      pagination={{ pageSize: 10, showSizeChanger: true }}
      request={async () => {
        try {
          const resp = await api.get(`${prefix}/tasks`);
          const payload = unwrap<ApprovalTask[]>(resp.data);
          const data = Array.isArray(payload) ? payload : [];
          return { data, success: true, total: data.length };
        } catch (e) {
          message.error(extractError(e));
          return { data: [], success: false, total: 0 };
        }
      }}
    />
  );
}

export default function ApprovalCenter() {
  const { hasPermission } = useAuth();
  return (
    <Tabs items={[
      { key: 'requisition', label: '请购审批', children: hasPermission('HumanTask.Search') ? <ApprovalTasks type="requisition" /> : '无权查看请购审批' },
      { key: 'sales', label: '销售审批', children: hasPermission('SalesApproval.Search') ? <ApprovalTasks type="sales" /> : '无权查看销售审批' },
    ]} defaultActiveKey={hasPermission('HumanTask.Search') ? 'requisition' : 'sales'} />
  );
}
