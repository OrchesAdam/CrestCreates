import axios from 'axios';
import { getTenantId, saveTokens } from './client';

export interface LoginResult {
  access_token: string;
  refresh_token?: string;
  expires_in: number;
  token_type: string;
}

export async function login(username: string, password: string): Promise<LoginResult> {
  const form = new URLSearchParams({
    grant_type: 'password',
    username,
    password,
    client_id: 'crestcreates-client',
    scope: 'openid profile offline_access',
  });
  const resp = await axios.post('/connect/token', form, {
    headers: { 'X-Tenant-Id': getTenantId() },
  });
  saveTokens(resp.data.access_token, resp.data.refresh_token);
  return resp.data;
}