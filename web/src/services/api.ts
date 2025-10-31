import type {
  DatabaseConnection,
  CreateConnectionRequest,
  UpdateConnectionRequest,
  TestConnectionResponse,
} from '../types/connection';
import { resolveApiUrl } from './config';

export const connectionApi = {
  // 获取所有连接
  async getAll(includeDisabled = false): Promise<DatabaseConnection[]> {
    const response = await fetch(
      resolveApiUrl(`/connections?includeDisabled=${includeDisabled}`)
    );
    if (!response.ok) throw new Error('Failed to fetch connections');
    return response.json();
  },

  // 获取单个连接
  async getById(id: string): Promise<DatabaseConnection> {
  const response = await fetch(resolveApiUrl(`/connections/${id}`));
    if (!response.ok) throw new Error('Failed to fetch connection');
    return response.json();
  },

  // 创建连接
  async create(data: CreateConnectionRequest): Promise<DatabaseConnection> {
  const response = await fetch(resolveApiUrl('/connections'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Failed to create connection');
    }
    return response.json();
  },

  // 更新连接
  async update(
    id: string,
    data: UpdateConnectionRequest
  ): Promise<DatabaseConnection> {
  const response = await fetch(resolveApiUrl(`/connections/${id}`), {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(data),
    });
    if (!response.ok) throw new Error('Failed to update connection');
    return response.json();
  },

  // 删除连接
  async delete(id: string): Promise<void> {
  const response = await fetch(resolveApiUrl(`/connections/${id}`), {
      method: 'DELETE',
    });
    if (!response.ok) throw new Error('Failed to delete connection');
  },

  // 测试连接
  async test(id: string): Promise<TestConnectionResponse> {
  const response = await fetch(resolveApiUrl(`/connections/${id}/test`), {
      method: 'POST',
    });
    if (!response.ok) throw new Error('Failed to test connection');
    return response.json();
  },
};
