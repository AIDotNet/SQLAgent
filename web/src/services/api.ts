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

  // 初始化（全量重建）向量索引
  async initIndex(id: string): Promise<{ initialized: boolean; updatedCount: number; totalCount: number }> {
    const response = await fetch(resolveApiUrl(`/connections/${id}/index/init`), { method: 'POST' });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || 'Failed to initialize index');
    }
    return response.json();
  },

  // 增量更新向量索引
  async updateIndex(id: string): Promise<{ initialized: boolean; updatedCount: number; totalCount: number }> {
    const response = await fetch(resolveApiUrl(`/connections/${id}/index/update`), { method: 'POST' });
    if (!response.ok) {
      const text = await response.text();
      throw new Error(text || 'Failed to update index');
    }
    return response.json();
  },
};
