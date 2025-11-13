// 连接相关类型
export interface DatabaseConnection {
  id: string;
  name: string;
  databaseType: string;
  connectionString: string;
  description?: string;
  isEnabled: boolean;
  createdAt: string;
  updatedAt?: string;

  // 向量索引状态（由后端补充返回）
  vectorIndexInitialized?: boolean;
  vectorIndexCount?: number;
}

export interface CreateConnectionRequest {
  name: string;
  databaseType: string;
  connectionString: string;
  description?: string;
}

export interface UpdateConnectionRequest {
  name?: string;
  databaseType?: string;
  connectionString?: string;
  description?: string;
  isEnabled?: boolean;
}

export interface TestConnectionResponse {
  success: boolean;
  message: string;
  elapsedMs: number;
}

export const AgentGenerationStatus = {
  NotStarted: 0,
  InProgress: 1,
  Completed: 2,
  Failed: 3,
} as const;

export type AgentGenerationStatus = typeof AgentGenerationStatus[keyof typeof AgentGenerationStatus];

export interface AgentGenerationState {
  status: AgentGenerationStatus;
  message?: string;
  startTime?: string;
  endTime?: string;
  errorMessage?: string;
}
