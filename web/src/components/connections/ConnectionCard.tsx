import { useMutation, useQueryClient, useQuery } from '@tanstack/react-query';
import { Database, Trash2, TestTube, Power, Rocket, CheckCircle2, XCircle, Loader2 } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { connectionApi } from '@/services/api';
import type { DatabaseConnection } from '@/types/connection';
import { AgentGenerationStatus } from '@/types/connection';
import { useConnectionStore } from '@/stores/connectionStore';
import { useEffect } from 'react';

interface ConnectionCardProps {
  connection: DatabaseConnection;
}

export function ConnectionCard({ connection }: ConnectionCardProps) {
  const queryClient = useQueryClient();
  const { selectedConnectionId, selectConnection } = useConnectionStore();
  const isSelected = selectedConnectionId === connection.id;

  const deleteMutation = useMutation({
    mutationFn: connectionApi.delete,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connections'] });
      if (isSelected) {
        selectConnection(null);
      }
    },
  });

  const testMutation = useMutation({
    mutationFn: connectionApi.test,
  });

  const generateAgentMutation = useMutation({
    mutationFn: () => connectionApi.generateAgent(connection.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    },
  });

  // 查询 Agent 生成状态
  const { data: agentStatus } = useQuery({
    queryKey: ['agent-status', connection.id],
    queryFn: () => connectionApi.getAgentGenerationStatus(connection.id),
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      // 如果状态是进行中，每 2 秒轮询一次
      if (status === AgentGenerationStatus.InProgress) {
        return 2000;
      }
      // 否则停止轮询
      return false;
    },
    refetchOnWindowFocus: false,
  });

  // 当生成完成或失败时，刷新连接列表
  useEffect(() => {
    if (agentStatus?.status === AgentGenerationStatus.Completed || 
        agentStatus?.status === AgentGenerationStatus.Failed) {
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    }
  }, [agentStatus?.status, queryClient]);

  const isGenerating = agentStatus?.status === AgentGenerationStatus.InProgress;

  const handleTest = () => {
    testMutation.mutate(connection.id);
  };

  const handleDelete = () => {
    if (confirm(`确定要删除连接 "${connection.name}" 吗？`)) {
      deleteMutation.mutate(connection.id);
    }
  };

  const handleSelect = () => {
    selectConnection(isSelected ? null : connection.id);
  };

  const getDatabaseIcon = (_type: string) => {
    return <Database className="w-5 h-5" />;
  };

  const getDatabaseTypeColor = (type: string) => {
    const colors: Record<string, string> = {
      sqlite: 'bg-blue-500/10 text-blue-700 dark:text-blue-400',
      mysql: 'bg-orange-500/10 text-orange-700 dark:text-orange-400',
      postgresql: 'bg-indigo-500/10 text-indigo-700 dark:text-indigo-400',
      mssql: 'bg-red-500/10 text-red-700 dark:text-red-400',
    };
    return colors[type.toLowerCase()] || 'bg-gray-500/10 text-gray-700 dark:text-gray-400';
  };

  return (
    <Card className={`group transition-all duration-200 hover:shadow-lg ${
      isSelected ? 'ring-2 ring-primary shadow-md' : ''
    }`}>
      <CardHeader className="pb-3">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-start gap-3 flex-1 min-w-0">
            <div className={`p-2.5 rounded-lg ${getDatabaseTypeColor(connection.databaseType)}`}>
              {getDatabaseIcon(connection.databaseType)}
            </div>
            <div className="flex-1 min-w-0">
              <CardTitle className="text-lg truncate" title={connection.name}>
                {connection.name}
              </CardTitle>
              <div className="flex items-center gap-2 mt-1.5">
                <Badge variant="secondary" className={`text-xs ${getDatabaseTypeColor(connection.databaseType)}`}>
                  {connection.databaseType.toUpperCase()}
                </Badge>
                {isSelected && (
                  <Badge variant="default" className="text-xs">
                    <Power className="w-3 h-3 mr-1" />
                    活动
                  </Badge>
                )}
              </div>
            </div>
          </div>
          <div className="flex gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
            <Button
              size="icon"
              variant="ghost"
              className="h-8 w-8"
              onClick={handleTest}
              disabled={testMutation.isPending}
              title="测试连接"
            >
              {testMutation.isPending ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <TestTube className="w-4 h-4" />
              )}
            </Button>
            <Button
              size="icon"
              variant="ghost"
              className="h-8 w-8 hover:text-destructive"
              onClick={handleDelete}
              disabled={deleteMutation.isPending}
              title="删除连接"
            >
              {deleteMutation.isPending ? (
                <Loader2 className="w-4 h-4 animate-spin" />
              ) : (
                <Trash2 className="w-4 h-4" />
              )}
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        {connection.description && (
          <p className="text-sm text-muted-foreground line-clamp-2">
            {connection.description}
          </p>
        )}
        
        <div className="text-xs text-muted-foreground font-mono bg-muted/50 p-3 rounded-md break-all border">
          {connection.connectionString}
        </div>

        {/* 测试连接结果 */}
        {testMutation.data && (
          <div
            className={`flex items-start gap-2 text-xs p-3 rounded-lg border ${
              testMutation.data.success
                ? 'bg-green-500/10 text-green-700 dark:text-green-400 border-green-200 dark:border-green-800'
                : 'bg-red-500/10 text-red-700 dark:text-red-400 border-red-200 dark:border-red-800'
            }`}
          >
            {testMutation.data.success ? (
              <CheckCircle2 className="w-4 h-4 flex-shrink-0 mt-0.5" />
            ) : (
              <XCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
            )}
            <div className="flex-1">
              <div>{testMutation.data.message}</div>
              <div className="text-xs opacity-75 mt-0.5">耗时: {testMutation.data.elapsedMs}ms</div>
            </div>
          </div>
        )}

        <div className="space-y-2 pt-1">
          <Button
            variant="outline"
            onClick={() => generateAgentMutation.mutate()}
            disabled={isGenerating || generateAgentMutation.isPending}
            className="w-full"
            size="sm"
          >
            {isGenerating || generateAgentMutation.isPending ? (
              <>
                <Loader2 className="w-4 h-4 mr-2 animate-spin" />
                生成中
              </>
            ) : (
              <>
                <Rocket className="w-4 h-4 mr-2" />
                生成 Agent
              </>
            )}
          </Button>

          <Button
            variant={isSelected ? 'default' : 'outline'}
            onClick={handleSelect}
            className="w-full"
            size="sm"
          >
            <Power className="w-4 h-4 mr-2" />
            {isSelected ? '已选择' : '选择'}
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
