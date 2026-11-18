import { ProFormDigit, ProFormList, ProFormSelect } from '@ant-design/pro-components';
import { Space } from 'antd';
import { useAuth } from '../../auth/AuthContext';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, lineArray, postDocAction, withPositions } from '../../components/docUtils';
import { useMasterOptions } from '../../hooks/useMasterOptions';

const apiPrefix = '/api/sales-orders';
const statusOptions = [
  { label: '草稿', value: 'Draft' },
  { label: '待审批', value: 'PendingApproval' },
  { label: '已批准', value: 'Approved' },
  { label: '已驳回', value: 'Rejected' },
  { label: '部分发货', value: 'PartiallyShipped' },
  { label: '已发货', value: 'Shipped' },
  { label: '已关闭', value: 'Closed' },
];
const closableStatuses = ['Approved', 'PartiallyShipped', 'Shipped'];
const required = [{ required: true }];

export default function SalesOrderList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();
  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true, width: 160 },
    {
      name: 'customerId', label: '客户', type: 'select', required: true, options: options.customers,
      render: (record) => labelOf(options.customers, record.customerId),
    },
    { name: 'status', label: '状态', type: 'select', options: statusOptions, hideInForm: true, width: 100 },
    { name: 'totalAmount', label: '金额', tableValueType: 'money', hideInForm: true, width: 120 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="销售订单"
      apiPrefix={apiPrefix}
      fields={fields}
      createPermission="SalesOrder.Create"
      updatePermission="SalesOrder.Update"
      deletePermission="SalesOrder.Delete"
      canModifyRow={(record) => record.status === 'Draft'}
      formExtra={(
        <ProFormList
          name="lines"
          label="订单行"
          creatorButtonProps={{ creatorButtonText: '添加行' }}
          min={1}
          copyIconProps={false}
          initialValue={[{}]}
        >
          <Space align="baseline" wrap>
            <ProFormSelect name="materialId" label="物料" options={options.materials} rules={required} showSearch width="md" />
            <ProFormSelect name="warehouseId" label="仓库" options={options.warehouses} rules={required} showSearch width="sm" />
            <ProFormSelect name="storageLocationId" label="库位" options={options.locations} rules={required} showSearch width="sm" />
            <ProFormDigit name="quantity" label="数量" rules={required} fieldProps={{ precision: 4, min: 0.0001 }} width="sm" />
            <ProFormDigit name="unitPrice" label="单价" rules={required} fieldProps={{ precision: 2, min: 0 }} width="sm" />
            <ProFormDigit name="position" label="序号" placeholder="按行排序" fieldProps={{ precision: 0, min: 1 }} width="xs" />
          </Space>
        </ProFormList>
      )}
      transformSubmit={(values) => {
        const lines = withPositions(values).map(({ materialId, quantity, unitPrice, position, warehouseId, storageLocationId }) => ({
          materialId, quantity, unitPrice, position, warehouseId, storageLocationId,
        }));
        if (!lines.length) throw new Error('请至少添加一条订单行');
        return { customerId: values.customerId, notes: values.notes, lines };
      }}
      expandedRowRender={(record) => (
        <ul style={{ margin: 0, paddingLeft: 20 }}>
          {lineArray(record).map((line) => (
            <li key={String(line.id)}>
              {labelOf(options.materials, line.materialId)}：订购 {String(line.quantity)}，已发 {String(line.shippedQuantity)}
              {'，仓库/库位 '}{labelOf(options.warehouses, line.warehouseId)} / {labelOf(options.locations, line.storageLocationId)}
            </li>
          ))}
        </ul>
      )}
      rowActions={(record, reload) => (
        <>
          {record.status === 'Draft' && hasPermission('SalesOrder.Submit') && (
            <a onClick={() => postDocAction<{ status: string }>(
              `${apiPrefix}/${record.id}/submit`,
              (data) => data?.status === 'PendingApproval' ? '已提交，等待销售审批' : '订单已提交',
              reload,
            )}>
              提交
            </a>
          )}
          {closableStatuses.includes(String(record.status)) && hasPermission('SalesOrder.Close') && (
            <a onClick={() => postDocAction(`${apiPrefix}/${record.id}/close`, '订单已关闭', reload)}>关闭</a>
          )}
        </>
      )}
    />
  );
}
