// ============================================
// SSE 消息类型定义（后端 → 前端）
// ============================================

/** SSE 事件类型 */
export type SSEEventType = 'delta' | 'block' | 'done' | 'error';

/** 基础 SSE 消息 */
export interface SSEMessage {
  type: SSEEventType;
}

/** 增量文本消息（流式文本输出） */
export interface DeltaMessage extends SSEMessage {
  type: 'delta';
  delta: string;
}

/** 内容块消息（SQL、数据、图表等特殊内容） */
export interface BlockMessage extends SSEMessage {
  type: 'block';
  block: ContentBlock;
}

/** 完成消息 */
export interface DoneMessage extends SSEMessage {
  type: 'done';
  elapsedMs: number;
}

/** 错误消息 */
export interface ErrorMessage extends SSEMessage {
  type: 'error';
  code: string;
  message: string;
  details?: string;
}

// ============================================
// 内容块类型定义
// ============================================

/** 内容块类型 */
export type ContentBlockType = 'sql' | 'data' | 'chart' | 'error';

/** 内容块基础接口 */
export interface ContentBlock {
  id: string;
  type: ContentBlockType;
}

/** SQL 代码块 */
export interface SqlBlock extends ContentBlock {
  type: 'sql';
  sql: string;
  tables: string[];
  dialect?: string;
}

/** 数据表格块 */
export interface DataBlock extends ContentBlock {
  type: 'data';
  columns: string[];
  rows: any[][];
  totalRows: number;
}

/** 图表块 */
export interface ChartBlock extends ContentBlock {
  type: 'chart';
  chartType: string;
  echartsOption?: string; // ECharts option 配置 JSON 字符串
  config: ChartConfig;
  data: any;
}

export interface ChartConfig {
  xAxis?: string;
  yAxis?: string[];
  title?: string;
  showLegend: boolean;
}

/** 错误块 */
export interface ErrorBlock extends ContentBlock {
  type: 'error';
  code: string;
  message: string;
  details?: string;
}

// ============================================
// 聊天消息定义
// ============================================

/** 消息状态 */
export type MessageStatus = 'streaming' | 'complete' | 'error';

/** 聊天消息 */
export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;  // 主要文本内容（流式累积）
  blocks: ContentBlock[];  // 附加的内容块（SQL、数据等）
  timestamp: number;
  status: MessageStatus;
}

// 聊天消息（用于对话历史）
export interface ChatRequestMessage {
  role: 'user' | 'assistant' | 'system';
  content: string;
}

// 聊天请求
export interface CompletionRequest {
  connectionId: string;
  messages: ChatRequestMessage[];  // 对话历史记录列表
  execute?: boolean;
  maxRows?: number;
  suggestChart?: boolean;
  dialect?: string;
  providerId: string;
  model: string;
}
