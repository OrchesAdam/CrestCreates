import React from 'react';
import { Result } from 'antd';

export default function PhasePlaceholder({ phase, title }: { phase: string; title: string }) {
  return (
    <Result
      status="info"
      title={title}
      subTitle={`该功能在 ${phase} 分期交付，当前为菜单占位。`}
    />
  );
}