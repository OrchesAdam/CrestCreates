import { useEffect, useState } from 'react';
import { ProFormDigit, ProFormList, ProFormSelect } from '@ant-design/pro-components';
import { Form, message, Space, Spin } from 'antd';
import { api, extractError, unwrap } from '../../api/client';
import { useAuth } from '../../auth/AuthContext';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { labelOf, lineArray, postDocAction } from '../../components/docUtils';
import { useMasterOptions, type MasterOptions } from '../../hooks/useMasterOptions';

const apiPrefix = '/api/sales-shipments';
const ordersPrefix = '/api/sales-orders';
const shippableStatuses = ['Approved', 'PartiallyShipped'];
const required = [{ required: true }];

interface OrderLine {
  id: string;
  materialId: string;
  warehouseId: string;
  storageLocationId: string;
  quantity: number;
  shippedQuantity: number;
  position: number;
}

interface Order {
  id: string;
  number: string;
  customerId: string;
  status: string;
  lines: OrderLine[];
}

async function requestOrderOptions() {
  try {
    const { data } = await api.get(`${ordersPrefix}/all`);
    return unwrap<Order[]>(data)
      .filter((order) => shippableStatuses.includes(order.status))
      .map((order) => ({ value: order.id, label: order.number }));
  } catch (error) {
    message.error(extractError(error));
    return [];
  }
}

function ShipmentFields({ options }: { options: MasterOptions }) {
  const form = Form.useFormInstance();
  const orderId = Form.useWatch<string>('salesOrderId', form);
  const warehouseId = Form.useWatch<string>('warehouseId', form);
  const [order, setOrder] = useState<Order>();
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!orderId) return;
    let active = true;
    setLoading(true);
    api.get(`${ordersPrefix}/${orderId}`)
      .then(({ data }) => {
        if (!active) return;
        const selected = unwrap<Order>(data);
        if (!selected || !shippableStatuses.includes(selected.status)) throw new Error('该销售订单当前不可发货');
        setOrder(selected);
        form.setFieldValue('customerId', selected.customerId);
      })
      .catch((error: unknown) => {
        if (active) message.error(extractError(error));
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => { active = false; };
  }, [form, orderId]);

  const selectedOrder = order?.id === orderId ? order : undefined;
  const pendingLines = (selectedOrder?.lines ?? []).filter((line) => line.quantity > line.shippedQuantity);
  const warehouses = options.warehouses.filter(({ value }) => pendingLines.some((line) => line.warehouseId === value));
  const orderLineOptions = pendingLines.filter((line) => line.warehouseId === warehouseId).map((line) => ({
    value: line.id,
    label: `#${line.position} ${labelOf(options.materials, line.materialId)} / ${labelOf(options.locations, line.storageLocationId)}（待发 ${Number((line.quantity - line.shippedQuantity).toFixed(4))}）`,
  }));

  return (
    <>
      <ProFormSelect
        name="salesOrderId"
        label="销售订单"
        rules={required}
        request={requestOrderOptions}
        showSearch
        fieldProps={{
          onChange: () => {
            setOrder(undefined);
            setLoading(false);
            form.setFieldsValue({ customerId: undefined, warehouseId: undefined, lines: [] });
          },
        }}
      />
      {loading && <Spin size="small" />}
      <ProFormSelect
        name="customerId"
        label="客户（取自订单）"
        disabled
        options={selectedOrder ? [{ value: selectedOrder.customerId, label: labelOf(options.customers, selectedOrder.customerId) }] : []}
      />
      <ProFormSelect
        name="warehouseId"
        label="仓库"
        options={warehouses}
        rules={required}
        disabled={!selectedOrder}
        showSearch
        fieldProps={{ onChange: () => form.setFieldValue('lines', []) }}
      />
      {orderLineOptions.length > 0 && (
        <ProFormList
          name="lines"
          label="发货行"
          creatorButtonProps={{ creatorButtonText: '添加发货行' }}
          copyIconProps={false}
          rules={[{
            validator: async (_, lines?: { salesOrderLineId?: string; quantity?: number }[]) => {
              for (const { salesOrderLineId, quantity } of lines ?? []) {
                if (quantity == null || !salesOrderLineId) continue;
                const line = pendingLines.find((item) => item.id === salesOrderLineId && item.warehouseId === warehouseId);
                if (!line || quantity > Number((line.quantity - line.shippedQuantity).toFixed(4))) {
                  throw new Error('发货数量不能超过所选仓库的待发数量');
                }
              }
            },
          }]}
        >
          <Space align="baseline" wrap>
            <ProFormSelect name="salesOrderLineId" label="订单行" options={orderLineOptions} rules={required} showSearch width="md" />
            <ProFormDigit name="quantity" label="发货数量" rules={required} fieldProps={{ precision: 4, min: 0.0001 }} width="sm" />
          </Space>
        </ProFormList>
      )}
    </>
  );
}

export default function ShipmentList() {
  const options = useMasterOptions();
  const { hasPermission } = useAuth();
  const fields: FieldDef[] = [
    { name: 'number', label: '单号', hideInForm: true },
    { name: 'salesOrderId', label: '销售订单 ID', hideInForm: true },
    { name: 'customerId', label: '客户', hideInForm: true, render: (record) => labelOf(options.customers, record.customerId) },
    { name: 'warehouseId', label: '仓库', hideInForm: true, render: (record) => labelOf(options.warehouses, record.warehouseId) },
    { name: 'status', label: '状态', hideInForm: true, type: 'select', options: [
      { label: '草稿', value: 'Draft' }, { label: '已过账', value: 'Posted' },
    ] },
    { name: 'postedAt', label: '过账时间', hideInForm: true, tableValueType: 'dateTime', width: 170 },
    { name: 'notes', label: '备注', type: 'textarea', hideInTable: true },
  ];

  return (
    <CrudPage
      title="销售发货单"
      apiPrefix={apiPrefix}
      fields={fields}
      createPermission="SalesShipment.Create"
      updatePermission="SalesShipment.Update"
      deletePermission="SalesShipment.Delete"
      canModifyRow={(record) => record.status === 'Draft'}
      formExtra={<ShipmentFields options={options} />}
      transformSubmit={(values) => {
        const lines = lineArray(values).map(({ salesOrderLineId, quantity }) => ({ salesOrderLineId, quantity }));
        if (!values.customerId) throw new Error('请先加载可发货的销售订单');
        if (!lines.length) throw new Error('请至少添加一条发货行');
        if (new Set(lines.map((line) => line.salesOrderLineId)).size !== lines.length) throw new Error('同一订单行只能添加一次');
        return { salesOrderId: values.salesOrderId, customerId: values.customerId, warehouseId: values.warehouseId, notes: values.notes, lines };
      }}
      expandedRowRender={(record) => (
        <ul style={{ margin: 0, paddingLeft: 20 }}>
          {lineArray(record).map((line) => (
            <li key={String(line.id)}>
              {labelOf(options.materials, line.materialId)} × {String(line.quantity)} / {labelOf(options.locations, line.storageLocationId)}
            </li>
          ))}
        </ul>
      )}
      rowActions={(record, reload) => (
        record.status === 'Draft' && hasPermission('SalesShipment.Post') && hasPermission('SalesOrder.Ship') && (
          <a onClick={() => postDocAction(`${apiPrefix}/${record.id}/post`, '已过账', reload)}>过账</a>
        )
      )}
    />
  );
}
