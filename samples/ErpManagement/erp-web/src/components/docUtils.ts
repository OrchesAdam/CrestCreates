import { message } from 'antd';
import { useRef } from 'react';
import { api, extractError, unwrap } from '../api/client';
import type { Option } from '../hooks/useMasterOptions';

export async function postDocAction<T = unknown>(
  url: string,
  successText: string | ((data: T) => string),
  reload: () => void,
  body?: Record<string, unknown>,
): Promise<boolean> {
  try {
    const response = await api.post(url, body);
    message.success(typeof successText === 'function' ? successText(unwrap<T>(response.data)) : successText);
    reload();
    return true;
  } catch (e) {
    message.error(extractError(e));
    return false;
  }
}

// Retain an operation ID across failed submissions for the same document and value.
export function useOperationDocAction() {
  const pendingIds = useRef(new Map<string, string>());
  return async (url: string, successText: string, reload: () => void, body: Record<string, number>) => {
    const key = JSON.stringify([url, body]);
    const operationId = pendingIds.current.get(key) ?? newOperationId();
    pendingIds.current.set(key, operationId);
    const succeeded = await postDocAction(url, successText, reload, { ...body, operationId });
    if (succeeded && pendingIds.current.get(key) === operationId) pendingIds.current.delete(key);
    return succeeded;
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