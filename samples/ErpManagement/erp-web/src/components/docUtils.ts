import { message } from 'antd';
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