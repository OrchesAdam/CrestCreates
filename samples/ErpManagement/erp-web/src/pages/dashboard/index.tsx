import { useEffect, useState } from 'react';
import { PageContainer } from '@ant-design/pro-components';
import { Alert, Card, Col, Row, Statistic } from 'antd';
import { Link } from 'react-router-dom';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';

interface DashboardSummary {
  pendingRequisitionCount: number;
  lowStockMaterialCount: number;
  monthlyPurchaseAmount: number;
  monthlySalesAmount: number;
  outstandingReceivableAmount: number;
  outstandingPayableAmount: number;
}

const metrics = [
  { key: 'pendingRequisitionCount', title: '待审批请购数', precision: 0 },
  { key: 'lowStockMaterialCount', title: '低库存物料', precision: 0 },
  { key: 'monthlyPurchaseAmount', title: '本月采购额', precision: 2 },
  { key: 'monthlySalesAmount', title: '本月销售额', precision: 2 },
  { key: 'outstandingReceivableAmount', title: '应收余额', precision: 2 },
  { key: 'outstandingPayableAmount', title: '应付余额', precision: 2 },
] as const;

export default function Dashboard() {
  const { hasPermission, tenantId } = useAuth();
  const [summary, setSummary] = useState<DashboardSummary>();
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();

  useEffect(() => {
    let active = true;
    setSummary(undefined);
    setError(undefined);
    setLoading(true);
    api.get('/api/dashboard/summary')
      .then(({ data }) => {
        if (active) setSummary(unwrap<DashboardSummary>(data));
      })
      .catch((failure: unknown) => {
        if (active) setError(extractError(failure));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [tenantId]);

  const salesApprovalEntry = hasPermission('SalesApproval.Search') && !hasPermission('HumanTask.Search');
  return (
    <PageContainer title="经营看板" extra={salesApprovalEntry ? <Link to="/approvals">销售审批</Link> : undefined}>
      {error && <Alert type="error" showIcon message="看板加载失败" description={error} style={{ marginBottom: 16 }} />}
      <Row gutter={[16, 16]}>
        {metrics.map(({ key, title, precision }) => (
          <Col key={key} xs={24} sm={12} lg={8}>
            <Card>
              <Statistic title={title} loading={loading} value={summary?.[key] ?? '-'} precision={precision} />
            </Card>
          </Col>
        ))}
      </Row>
    </PageContainer>
  );
}
