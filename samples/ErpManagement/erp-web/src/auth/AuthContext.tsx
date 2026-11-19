import React, { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { api, clearTokens, getAccessToken, getTenantId, setTenantId, unwrap } from '../api/client';
import { login as loginRequest } from '../api/auth';

interface AuthState {
  isAuthenticated: boolean;
  userName: string;
  userId: string;
  tenantId: string;
  permissions: string[];
  loading: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
  hasPermission: (permission: string) => boolean;
  switchTenant: (tenantId: string) => void;
  reloadPermissions: () => Promise<void>;
}

const AuthContext = createContext<AuthState>(null!);

function parseJwt(token: string): Record<string, string> {
  try {
    const payload = token.split('.')[1];
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(json);
  } catch {
    return {};
  }
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const [userName, setUserName] = useState('');
  const [userId, setUserId] = useState('');
  const [tenantId, setTenantIdState] = useState('');
  const [permissions, setPermissions] = useState<string[]>([]);
  const [isSuperAdmin, setIsSuperAdmin] = useState(false);
  const [loading, setLoading] = useState(true);
  const [isAuthenticated, setIsAuthenticated] = useState(!!getAccessToken());

  const loadIdentity = useCallback(async () => {
    const token = getAccessToken();
    if (!token) {
      setIsAuthenticated(false);
      setLoading(false);
      return;
    }
    const claims = parseJwt(token);
    const name = claims['unique_name'] ?? claims['name'] ?? claims['preferred_username'] ?? '';
    const id = claims['sub'] ?? claims['nameidentifier'] ?? '';
    setUserName(name);
    setUserId(id);
    setTenantIdState(claims['tenantid'] ?? '');
    setIsSuperAdmin(claims['is_super_admin'] === 'true' || claims['is_super_admin'] === 'True');
    setIsAuthenticated(true);
    if (id) {
      try {
        const resp = await api.get('/api/permission-grants/me');
        const payload = unwrap<{ permissions?: string[] }>(resp.data);
        setPermissions(payload?.permissions ?? []);
      } catch {
        setPermissions([]);
      }
    }
    setLoading(false);
  }, []);

  useEffect(() => {
    loadIdentity();
  }, [loadIdentity]);

  const login = useCallback(async (username: string, password: string) => {
    await loginRequest(username, password);
    await loadIdentity();
  }, [loadIdentity]);

  const logout = useCallback(() => {
    clearTokens();
    setIsAuthenticated(false);
    setUserName('');
    setUserId('');
    setPermissions([]);
  }, []);

  const hasPermission = useCallback(
    (permission: string) => isSuperAdmin || permissions.includes(permission),
    [isSuperAdmin, permissions],
  );

  const switchTenant = useCallback((tenantId: string) => {
    setTenantId(tenantId);
    window.location.reload();
  }, []);

  const value = useMemo<AuthState>(
    () => ({
      isAuthenticated,
      userName,
      userId,
      tenantId,
      permissions,
      loading,
      login,
      logout,
      hasPermission,
      switchTenant,
      reloadPermissions: loadIdentity,
    }),
    [isAuthenticated, userName, userId, tenantId, permissions, loading, login, logout, hasPermission, switchTenant, loadIdentity],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthState {
  return useContext(AuthContext);
}

export { getTenantId };