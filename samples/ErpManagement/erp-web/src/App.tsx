import React from 'react';
import { Navigate, Route, Routes } from 'react-router-dom';
import { Spin } from 'antd';
import { useAuth } from './auth/AuthContext';
import BasicLayout from './layout/BasicLayout';
import LoginPage from './pages/login/index';
import Dashboard from './pages/dashboard/index';
import MaterialList from './pages/masterdata/MaterialList';
import CategoryList from './pages/masterdata/CategoryList';
import UnitList from './pages/masterdata/UnitList';
import WarehouseList from './pages/masterdata/WarehouseList';
import SupplierList from './pages/masterdata/SupplierList';
import CustomerList from './pages/masterdata/CustomerList';
import BomList from './pages/masterdata/BomList';
import RequisitionList from './pages/procurement/RequisitionList';
import OrderList from './pages/procurement/OrderList';
import ReceiptList from './pages/procurement/ReceiptList';
import BalanceList from './pages/inventory/BalanceList';
import TransactionList from './pages/inventory/TransactionList';
import TransferList from './pages/inventory/TransferList';
import CountList from './pages/inventory/CountList';
import SalesOrderList from './pages/sales/OrderList';
import ShipmentList from './pages/sales/ShipmentList';
import WorkOrderList from './pages/production/WorkOrderList';
import LedgerList from './pages/finance/LedgerList';
import ApprovalCenter from './pages/approvals/index';
import UsersPage from './pages/system/UsersPage';
import RolesPage from './pages/system/RolesPage';
import PermissionsPage from './pages/system/PermissionsPage';
import TenantsPage from './pages/system/TenantsPage';
import SettingsPage from './pages/system/SettingsPage';
import FeaturesPage from './pages/system/FeaturesPage';
import AuditLogsPage from './pages/system/AuditLogsPage';
import JobsPage from './pages/system/JobsPage';
import PhasePlaceholder from './pages/PhasePlaceholder';
import IntegrationPage from './pages/integration/IntegrationPage';

function RequireAuth({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, loading } = useAuth();
  if (loading) {
    return (
      <div style={{ display: 'flex', justifyContent: 'center', paddingTop: 200 }}>
        <Spin size="large" />
      </div>
    );
  }
  if (!isAuthenticated) return <Navigate to="/login" replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        path="/"
        element={
          <RequireAuth>
            <BasicLayout />
          </RequireAuth>
        }
      >
        <Route index element={<Navigate to="/dashboard" replace />} />
        <Route path="dashboard" element={<Dashboard />} />
        <Route path="master-data/materials" element={<MaterialList />} />
        <Route path="master-data/categories" element={<CategoryList />} />
        <Route path="master-data/units" element={<UnitList />} />
        <Route path="master-data/warehouses" element={<WarehouseList />} />
        <Route path="master-data/suppliers" element={<SupplierList />} />
        <Route path="master-data/customers" element={<CustomerList />} />
        <Route path="master-data/boms" element={<BomList />} />
        <Route path="procurement/requisitions" element={<RequisitionList />} />
        <Route path="procurement/orders" element={<OrderList />} />
        <Route path="procurement/receipts" element={<ReceiptList />} />
        <Route path="inventory/balances" element={<BalanceList />} />
        <Route path="inventory/transactions" element={<TransactionList />} />
        <Route path="inventory/transfers" element={<TransferList />} />
        <Route path="inventory/counts" element={<CountList />} />
        <Route path="sales/orders" element={<SalesOrderList />} />
        <Route path="sales/shipments" element={<ShipmentList />} />
        <Route path="production/work-orders" element={<WorkOrderList />} />
        <Route path="finance/ledger" element={<LedgerList />} />
        <Route path="sales/*" element={<PhasePlaceholder phase="P2" title="销售管理" />} />
        <Route path="production/*" element={<PhasePlaceholder phase="P2" title="生产管理" />} />
        <Route path="finance/*" element={<PhasePlaceholder phase="P2" title="应收应付" />} />
        <Route path="approvals" element={<ApprovalCenter />} />
        <Route path="system/users" element={<UsersPage />} />
        <Route path="system/roles" element={<RolesPage />} />
        <Route path="system/permissions" element={<PermissionsPage />} />
        <Route path="system/tenants" element={<TenantsPage />} />
        <Route path="system/settings" element={<SettingsPage />} />
        <Route path="system/features" element={<FeaturesPage />} />
        <Route path="system/audit" element={<AuditLogsPage />} />
        <Route path="system/jobs" element={<JobsPage />} />
        <Route path="system/*" element={<PhasePlaceholder phase="P0 后续切片" title="系统管理" />} />
        <Route path="integration/mes" element={<IntegrationPage kind="mes" />} />
        <Route path="integration/crm" element={<IntegrationPage kind="crm" />} />
        <Route path="integration/*" element={<PhasePlaceholder phase="P3" title="集成中心" />} />
      </Route>
      <Route path="*" element={<Navigate to="/" replace />} />
    </Routes>
  );
}