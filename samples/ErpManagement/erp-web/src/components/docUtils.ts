import { message } from 'antd';
import { isAxiosError } from 'axios';
import { useEffect, useReducer } from 'react';
import { api, extractError, getAccessToken, tokenUserId, unwrap } from '../api/client';
import { useAuth } from '../auth/AuthContext';
import type { Option } from '../hooks/useMasterOptions';

export async function postDocAction<T = unknown>(
  url: string,
  successText: string | ((data: T) => string),
  reload?: () => void,
  body?: Record<string, unknown>,
): Promise<boolean> {
  try {
    const response = await api.post(url, body);
    message.success(typeof successText === 'function' ? successText(unwrap<T>(response.data)) : successText);
    reload?.();
    return true;
  } catch (e) {
    message.error(extractError(e));
    return false;
  }
}

type OperationValues = Record<string, number>;
type PendingOperation = { operationId: string; values: OperationValues };

const activeOperations = new Set<string>();
const rejectedStatuses = new Set([400, 401, 403, 404, 405, 409, 412, 415, 422, 428, 429]);
const operationLeaseMs = 90_000;
const operationTabId = newOperationId();

function operationLockKey(key: string) {
  return `${key}:lease`;
}

function tryAcquireOperation(key: string): boolean {
  const lockKey = operationLockKey(key);
  const now = Date.now();
  try {
    const current = localStorage.getItem(lockKey);
    if (current) {
      const lease = JSON.parse(current) as { owner: string; expiresAt: number };
      if (lease.expiresAt > now) return false;
    }
    localStorage.setItem(lockKey, JSON.stringify({ owner: operationTabId, expiresAt: now + operationLeaseMs }));
    // A competing tab can overwrite the claim between the read and write; only the final owner proceeds.
    return (JSON.parse(localStorage.getItem(lockKey)!) as { owner: string }).owner === operationTabId;
  } catch {
    return false;
  }
}

function releaseOperation(key: string) {
  const lockKey = operationLockKey(key);
  try {
    const current = localStorage.getItem(lockKey);
    if (current && (JSON.parse(current) as { owner: string }).owner === operationTabId) {
      localStorage.removeItem(lockKey);
    }
  } catch {
    // Storage is unavailable; the bounded lease still expires.
  }
}

function readPendingOperation(key: string): PendingOperation | null {
  const stored = localStorage.getItem(key);
  return stored === null ? null : JSON.parse(stored) as PendingOperation;
}

export function useOperationDocAction() {
  const { operationIdentity } = useAuth();
  const [, refreshActions] = useReducer((version: number) => version + 1, 0);
  const keyFor = (url: string) => operationIdentity &&
    `erp.pending-operation:${JSON.stringify([operationIdentity.userId, operationIdentity.tenantId, url])}`;

  useEffect(() => {
    const refresh = () => refreshActions();
    window.addEventListener('storage', refresh);
    return () => window.removeEventListener('storage', refresh);
  }, []);

  return {
    pending(url: string): PendingOperation | null {
      const key = keyFor(url);
      try {
        return key ? readPendingOperation(key) : null;
      } catch {
        return null;
      }
    },
    isSubmitting(url: string): boolean {
      const key = keyFor(url);
      if (!key) return false;
      if (activeOperations.has(key)) return true;
      try {
        const lease = localStorage.getItem(operationLockKey(key));
        return !!lease && (JSON.parse(lease) as { expiresAt: number }).expiresAt > Date.now();
      } catch {
        return false;
      }
    },
    // Close the form when the action is confirmed or moves to the dedicated retry button.
    async submit(url: string, successText: string, reload: () => void, values?: OperationValues): Promise<boolean> {
      const key = keyFor(url);
      if (!key || !operationIdentity || tokenUserId(getAccessToken()) !== operationIdentity.userId) {
        message.error('无法确认当前用户与租户，请刷新页面后再试。');
        return false;
      }
      if (activeOperations.has(key)) return false;

      let previous: PendingOperation | null;
      let operation: PendingOperation;
      try {
        previous = readPendingOperation(key);
        if (previous) {
          operation = previous;
        } else {
          if (!values) return false;
          operation = { operationId: newOperationId(), values };
          localStorage.setItem(key, JSON.stringify(operation));
        }
      } catch {
        message.error('无法保存提交记录，请检查浏览器存储后再试。');
        return false;
      }

      if (!tryAcquireOperation(key)) {
        if (!previous) localStorage.removeItem(key);
        refreshActions();
        return true;
      }

      activeOperations.add(key);
      let refreshPending = previous !== null;
      const heartbeat = window.setInterval(() => {
        try {
          localStorage.setItem(operationLockKey(key), JSON.stringify({ owner: operationTabId, expiresAt: Date.now() + operationLeaseMs }));
        } catch {
          // The next explicit retry checks storage before sending another request.
        }
      }, 30_000);
      const releaseOnUnload = () => releaseOperation(key);
      window.addEventListener('pagehide', releaseOnUnload);
      if (previous) refreshActions();
      try {
        try {
          await api.post(url, { ...operation.values, operationId: operation.operationId }, {
            erpOperationUserId: operationIdentity.userId,
            erpOperationTenantId: operationIdentity.tenantId,
            timeout: 60_000,
          });
        } catch (error) {
          const status = isAxiosError(error) ? error.response?.status : undefined;
          const rejected = status !== undefined && rejectedStatuses.has(status);
          if (rejected && !previous) {
            localStorage.removeItem(key);
            message.error(extractError(error));
            return false;
          }
          // A rejection on replay can occur before the server checks the original operation.
          refreshPending = true;
          message.error(rejected
            ? `${extractError(error)} 原提交结果仍未确认。`
            : '提交结果未确认，请使用原参数重试。');
          return true;
        }
        localStorage.removeItem(key);
        message.success(successText);
        reload();
        return true;
      } finally {
        window.clearInterval(heartbeat);
        window.removeEventListener('pagehide', releaseOnUnload);
        releaseOperation(key);
        activeOperations.delete(key);
        if (refreshPending) refreshActions();
      }
    },
  };
}

function newOperationId(): string {
  // getRandomValues also works for ERP deployments on an internal HTTP origin.
  const bytes = crypto.getRandomValues(new Uint8Array(16));
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function lineArray(record: Record<string, unknown>): Record<string, unknown>[] {
  return Array.isArray(record.lines) ? (record.lines as Record<string, unknown>[]) : [];
}

export function withPositions(values: Record<string, unknown>): Record<string, unknown>[] {
  return lineArray(values).map((line, index) => ({ ...line, position: line.position ?? index + 1 }));
}

export function labelOf(options: Option[], id: unknown): string {
  const key = String(id ?? '');
  return options.find((o) => o.value === key)?.label ?? key;
}