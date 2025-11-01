import type { AIProvider, AIProviderInput, AIModel } from '../types/aiProvider';

const API_BASE = '/api';

export const aiProviderService = {
  /**
   * 获取所有 AI 提供商
   */
  async getAll(): Promise<AIProvider[]> {
    const response = await fetch(`${API_BASE}/providers`);
    if (!response.ok) {
      throw new Error('Failed to fetch AI providers');
    }
    return response.json();
  },

  /**
   * 获取单个 AI 提供商
   */
  async getById(id: string): Promise<AIProvider> {
    const response = await fetch(`${API_BASE}/providers/${id}`);
    if (!response.ok) {
      throw new Error(`Failed to fetch AI provider ${id}`);
    }
    return response.json();
  },

  /**
   * 创建 AI 提供商
   */
  async create(input: AIProviderInput): Promise<AIProvider> {
    const response = await fetch(`${API_BASE}/providers`, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(input),
    });
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Failed to create AI provider');
    }
    return response.json();
  },

  /**
   * 更新 AI 提供商
   */
  async update(id: string, input: AIProviderInput): Promise<AIProvider> {
    const response = await fetch(`${API_BASE}/providers/${id}`, {
      method: 'PUT',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(input),
    });
    if (!response.ok) {
      const error = await response.json();
      throw new Error(error.message || 'Failed to update AI provider');
    }
    return response.json();
  },

  /**
   * 删除 AI 提供商
   */
  async delete(id: string): Promise<void> {
    const response = await fetch(`${API_BASE}/providers/${id}`, {
      method: 'DELETE',
    });
    if (!response.ok) {
      throw new Error(`Failed to delete AI provider ${id}`);
    }
  },

  /**
   * 获取提供商的可用模型
   */
  async getModels(id: string): Promise<AIModel[]> {
    const response = await fetch(`${API_BASE}/providers/${id}/models`);
    if (!response.ok) {
      throw new Error(`Failed to fetch models for provider ${id}`);
    }
    return response.json();
  },
};
