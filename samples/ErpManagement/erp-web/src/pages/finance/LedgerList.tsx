import { ModalForm, ProFormDigit } from '@ant-design/pro-components';
import { useAuth } from '../../auth/AuthContext';
import CrudPage, { type FieldDef } from '../../components/CrudPage';
import { useOperationDocAction } from '../../components/docUtils';

const apiPrefix = '/api/arap-ledger';
const fields: FieldDef[] = [
  { name: 'direction', label: '方向', type: 'select', options: [
    { label: '应收', value: 'Receivable' }, { label: '应付', value: 'Payable' },
  ] },
  { name: 'sourceDocType', label: '来源类型' },
  { name: 'sourceDocId', label: '来源单据 ID' },
  { name: 'amount', label: '金额', tableValueType: 'money' },
  { name: 'settledAmount', label: '已结金额', tableValueType: 'money' },
  { name: 'status', label: '状态', type: 'select', options: [
    { label: '未结算', value: 'Open' },
    { label: '部分结算', value: 'PartiallySettled' },
    { label: '已结清', value: 'Settled' },
  ] },
  { name: 'dueDate', label: '到期日', tableValueType: 'date' },
];

export default function LedgerList() {
  const { hasPermission } = useAuth();
  const submitSettlement = useOperationDocAction();
  return (
    <CrudPage
      title="应收应付台账"
      apiPrefix={apiPrefix}
      fields={fields}
      readOnly
      rowActions={(record, reload) => {
        if (!hasPermission('ArApLedger.Settle') || (record.status !== 'Open' && record.status !== 'PartiallySettled')) {
          return null;
        }
        const remaining = Number((Number(record.amount) - Number(record.settledAmount)).toFixed(2));
        return (
          <ModalForm<{ amount: number }>
            title="结算"
            trigger={<a>结算</a>}
            modalProps={{ destroyOnClose: true }}
            onFinish={(values) => submitSettlement(`${apiPrefix}/${record.id}/settle`, '结算成功', reload, values)}
          >
            <ProFormDigit name="amount" label="结算金额" extra={`未结余额 ${remaining.toFixed(2)}`}
              rules={[{ required: true, type: 'number', min: 0.01, max: remaining }]}
              fieldProps={{ precision: 2, min: 0.01, max: remaining }} />
          </ModalForm>
        );
      }}
    />
  );
}
