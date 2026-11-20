import React, { useEffect, useState } from 'react';
import { PageContainer } from '@ant-design/pro-components';
import { Card, Col, Descriptions, Row, Statistic, Tag, Typography } from 'antd';
import { apiHost, extractError, unwrap } from '../../api/client';

type Kind = 'mes' | 'crm';

interface FeatureDto { name: string; displayName?: string; value?: string | null; defaultValue?: string; }

const meta: Record<Kind, {
  feature: string; title: string; endpoint: string; method: string; keyHeader: string;
  body: string; description: string;
}> = {
  mes: {
    feature: 'Erp.MesIntegration',
    title: 'MES 集成',
    endpoint: '/api/integration/mes/work-orders/{workOrderId}/report',
    method: 'POST',
    keyHeader: 'X-Integration-Api-Key',
    body: '{ "operationId": "945af5c8-102e-4eb8-9932-86ab90366cad", "reportedQuantity": 1 }',
    description: '需启用 Erp.MesIntegration 并携带集成 API-Key。每次报工使用新的 UUID，重试时沿用原 UUID 和数量。同一工单的操作 ID 对应不同请求时返回 409。',
  },
  crm: {
    feature: 'Erp.CrmIntegration',
    title: 'CRM 集成',
    endpoint: '/api/integration/crm/customers',
    method: 'POST',
    keyHeader: 'X-Integration-Api-Key',
    body: '{ "code": "C01", "name": "示例客户" }',
    description: 'CRM 客户同步（存在则更新，否则创建）；需 Erp.CrmIntegration 功能开启并携带集成 API-Key。',
  },
};

export default function IntegrationPage({ kind }: { kind: Kind }) {
  const m = meta[kind];
  const [enabled, setEnabled] = useState<boolean | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    (async () => {
      try {
        // Feature global values are host-only: use the host-scoped client (no tenant header).
        const resp = await apiHost.get('/api/features/global');
        const values = unwrap<FeatureValueDto[]>(resp.data) ?? [];
        const found = values.find((f) => f.name === m.feature);
        setEnabled((found?.value ?? '') === 'true');
      } catch (e) { setError(extractError(e)); }
    })();
  }, [m.feature]);

  interface FeatureValueDto { name: string; value?: string | null; }

  return (
    <PageContainer title={m.title}>
      <Row gutter={16}>
        <Col span={8}>
          <Card>
            <Statistic title="功能状态" valueRender={() => (
              enabled === null ? <Tag>未知</Tag> : enabled ? <Tag color="green">已启用</Tag> : <Tag color="red">未启用</Tag>
            )} />
          </Card>
        </Col>
        <Col span={16}>
          <Card title="入站契约">
            <Descriptions column={1} size="small">
              <Descriptions.Item label="端点">{m.method} {m.endpoint}</Descriptions.Item>
              <Descriptions.Item label="认证头">{m.keyHeader}</Descriptions.Item>
              <Descriptions.Item label="请求体"><code>{m.body}</code></Descriptions.Item>
              <Descriptions.Item label="说明">{m.description}</Descriptions.Item>
            </Descriptions>
            {error && <Typography.Text type="danger">{error}</Typography.Text>}
          </Card>
        </Col>
      </Row>
      <Card style={{ marginTop: 16 }} title="出站事件（本地事件演示）">
        <Typography.Paragraph>
          {kind === 'mes'
            ? '工单下达（MesWorkOrderReleased）/ 完工（MesWorkOrderCompleted）时经由本地事件总线对外发布；生产环境可绑定 RabbitMQ 传输（deploy/docker-compose.rabbitmq.yml overlay）。'
            : '客户创建/变更（CrmCustomerChanged）时经由本地事件总线对外发布；生产环境可绑定 RabbitMQ 传输（deploy/docker-compose.rabbitmq.yml overlay）。'}
        </Typography.Paragraph>
      </Card>
    </PageContainer>
  );
}