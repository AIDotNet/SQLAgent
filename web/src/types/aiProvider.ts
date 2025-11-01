/**
 * AI 提供商类型
 */
export type AIProviderType = "OpenAI" | "AzureOpenAI" | "CustomOpenAI";

export const AIProviderTypes = {
  OpenAI: "OpenAI" as const,
  AzureOpenAI: "AzureOpenAI" as const,
  CustomOpenAI: "CustomOpenAI" as const,
};

/**
 * AI 提供商
 */
export interface AIProvider {
  id: string;
  name: string;
  type: AIProviderType;
  endpoint?: string;
  apiKey: string;
  availableModels: string[];
  defaultModel?: string;
  isEnabled: boolean;
  extraConfig?: string;
  createdAt: string;
  updatedAt: string;
}

/**
 * AI 提供商输入（创建/更新）
 */
export interface AIProviderInput {
  name: string;
  type: string;
  endpoint?: string;
  apiKey: string;
  availableModels: string | string[]; // 逗号分隔
  defaultModel?: string;
  isEnabled: boolean;
  extraConfig?: string;
}

/**
 * AI 模型信息
 */
export interface AIModel {
  name: string;
  displayName: string;
  isDefault: boolean;
}

/**
 * 常用的模型列表
 */
export const COMMON_MODELS = {
  OpenAI: ["gpt-4.1", "gpt-4.1-mini", "gpt-5", "gpt-5-mini"],
  AzureOpenAI: ["gpt-4.1", "gpt-4.1-mini", "gpt-5", "gpt-5-mini"],
  CustomOpenAI: ["gpt-4.1", "gpt-4.1-mini", "gpt-5", "gpt-5-mini"],
};
