import { useState, useEffect } from 'react';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import EChartsReact from 'echarts-for-react';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import type { ChatMessage, ContentBlock, SqlBlock, DataBlock, ChartBlock, ErrorBlock } from '@/types/message';
import { User, Bot, Code, Table, BarChart3, AlertCircle, Loader2, Trash2 } from 'lucide-react';
import { cn } from '@/lib/utils';

interface MessageItemProps {
  message: ChatMessage;
  onDelete?: (messageId: string) => void;
}

export function MessageItem({ message, onDelete }: MessageItemProps) {
  const isUser = message.role === 'user';
  const isStreaming = message.status === 'streaming';
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);

  const handleDeleteClick = () => {
    setShowDeleteDialog(true);
  };

  const handleConfirmDelete = () => {
    onDelete?.(message.id);
    setShowDeleteDialog(false);
  };

  return (
    <div className={cn(
      "group/message flex gap-4 animate-in fade-in slide-in-from-bottom-4 duration-500",
      isUser && "flex-row-reverse"
    )}>
      {/* 头像 */}
      <div
        className={cn(
          "w-10 h-10 rounded-lg flex items-center justify-center flex-shrink-0",
          isUser 
            ? 'bg-primary text-primary-foreground' 
            : 'bg-muted text-muted-foreground'
        )}
      >
        {isUser ? <User className="w-5 h-5" /> : <Bot className="w-5 h-5" />}
      </div>

      <div className={cn("flex-1 space-y-3 max-w-4xl", isUser && "items-end")}>
        {/* 主要文本内容 */}
        {message.content && (
          <div className={cn("group relative", isUser && "flex justify-end")}>
            <div
              className={cn(
                "inline-block px-4 py-3 rounded-lg",
                isUser
                  ? 'bg-primary text-primary-foreground'
                  : 'bg-muted'
              )}
            >
              <p className="whitespace-pre-wrap leading-relaxed">
                {message.content}
                {isStreaming && (
                  <span className="inline-block w-2 h-5 ml-1 bg-current/70 animate-pulse rounded" />
                )}
              </p>
            </div>
            {/* 删除按钮 - 悬停时显示 */}
            {onDelete && !isStreaming && (
              <Button
                variant="ghost"
                size="icon"
                className={cn(
                  "absolute -top-2 opacity-0 group-hover:opacity-100 transition-opacity h-7 w-7",
                  isUser ? "-left-9" : "-right-9"
                )}
                onClick={handleDeleteClick}
                title="删除此消息"
              >
                <Trash2 className="h-4 w-4 text-muted-foreground hover:text-destructive" />
              </Button>
            )}
          </div>
        )}

        {/* 内容块渲染 */}
        {message?.blocks?.map((block) => (
          <BlockRenderer key={block.id} block={block} />
        ))}

        {/* 时间戳和状态 */}
        <div className={cn(
          "flex items-center gap-2 text-xs text-muted-foreground px-2",
          isUser && "justify-end"
        )}>
          {isStreaming && (
            <>
              <Loader2 className="w-3 h-3 animate-spin" />
              <span>处理中</span>
            </>
          )}
          <span>{new Date(message.timestamp).toLocaleTimeString('zh-CN', { hour: '2-digit', minute: '2-digit' })}</span>
        </div>
      </div>

      {/* 删除确认对话框 */}
      <AlertDialog open={showDeleteDialog} onOpenChange={setShowDeleteDialog}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>确认删除消息</AlertDialogTitle>
            <AlertDialogDescription>
              此操作无法撤销。确定要删除这条{isUser ? '用户' : 'AI'}消息吗？
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>取消</AlertDialogCancel>
            <AlertDialogAction onClick={handleConfirmDelete} className="bg-destructive text-destructive-foreground hover:bg-destructive/90">
              删除
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}

// ============================================
// 内容块渲染器
// ============================================

interface BlockRendererProps {
  block: ContentBlock;
}

function BlockRenderer({ block }: BlockRendererProps) {
  switch (block.type) {
    case 'sql':
      return <SqlBlockRenderer block={block as SqlBlock} />;
    case 'data':
      return <DataBlockRenderer block={block as DataBlock} />;
    case 'chart':
      return <ChartBlockRenderer block={block as ChartBlock} />;
    case 'error':
      return <ErrorBlockRenderer block={block as ErrorBlock} />;
    default:
      return null;
  }
}

// SQL 代码块
function SqlBlockRenderer({ block }: { block: SqlBlock }) {
  return (
    <Card className="overflow-hidden hover:border-ring transition-colors">
      <div className="flex items-center gap-3 px-4 py-3 bg-muted/50 border-b">
        <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center">
          <Code className="w-4 h-4 text-primary-foreground" />
        </div>
        <div className="flex-1">
          <div className="text-sm font-semibold">生成的 SQL</div>
          {block.dialect && (
            <div className="text-xs text-muted-foreground">{block.dialect}</div>
          )}
        </div>
      </div>
      <div className="p-4">
        <pre className="text-sm font-mono overflow-x-auto p-4 bg-muted rounded-lg border">
          <code className="text-foreground">{block.sql}</code>
        </pre>
        {block.tables.length > 0 && (
          <div className="mt-3 flex items-center gap-2 text-xs">
            <span className="text-muted-foreground">涉及表:</span>
            <div className="flex gap-1 flex-wrap">
              {block.tables.map((table) => (
                <Badge key={table} variant="secondary">
                  {table}
                </Badge>
              ))}
            </div>
          </div>
        )}
      </div>
    </Card>
  );
}

// 数据表格块
function DataBlockRenderer({ block }: { block: DataBlock }) {
  const displayRows = block.rows.slice(0, 10);
  
  return (
    <Card className="overflow-hidden hover:border-ring transition-colors">
      <div className="flex items-center gap-3 px-4 py-3 bg-muted/50 border-b">
        <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center">
          <Table className="w-4 h-4 text-primary-foreground" />
        </div>
        <div className="flex-1">
          <div className="text-sm font-semibold">查询结果</div>
          <div className="text-xs text-muted-foreground">共 {block.totalRows} 行数据</div>
        </div>
      </div>
      <div className="p-4">
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead>
              <tr className="bg-muted border-b">
                {block.columns.map((col) => (
                  <th key={col} className="px-4 py-3 text-left font-semibold whitespace-nowrap">
                    {col}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {displayRows.map((row, i) => (
                <tr 
                  key={i} 
                  className="border-b last:border-0 hover:bg-muted/50 transition-colors"
                >
                  {row.map((cell, j) => (
                    <td key={j} className="px-4 py-3 whitespace-nowrap">
                      {cell?.toString() || <span className="text-muted-foreground italic">null</span>}
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {block.totalRows > 10 && (
          <div className="mt-3 text-xs text-center py-2 bg-muted rounded-lg text-muted-foreground border">
            📊 仅显示前 10 行，共 {block.totalRows} 行数据
          </div>
        )}
      </div>
    </Card>
  );
}

// 图表块
function ChartBlockRenderer({ block }: { block: ChartBlock }) {
  const [option, setOption] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (block.echartsOption) {
      try {
        let parsedOption;
        if (typeof block.echartsOption === 'string') {
          // 使用 Function 构造函数安全地解析包含 JavaScript 代码的配置
          // 将字符串包装在返回语句中
          const func = new Function(`return ${block.echartsOption}`);
          parsedOption = func();
        } else {
          parsedOption = block.echartsOption;
        }
        setOption(parsedOption);
        setError(null);
      } catch (err) {
        setError('图表配置解析失败');
        console.error('Failed to parse ECharts option:', err);
      }
    }
  }, [block.echartsOption]);

  return (
    <Card className="overflow-hidden hover:border-ring transition-colors">
      <div className="flex items-center gap-3 px-4 py-3 bg-muted/50 border-b">
        <div className="w-8 h-8 rounded-lg bg-primary flex items-center justify-center">
          <BarChart3 className="w-4 h-4 text-primary-foreground" />
        </div>
        <div className="flex-1">
          <div className="text-sm font-semibold">数据可视化</div>
          <div className="text-xs text-muted-foreground capitalize">{block.chartType}</div>
        </div>
      </div>
      <div className="p-4">
        {error ? (
          <div className="p-6 border border-destructive rounded-lg bg-destructive/5 flex items-center justify-center min-h-[200px]">
            <div className="text-center text-destructive">
              <AlertCircle className="w-12 h-12 mx-auto mb-3" />
              <p className="text-sm">{error}</p>
            </div>
          </div>
        ) : option ? (
          <EChartsReact
            option={option}
            style={{ height: '400px', width: '100%' }}
            opts={{ renderer: 'canvas' }}
            notMerge={true}
            lazyUpdate={true}
          />
        ) : (
          <div className="p-6 border rounded-lg bg-muted/30 flex items-center justify-center min-h-[200px]">
            <div className="text-center text-muted-foreground">
              <BarChart3 className="w-12 h-12 mx-auto mb-3 opacity-50" />
              <p className="text-sm">等待图表数据...</p>
            </div>
          </div>
        )}
      </div>
    </Card>
  );
}

// 错误块
function ErrorBlockRenderer({ block }: { block: ErrorBlock }) {
  return (
    <Card className="overflow-hidden border-destructive">
      <div className="flex items-center gap-3 px-4 py-3 bg-destructive/10 border-b border-destructive/20">
        <div className="w-8 h-8 rounded-lg bg-destructive flex items-center justify-center">
          <AlertCircle className="w-4 h-4 text-white" />
        </div>
        <div className="flex-1">
          <div className="text-sm font-semibold text-destructive">执行错误</div>
          {block.code && (
            <Badge variant="destructive" className="text-xs font-mono mt-1">
              {block.code}
            </Badge>
          )}
        </div>
      </div>
      <div className="p-4 bg-destructive/5">
        <p className="text-sm leading-relaxed">
          {block.message}
        </p>
        {block.details && (
          <details className="mt-3">
            <summary className="cursor-pointer text-xs font-medium text-muted-foreground hover:text-foreground transition-colors select-none">
              📋 查看详细信息
            </summary>
            <pre className="mt-2 p-3 bg-muted rounded-lg border overflow-x-auto text-xs">
              <code>{block.details}</code>
            </pre>
          </details>
        )}
      </div>
    </Card>
  );
}
