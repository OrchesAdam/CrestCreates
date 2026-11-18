import React from 'react';
import { PageContainer } from '@ant-design/pro-components';
import { Result } from 'antd';

export default function JobsPage() {
  return (
    <PageContainer title="定时任务">
      <Result
        status="warning"
        title="定时任务管理 HTTP 面尚未交付"
        subTitle="Quartz 调度器当前仅有服务层能力（ISchedulerService），平台管理 HTTP 投影（G-09 范围）未包含调度任务端点。此为已知框架缺口，待后续立项补齐；不影响 P1 已有业务链。"
      />
    </PageContainer>
  );
}