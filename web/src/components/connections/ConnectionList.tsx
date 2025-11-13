import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { connectionApi } from '@/services/api';
import { ConnectionCard } from './ConnectionCard';
import { Loader2, Database } from 'lucide-react';
import { useConnectionStore } from '@/stores/connectionStore';

export function ConnectionList() {
  const { data: connections, isLoading, error } = useQuery({
    queryKey: ['connections'],
    queryFn: () => connectionApi.getAll(true),
  });

  const setConnections = useConnectionStore((state) => state.setConnections);

  // Sync API data to store
  useEffect(() => {
    if (connections) {
      setConnections(connections);
    }
  }, [connections, setConnections]);

  if (isLoading) {
    return (
      <div className="flex items-center justify-center p-12">
        <div className="flex flex-col items-center gap-3">
          <Loader2 className="w-10 h-10 animate-spin text-primary" />
          <p className="text-sm text-muted-foreground">加载连接中...</p>
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-8 text-center">
        <div className="inline-flex items-center justify-center w-16 h-16 rounded-full bg-destructive/10 mb-4">
          <Database className="w-8 h-8 text-destructive" />
        </div>
        <h3 className="text-lg font-semibold mb-2">加载失败</h3>
        <p className="text-sm text-muted-foreground">
          {(error as Error).message}
        </p>
      </div>
    );
  }

  if (!connections || connections.length === 0) {
    return (
      <div className="flex items-center justify-center p-12">
        <div className="text-center max-w-md">
          <div className="inline-flex items-center justify-center w-20 h-20 rounded-full bg-primary/10 mb-6">
            <Database className="w-10 h-10 text-primary" />
          </div>
          <h3 className="text-xl font-semibold mb-2">还没有任何连接</h3>
          <p className="text-sm text-muted-foreground mb-6">
            开始创建您的第一个数据库连接，轻松管理和查询数据
          </p>
          <div className="flex items-center justify-center gap-2 text-xs text-muted-foreground">
            <span className="inline-block w-2 h-2 rounded-full bg-primary"></span>
            <span>点击上方"新建连接"按钮开始</span>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div>
      <div className="mb-4 flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          共 {connections.length} 个连接
        </p>
      </div>
      <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-6">
        {connections.map((connection) => (
          <ConnectionCard key={connection.id} connection={connection} />
        ))}
      </div>
    </div>
  );
}
