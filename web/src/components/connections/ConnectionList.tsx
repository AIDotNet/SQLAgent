import { useQuery } from '@tanstack/react-query';
import { useEffect } from 'react';
import { connectionApi } from '@/services/api';
import { ConnectionCard } from './ConnectionCard';
import { Loader2 } from 'lucide-react';
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
      <div className="flex items-center justify-center p-8">
        <Loader2 className="w-8 h-8 animate-spin text-muted-foreground" />
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4 text-center text-destructive">
        加载连接失败: {(error as Error).message}
      </div>
    );
  }

  if (!connections || connections.length === 0) {
    return (
      <div className="p-8 text-center text-muted-foreground">
        <p>还没有任何连接</p>
        <p className="text-sm mt-2">点击上方"添加连接"按钮创建第一个连接</p>
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      {connections.map((connection) => (
        <ConnectionCard key={connection.id} connection={connection} />
      ))}
    </div>
  );
}
