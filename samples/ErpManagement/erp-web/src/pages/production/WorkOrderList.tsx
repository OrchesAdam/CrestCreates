import { ModalForm, ProFormDigit } from '@ant-design/pro-components';
import { Button, Space } from 'antd';
import { useAuth } from '../../auth/AuthContext';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, postDocAction, useOperationDocAction } from '../../components/docUtils';
import { useMasterOptions } from '../../hooks/useMasterOptions';

const apiPrefix = '/api/work-orders';
const statusOptions = [
  { label: '计划中', value: 'Planned' },
  { label: '已下达', value: 'Released' },
  { label: '生产中', value: 'InProgress' },
  { label: '已完工', value: 'Completed' },
  { label: '已关闭', value: 'Closed' },
];

interface MaterialRequirement {
  materialId: string;
  requiredQuantity: number;
  issuedQuantity: number;
}

interface WorkReport {
  id?: string;
  reportedQuantity: number;
  reportSource: string;
  reportedAt: string;
}

function getReports(record: Record<string, unknown>): WorkReport[] {
  return Array.isArray(record.reports) ? record.reports : [];
}

export default function WorkOrderList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();
  const submitReport = useOperationDocAction();
  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    { name: 'materialId', label: '生产物料', type: 'select', options: options.materials, required: true },
    { name: 'quantity', label: '计划数量', type: 'number', required: true, precision: 4, min: 0.0001 },
    { name: 'status', label: '状态', hideInForm: true, type: 'select', options: statusOptions },
    { name: 'sourceSalesOrderId', label: '来源销售订单 ID', hideInTable: true },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="生产工单"
      apiPrefix={apiPrefix}
      fields={fields}
      createPermission="WorkOrder.Create"
      updatePermission="WorkOrder.Update"
      deletePermission="WorkOrder.Delete"
      canModifyRow={(record) => record.status === 'Planned'}
      transformSubmit={({ materialId, quantity, notes, sourceSalesOrderId }) => ({
        materialId, quantity, notes, sourceSalesOrderId: sourceSalesOrderId || undefined,
      })}
      expandedRowRender={(record) => {
        const materials: MaterialRequirement[] = Array.isArray(record.materials) ? record.materials : [];
        const reports = getReports(record);
        return (
          <Space direction="vertical" size="small">
            <strong>用料明细</strong>
            {materials.length ? (
              <ul style={{ margin: 0, paddingLeft: 20 }}>
                {materials.map((item) => (
                  <li key={item.materialId}>
                    {labelOf(options.materials, item.materialId)}：需求 {item.requiredQuantity}，已领 {item.issuedQuantity}
                  </li>
                ))}
              </ul>
            ) : '暂无用料'}
            <strong>报工记录</strong>
            {reports.length ? (
              <ul style={{ margin: 0, paddingLeft: 20 }}>
                {reports.map((report, index) => (
                  <li key={report.id ?? index}>
                    {new Date(report.reportedAt).toLocaleString()} · {report.reportSource} · 数量 {report.reportedQuantity}
                  </li>
                ))}
              </ul>
            ) : '暂无报工'}
          </Space>
        );
      }}
      rowActions={(record, reload) => {
        const url = `${apiPrefix}/${record.id}`;
        const reportUrl = `${url}/report`;
        const pendingReport = hasPermission('WorkOrder.Report') ? submitReport.pending(reportUrl) : null;
        const reported = getReports(record).reduce((total, report) => total + report.reportedQuantity, 0);
        const remaining = Number((Number(record.quantity) - reported).toFixed(4));
        return (
          <>
            {record.status === 'Planned' && hasPermission('WorkOrder.Release') && (
              <a onClick={() => postDocAction(`${url}/release`, '工单已下达', reload)}>下达</a>
            )}
            {pendingReport ? (
              <Button type="link" loading={submitReport.isSubmitting(reportUrl)}
                onClick={() => void submitReport.submit(reportUrl, '报工成功', reload)}>
                重试报工（{pendingReport.values.reportedQuantity}）
              </Button>
            ) : (record.status === 'Released' || record.status === 'InProgress') && hasPermission('WorkOrder.Report') && (
              <ModalForm<{ reportedQuantity: number }>
                title={`报工 ${record.number}`}
                trigger={<a>报工</a>}
                modalProps={{ destroyOnClose: true }}
                onFinish={(values) => submitReport.submit(reportUrl, '报工成功', reload, values)}
              >
                <ProFormDigit name="reportedQuantity" label="报工数量" extra={`待报工 ${remaining}`}
                  rules={[{ required: true, type: 'number', min: 0.0001, max: remaining }]}
                  fieldProps={{ precision: 4, min: 0.0001, max: remaining }} />
              </ModalForm>
            )}
            {record.status === 'Completed' && hasPermission('WorkOrder.Close') && (
              <a onClick={() => postDocAction(`${url}/close`, '工单已关闭', reload)}>关闭</a>
            )}
          </>
        );
      }}
    />
  );
}
