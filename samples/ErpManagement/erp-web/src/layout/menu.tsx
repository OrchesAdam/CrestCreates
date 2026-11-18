import React from 'react';
import {
  AppstoreOutlined,
  DashboardOutlined,
  DatabaseOutlined,
  SettingOutlined,
  ShoppingCartOutlined,
  InboxOutlined,
  AccountBookOutlined,
  ToolOutlined,
  AuditOutlined,
  TeamOutlined,
  SafetyOutlined,
  ApartmentOutlined,
  ControlOutlined,
  FlagOutlined,
  FileSearchOutlined,
  ClockCircleOutlined,
  ApiOutlined,
} from '@ant-design/icons';
import type { MenuDataItem } from '@ant-design/pro-components';

export interface ErpMenuItem extends MenuDataItem {
  permission?: string;
  children?: ErpMenuItem[];
}

export const menuTree: ErpMenuItem[] = [
  { path: '/dashboard', name: '经营看板', icon: <DashboardOutlined /> },
  {
    path: '/master-data',
    name: '基础资料',
    icon: <DatabaseOutlined />,
    children: [
      { path: '/master-data/materials', name: '物料', permission: 'Material.Search' },
      { path: '/master-data/categories', name: '物料分类', permission: 'MaterialCategory.Search' },
      { path: '/master-data/units', name: '计量单位', permission: 'UnitOfMeasure.Search' },
      { path: '/master-data/warehouses', name: '仓库与库位', permission: 'Warehouse.Search' },
      { path: '/master-data/suppliers', name: '供应商', permission: 'Supplier.Search' },
      { path: '/master-data/customers', name: '客户', permission: 'Customer.Search' },
      { path: '/master-data/boms', name: 'BOM', permission: 'Bom.Search' },
    ],
  },
  {
    path: '/procurement',
    name: '采购管理',
    icon: <ShoppingCartOutlined />,
    children: [
      { path: '/procurement/requisitions', name: '请购单', permission: 'PurchaseRequisition.Search' },
      { path: '/procurement/orders', name: '采购订单', permission: 'PurchaseOrder.Search' },
      { path: '/procurement/receipts', name: '收货单', permission: 'GoodsReceipt.Search' },
    ],
  },
  {
    path: '/inventory',
    name: '库存管理',
    icon: <InboxOutlined />,
    children: [
      { path: '/inventory/balances', name: '库存余额', permission: 'StockBalance.Search' },
      { path: '/inventory/transactions', name: '库存流水', permission: 'StockTransaction.Search' },
      { path: '/inventory/transfers', name: '调拨单', permission: 'StockTransfer.Search' },
      { path: '/inventory/counts', name: '盘点单', permission: 'StockCount.Search' },
    ],
  },
  {
    path: '/sales',
    name: '销售管理',
    icon: <AccountBookOutlined />,
    children: [
      { path: '/sales/orders', name: '销售订单', permission: 'SalesOrder.Search' },
      { path: '/sales/shipments', name: '发货单', permission: 'SalesShipment.Search' },
    ],
  },
  {
    path: '/production',
    name: '生产管理',
    icon: <ToolOutlined />,
    children: [
      { path: '/production/work-orders', name: '生产工单', permission: 'WorkOrder.Search' },
    ],
  },
  {
    path: '/finance',
    name: '应收应付',
    icon: <FileSearchOutlined />,
    children: [
      { path: '/finance/ledger', name: '往来台账', permission: 'ArApLedger.Search' },
    ],
  },
  { path: '/approvals', name: '审批中心', icon: <AuditOutlined />, permission: 'HumanTask.Search' },
  {
    path: '/system',
    name: '系统管理',
    icon: <SettingOutlined />,
    children: [
      { path: '/system/users', name: '用户管理', permission: 'IdentityManagement.Users.Read' },
      { path: '/system/roles', name: '角色管理', permission: 'IdentityManagement.Users.Read' },
      { path: '/system/permissions', name: '权限授予', permission: 'PermissionManagement.Read' },
      { path: '/system/tenants', name: '租户管理', permission: 'TenantManagement.View' },
      { path: '/system/settings', name: '参数设置', permission: 'SettingManagement.Read' },
      { path: '/system/features', name: '功能开关', permission: 'FeatureManagement.Read' },
      { path: '/system/audit', name: '审计日志', permission: 'AuditLogging.Read' },
      { path: '/system/jobs', name: '定时任务', permission: 'Scheduling.Jobs' },
    ],
  },
  {
    path: '/integration',
    name: '集成中心',
    icon: <ApiOutlined />,
    children: [
      { path: '/integration/mes', name: 'MES 接入', permission: 'ErpIntegration.Mes' },
      { path: '/integration/crm', name: 'CRM 接入', permission: 'ErpIntegration.Crm' },
    ],
  },
];

export function filterMenu(items: ErpMenuItem[], hasPermission: (p: string) => boolean): ErpMenuItem[] {
  return items
    .map((item) => {
      if (item.children) {
        const children = filterMenu(item.children, hasPermission);
        if (children.length === 0) return null;
        return { ...item, children };
      }
      if (item.permission && !hasPermission(item.permission)) return null;
      return item;
    })
    .filter((item): item is ErpMenuItem => item !== null);
}