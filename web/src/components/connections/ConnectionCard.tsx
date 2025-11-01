import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Database, Trash2, TestTube, Power, RefreshCw, Rocket } from 'lucide-react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { connectionApi } from '@/services/api';
import type { DatabaseConnection } from '@/types/connection';
import { useConnectionStore } from '@/stores/connectionStore';

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

  const initIndexMutation = useMutation({
    mutationFn: () => connectionApi.initIndex(connection.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    },
  });

  const updateIndexMutation = useMutation({
    mutationFn: () => connectionApi.updateIndex(connection.id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connections'] });
    },
  });

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

  return (
    <Card className={isSelected ? 'ring-2 ring-primary' : ''}>
      <CardHeader>
        <div className="flex items-start justify-between">
          <div className="flex items-center gap-2">
            {getDatabaseIcon(connection.databaseType)}
            <div>
              <CardTitle className="text-lg">{connection.name}</CardTitle>
              <CardDescription className="text-xs">
                {connection.databaseType.toUpperCase()}
              </CardDescription>
            </div>
          </div>
          <div className="flex gap-1">
            <Button
              size="icon"
              variant="ghost"
              onClick={handleTest}
              disabled={testMutation.isPending}
              title="测试连接"
            >
              <TestTube className="w-4 h-4" />
            </Button>
            <Button
              size="icon"
              variant="ghost"
              onClick={handleDelete}
              disabled={deleteMutation.isPending}
              title="删除连接"
            >
              <Trash2 className="w-4 h-4" />
            </Button>
          </div>
        </div>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="text-sm text-muted-foreground">
          {connection.description || '无描述'}
        </div>
        <div className="text-xs text-muted-foreground font-mono bg-muted p-2 rounded break-all">
          {connection.connectionString}
        </div>

        {/* 向量索引状态 */}
        <div className="flex items-center justify-between text-xs">
          <div className={`px-2 py-1 rounded ${connection.vectorIndexInitialized ? 'bg-green-500/10 text-green-600' : 'bg-amber-500/10 text-amber-600'}`}>
            {connection.vectorIndexInitialized ? `索引已初始化（${connection.vectorIndexCount ?? 0}）` : '索引未初始化'}
          </div>
          <div className="flex gap-2">
            {!connection.vectorIndexInitialized ? (
              <Button
                size="sm"
                variant="outline"
                onClick={() => initIndexMutation.mutate()}
                disabled={initIndexMutation.isPending}
                title="初始化表向量索引"
              >
                <Rocket className="w-4 h-4 mr-1" />
                {initIndexMutation.isPending ? '初始化中...' : '初始化索引'}
              </Button>
            ) : (
              <Button
                size="sm"
                variant="outline"
                onClick={() => updateIndexMutation.mutate()}
                disabled={updateIndexMutation.isPending}
                title="更新表向量索引"
              >
                <RefreshCw className="w-4 h-4 mr-1" />
                {updateIndexMutation.isPending ? '更新中...' : '更新索引'}
              </Button>
            )}
          </div>
        </div>

        {testMutation.data && (
          <div
            className={`text-xs p-2 rounded ${
              testMutation.data.success
                ? 'bg-green-500/10 text-green-600'
                : 'bg-red-500/10 text-red-600'
            }`}
          >
            {testMutation.data.message} ({testMutation.data.elapsedMs}ms)
          </div>
        )}

        <Button
          className="w-full"
          variant={isSelected ? 'default' : 'outline'}
          onClick={handleSelect}
        >
          <Power className="w-4 h-4 mr-2" />
          {isSelected ? '已选择' : '选择此连接'}
        </Button>
      </CardContent>
    </Card>
  );
}
