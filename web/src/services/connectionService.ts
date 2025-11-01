import type { DatabaseConnection } from '../types/connection';

const API_BASE = '/api';

export const connectionService = {
  /**
   * 获取所有数据库连接
   */
  async getAll(): Promise<DatabaseConnection[]> {
    const response = await fetch(`${API_BASE}/connections`);
    if (!response.ok) {
      throw new Error('Failed to fetch connections');
    }
    return response.json();
  },

  /**
   * 获取单个数据库连接
   */
  async getById(id: string): Promise<DatabaseConnection> {
    const response = await fetch(`${API_BASE}/connections/${id}`);
    if (!response.ok) {
      throw new Error(`Failed to fetch connection ${id}`);
    }
    return response.json();
  },

  /**
   * 创建数据库连接
   */
  async create(input: Omit<DatabaseConnection, 'id' | 'createdAt' | 'updatedAt'>): Promise<DatabaseConnection> {
    const response = await fetch(`${API_BASE}/connections`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(input),
    });
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Failed to create connection');
    }
    return response.json();
  },

  /**
   * 更新数据库连接
   */
  async update(id: string, input: Partial<DatabaseConnection>): Promise<DatabaseConnection> {
    const response = await fetch(`${API_BASE}/connections/${id}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(input),
    });
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Failed to update connection');
    }
    return response.json();
  },

  /**
   * 删除数据库连接
   */
  async delete(id: string): Promise<void> {
    const response = await fetch(`${API_BASE}/connections/${id}`, {
      method: 'DELETE',
    });
    if (!response.ok) {
      throw new Error(`Failed to delete connection ${id}`);
    }
  },

  /**
   * 测试数据库连接
   */
  async test(id: string): Promise<{ success: boolean; message: string }> {
    const response = await fetch(`${API_BASE}/connections/${id}/test`, {
      method: 'POST',
    });
    if (!response.ok) {
      throw new Error(`Failed to test connection ${id}`);
    }
    return response.json();
  },
};
