import axios from 'axios';

const ACCESS_TOKEN_KEY = 'erp_access_token';
const REFRESH_TOKEN_KEY = 'erp_refresh_token';
const TENANT_KEY = 'erp_tenant_id';

export function getAccessToken(): string | null {
  return localStorage.getItem(ACCESS_TOKEN_KEY);
}

export function getTenantId(): string {
  return localStorage.getItem(TENANT_KEY) || 'host';
}

export function setTenantId(tenantId: string) {
  localStorage.setItem(TENANT_KEY, tenantId);
}

export function saveTokens(accessToken: string, refreshToken?: string) {
  localStorage.setItem(ACCESS_TOKEN_KEY, accessToken);
  if (refreshToken) localStorage.setItem(REFRESH_TOKEN_KEY, refreshToken);
}

export function clearTokens() {
  localStorage.removeItem(ACCESS_TOKEN_KEY);
  localStorage.removeItem(REFRESH_TOKEN_KEY);
}

export const api = axios.create({ baseURL: '/' });

api.interceptors.request.use((config) => {
  const token = getAccessToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  config.headers['X-Tenant-Id'] = getTenantId();
  return config;
});

let refreshing: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
  if (!refreshToken) return null;
  const form = new URLSearchParams({
    grant_type: 'refresh_token',
    refresh_token: refreshToken,
    client_id: 'crestcreates-client',
  });
  const resp = await axios.post('/connect/token', form, {
    headers: { 'X-Tenant-Id': getTenantId() },
  });
  saveTokens(resp.data.access_token, resp.data.refresh_token);
  return resp.data.access_token;
}

api.interceptors.response.use(
  (resp) => resp,
  async (error) => {
    const original = error.config;
    if (error.response?.status === 401 && !original._retry && localStorage.getItem(REFRESH_TOKEN_KEY)) {
      original._retry = true;
      try {
        refreshing = refreshing ?? refreshAccessToken();
        const token = await refreshing;
        refreshing = null;
        if (token) {
          original.headers.Authorization = `Bearer ${token}`;
          return api(original);
        }
      } catch {
        refreshing = null;
      }
      clearTokens();
      window.location.href = '/login';
    }
    return Promise.reject(error);
  },
);

export function extractError(error: unknown): string {
  const e = error as { response?: { data?: { message?: string; details?: string; title?: string } }; message?: string };
  const data = e?.response?.data;
  if (data?.message && data?.details) return `${data.message} ${data.details}`;
  if (data?.message) return data.message;
  if (data?.title) return data.title;
  return e?.message ?? '请求失败';
}

export function unwrap<T>(responseData: unknown): T {
  if (responseData && typeof responseData === 'object' && 'data' in responseData) {
    return (responseData as { data: T }).data;
  }
  return responseData as T;
}

// Host-scoped client: no X-Tenant-Id header, so host-only platform endpoints
// (e.g. feature global management) resolve to the host (null tenant) context.
export const apiHost = axios.create({ baseURL: '/' });
apiHost.interceptors.request.use((config) => {
  const token = getAccessToken();
  if (token) config.headers.Authorization = `Bearer ${token}`;
  return config;
});
