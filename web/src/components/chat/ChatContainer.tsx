import { useCallback } from 'react';
import { ChatInput } from './ChatInput';
import { MessageList } from './MessageList';
import { useChatStore } from '@/stores/chatStore';
import { useConnectionStore } from '@/stores/connectionStore';
import { useAIProviderStore } from '@/stores/aiProviderStore';
import { sseClient } from '@/services/sse';
import type { 
  ChatMessage, 
  SSEMessage,
  DeltaMessage,
  BlockMessage,
  ErrorMessage,
  ErrorBlock,
} from '@/types/message';

export function ChatContainer() {
  const { messages, addMessage, updateLastMessage, deleteMessage, isStreaming, setStreaming } = useChatStore();
  const { selectedConnectionId } = useConnectionStore();
  const { selectedProviderId, selectedModel } = useAIProviderStore();

  const handleSend = useCallback(async (content: string) => {
    if (!selectedConnectionId) {
      alert('请先选择一个数据库连接');
      return;
    }

    if (!selectedProviderId || !selectedModel) {
      alert('请先选择 AI 提供商和模型');
      return;
    }

    // 添加用户消息
    const userMessage: ChatMessage = {
      id: Date.now().toString(),
      role: 'user',
      content,
      blocks: [],
      timestamp: Date.now(),
      status: 'complete',
    };
    addMessage(userMessage);

    // 创建一个 AI 助手消息用于接收流式内容
    const assistantMessage: ChatMessage = {
      id: (Date.now() + 1).toString(),
      role: 'assistant',
      content: '',
      blocks: [],
      timestamp: Date.now(),
      status: 'streaming',
    };
    addMessage(assistantMessage);

    // 启动流式会话
    setStreaming(true);

    try {
      // 构建对话历史记录（包含当前消息）
      // 只发送文本内容，不发送blocks（blocks是展示用的）
      const conversationHistory = [
        ...messages.map(msg => ({
          role: msg.role,
          content: msg.content,
        })),
        {
          role: 'user' as const,
          content,
        }
      ];

      await sseClient.sendMessage(
        {
          connectionId: selectedConnectionId,
          messages: conversationHistory,
          execute: true,
          suggestChart: true,
          providerId: selectedProviderId,
          model: selectedModel,
        },
        (message: SSEMessage) => {
          handleSSEMessage(message);
        }
      );
      
      // 流式完成，更新状态
      updateLastMessage({ status: 'complete' });
    } catch (error) {
      // 出错时创建错误块
      const errorBlock: ErrorBlock = {
        id: `error-${Date.now()}`,
        type: 'error',
        code: 'CLIENT_ERROR',
        message: error instanceof Error ? error.message : '发送消息失败',
      };
      
      updateLastMessage({
        blocks: [errorBlock],
        status: 'error',
      });
    } finally {
      setStreaming(false);
    }
  }, [selectedConnectionId, selectedProviderId, selectedModel, messages, addMessage, updateLastMessage, setStreaming]);

  const handleSSEMessage = useCallback((message: SSEMessage) => {
    switch (message.type) {
      case 'delta': {
        // 增量文本消息 - 累加到最后一条消息的 content
        const deltaMsg = message as DeltaMessage;
        // 使用函数式更新来访问最新状态
        updateLastMessage((prev) => ({
          content: (prev.content || '') + deltaMsg.delta,
        }));
        break;
      }
      case 'block': {
        // 内容块消息 - 添加到最后一条消息的 blocks 数组
        const blockMsg = message as BlockMessage;
        updateLastMessage((prev) => ({
          blocks: [...(prev.blocks || []), blockMsg.block],
        }));
        break;
      }
      case 'error': {
        // 错误消息 - 转换为错误块
        const errorMsg = message as ErrorMessage;
        const errorBlock: ErrorBlock = {
          id: `error-${Date.now()}`,
          type: 'error',
          code: errorMsg.code,
          message: errorMsg.message,
          details: errorMsg.details,
        };
        
        updateLastMessage((prev) => ({
          blocks: [...(prev.blocks || []), errorBlock],
          status: 'error' as const,
        }));
        break;
      }
      case 'done': {
        // 完成标记
        updateLastMessage({ status: 'complete' });
        break;
      }
    }
  }, [updateLastMessage]);

  const handleDeleteMessage = useCallback((messageId: string) => {
    if (isStreaming) {
      return; // 流式传输时不允许删除
    }
    deleteMessage(messageId);
  }, [deleteMessage, isStreaming]);

  return (
    <div className="flex flex-col h-full w-full">
      <div className="flex-1 min-h-0 flex justify-center overflow-hidden">
        <div className="w-full max-w-6xl flex flex-col">
          {/* 消息列表区域 - 占据主要空间 */}
          <div className="flex-1 min-h-0">
            <MessageList
              messages={messages}
              isStreaming={isStreaming}
              onDeleteMessage={handleDeleteMessage}
            />
          </div>
        </div>
      </div>
      
      {/* 输入区域 - 固定在底部 */}
      <div className="flex-shrink-0 border-t bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
        <div className="max-w-6xl mx-auto p-4">
          <ChatInput onSend={handleSend} disabled={isStreaming || !selectedConnectionId} />
          {!selectedConnectionId && (
            <p className="text-xs text-muted-foreground mt-2 text-center">
              请先在连接管理页面选择一个数据库连接
            </p>
          )}
        </div>
      </div>
    </div>
  );
}
