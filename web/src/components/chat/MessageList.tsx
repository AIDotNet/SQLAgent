import { useEffect, useRef } from 'react';
import { MessageItem } from './MessageItem';
import type { ChatMessage } from '@/types/message';
import { Loader2 } from 'lucide-react';

interface MessageListProps {
  messages: ChatMessage[];
  isStreaming?: boolean;
  onDeleteMessage?: (messageId: string) => void;
}

export function MessageList({ messages, isStreaming, onDeleteMessage }: MessageListProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

  if (messages.length === 0) {
    return (
      <div className="h-full flex items-center justify-center text-muted-foreground p-4 overflow-y-auto">
        <div className="text-center max-w-md">
          <div className="mb-6">
            <div className="inline-flex items-center justify-center w-16 h-16 rounded-lg bg-muted mb-4">
              <span className="text-3xl">💬</span>
            </div>
          </div>
          <h3 className="text-xl font-semibold text-foreground mb-2">开始对话</h3>
          <p className="text-muted-foreground mb-6">
            使用自然语言描述您想要查询的数据，AI 会为您生成并执行 SQL 查询
          </p>
          <div className="text-left space-y-2 bg-muted/50 rounded-lg p-4 border">
            <p className="text-sm font-medium text-foreground mb-2">示例问题：</p>
            <div className="space-y-1 text-sm text-muted-foreground">
              <p>• 查询最近30天的订单总额</p>
              <p>• 显示销量最高的10个产品</p>
              <p>• 统计每个月的新用户数量</p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="h-full overflow-y-auto">
      <div className="p-4 space-y-6">
        {messages.map((message) => (
          <MessageItem 
            key={message.id} 
            message={message} 
            onDelete={onDeleteMessage}
          />
        ))}
        {isStreaming && (
          <div className="flex gap-3 items-start">
            <div className="w-8 h-8 rounded-full flex items-center justify-center bg-primary">
              <Loader2 className="w-4 h-4 animate-spin text-primary-foreground" />
            </div>
            <div className="flex-1 pt-1">
              <div className="text-sm text-muted-foreground flex items-center gap-2">
                <span>AI 正在思考</span>
                <span className="flex gap-1">
                  <span className="animate-bounce" style={{ animationDelay: '0ms' }}>.</span>
                  <span className="animate-bounce" style={{ animationDelay: '150ms' }}>.</span>
                  <span className="animate-bounce" style={{ animationDelay: '300ms' }}>.</span>
                </span>
              </div>
            </div>
          </div>
        )}
        <div ref={bottomRef} />
      </div>
    </div>
  );
}
