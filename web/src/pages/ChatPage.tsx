import { useNavigate } from 'react-router-dom';
import { Database, Brain, ArrowRight, CheckCircle2, Sparkles } from 'lucide-react';
import { AppLayout } from '../components/layout/AppLayout';
import { ChatContainer } from '../components/chat/ChatContainer';
import { useConnectionStore } from '../stores/connectionStore';
import { useAIProviderStore } from '../stores/aiProviderStore';
import { Button } from '../components/ui/button';
import { Card, CardContent } from '../components/ui/card';
import { cn } from '@/lib/utils';

interface SetupStep {
  id: string;
  title: string;
  description: string;
  icon: React.ReactNode;
  completed: boolean;
  action: () => void;
  actionLabel: string;
}

function EmptyState({ steps }: { steps: SetupStep[] }) {
  const completedSteps = steps.filter(s => s.completed).length;
  const totalSteps = steps.length;
  const progress = (completedSteps / totalSteps) * 100;

  return (
    <div className="h-full flex items-center justify-center p-6 overflow-y-auto">
      <div className="max-w-3xl w-full">
        {/* 主标题区域 */}
        <div className="text-center mb-8">
          <div className="inline-flex items-center justify-center w-16 h-16 rounded-xl bg-primary mb-4 shadow-sm">
            <Sparkles className="w-8 h-8 text-primary-foreground" />
          </div>
          <h1 className="text-3xl font-bold mb-2 text-foreground">
            欢迎使用 SQLBox AI 助手
          </h1>
          <p className="text-muted-foreground text-lg">
            使用自然语言与您的数据库对话，让 AI 帮您生成和执行 SQL 查询
          </p>
        </div>

        {/* 进度指示器 */}
        {completedSteps < totalSteps && (
          <div className="mb-8">
            <div className="flex items-center justify-between mb-2">
              <span className="text-sm font-medium">配置进度</span>
              <span className="text-sm text-muted-foreground">
                {completedSteps} / {totalSteps} 已完成
              </span>
            </div>
            <div className="h-2 bg-muted rounded-full overflow-hidden">
              <div 
                className="h-full bg-primary transition-all duration-500 ease-out"
                style={{ width: `${progress}%` }}
              />
            </div>
          </div>
        )}

        {/* 配置步骤卡片 */}
        <div className="space-y-4">
          {steps.map((step, index) => (
            <Card 
              key={step.id}
              className={cn(
                "transition-all duration-200",
                step.completed 
                  ? 'bg-accent border-accent' 
                  : 'hover:shadow-sm hover:border-ring'
              )}
            >
              <CardContent className="p-6">
                <div className="flex items-start gap-4">
                  {/* 步骤图标 */}
                  <div className={cn(
                    "flex-shrink-0 w-12 h-12 rounded-lg flex items-center justify-center",
                    step.completed
                      ? 'bg-primary text-primary-foreground'
                      : 'bg-muted text-muted-foreground'
                  )}>
                    {step.completed ? (
                      <CheckCircle2 className="w-6 h-6" />
                    ) : (
                      step.icon
                    )}
                  </div>

                  {/* 步骤内容 */}
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-1">
                      <span className="text-xs font-medium text-muted-foreground">
                        步骤 {index + 1}
                      </span>
                      {step.completed && (
                        <span className="text-xs font-medium text-primary">
                          ✓ 已完成
                        </span>
                      )}
                    </div>
                    <h3 className="text-lg font-semibold mb-1">{step.title}</h3>
                    <p className="text-sm text-muted-foreground mb-4">
                      {step.description}
                    </p>
                    {!step.completed && (
                      <Button 
                        onClick={step.action}
                        className="group"
                        size="sm"
                      >
                        {step.actionLabel}
                        <ArrowRight className="ml-2 h-4 w-4 transition-transform group-hover:translate-x-1" />
                      </Button>
                    )}
                  </div>
                </div>
              </CardContent>
            </Card>
          ))}
        </div>

        {/* 完成提示 */}
        {completedSteps === totalSteps && (
          <Card className="mt-6 bg-accent border-accent">
            <CardContent className="p-6 text-center">
              <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-primary text-primary-foreground mb-3">
                <CheckCircle2 className="w-6 h-6" />
              </div>
              <h3 className="text-lg font-semibold mb-2">配置完成！</h3>
              <p className="text-sm text-muted-foreground">
                所有必需的配置已完成。您现在可以开始与数据库对话了。
              </p>
            </CardContent>
          </Card>
        )}
      </div>
    </div>
  );
}

export default function ChatPage() {
  const navigate = useNavigate();
  const { connections, getSelectedConnection } = useConnectionStore();
  const { providers, getSelectedProvider, selectedModel } = useAIProviderStore();
  const currentConnection = getSelectedConnection();
  const currentProvider = getSelectedProvider();

  // 检查配置状态
  const hasConnections = connections.length > 0;
  const hasSelectedConnection = !!currentConnection;
  const hasProviders = providers.length > 0;
  const hasSelectedProvider = !!currentProvider && !!selectedModel;

  // 如果有任何未完成的配置，显示引导界面
  if (!hasConnections || !hasSelectedConnection || !hasProviders || !hasSelectedProvider) {
    const steps: SetupStep[] = [
      {
        id: 'connection',
        title: '配置数据库连接',
        description: hasConnections 
          ? `已配置 ${connections.length} 个数据库连接${!hasSelectedConnection ? '，请在顶部菜单中选择一个连接' : ''}` 
          : '添加您的第一个数据库连接，支持 MySQL、PostgreSQL、SQL Server 等',
        icon: <Database className="w-6 h-6" />,
        completed: hasConnections && hasSelectedConnection,
        action: () => navigate('/connections'),
        actionLabel: hasConnections ? '选择连接' : '添加连接',
      },
      {
        id: 'provider',
        title: '配置 AI 提供商',
        description: hasProviders
          ? `已配置 ${providers.length} 个 AI 提供商${!hasSelectedProvider ? '，请在顶部菜单中选择提供商和模型' : ''}`
          : '配置 OpenAI 或 Azure OpenAI 以使用 AI 功能',
        icon: <Brain className="w-6 h-6" />,
        completed: hasProviders && hasSelectedProvider,
        action: () => navigate('/providers'),
        actionLabel: hasProviders ? '选择模型' : '添加提供商',
      },
    ];

    return (
      <AppLayout>
        <EmptyState steps={steps} />
      </AppLayout>
    );
  }

  // 配置完成，显示聊天界面
  return (
    <AppLayout>
      <ChatContainer />
    </AppLayout>
  );
}
