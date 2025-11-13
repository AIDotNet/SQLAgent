import { useEffect } from 'react';
import { Database, Power } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import { Badge } from '@/components/ui/badge';
import { useConnectionStore } from '@/stores/connectionStore';
import { useQuery } from '@tanstack/react-query';
import { connectionApi } from '@/services/api';

export function ConnectionSelector() {
  const {
    connections,
    selectedConnectionId,
    selectConnection,
    setConnections,
  } = useConnectionStore();

  const { data: connectionsData } = useQuery({
    queryKey: ['connections'],
    queryFn: () => connectionApi.getAll(true),
  });

  useEffect(() => {
    if (connectionsData) {
      setConnections(connectionsData);
    }
  }, [connectionsData, setConnections]);

  const selectedConnection = connections.find(
    (c) => c.id === selectedConnectionId
  );

  const getDatabaseTypeColor = (type: string) => {
    const colors: Record<string, string> = {
      sqlite: 'bg-blue-500',
      mysql: 'bg-orange-500',
      postgresql: 'bg-indigo-500',
      mssql: 'bg-red-500',
    };
    return colors[type.toLowerCase()] || 'bg-gray-500';
  };

  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button variant="outline" size="sm" className="gap-2 min-w-[200px]">
          <Database className="w-4 h-4" />
          {selectedConnection ? (
            <span className="flex items-center gap-2 flex-1 min-w-0">
              <span className="truncate">{selectedConnection.name}</span>
              <Badge variant="secondary" className="text-xs flex-shrink-0">
                {selectedConnection.databaseType.toUpperCase()}
              </Badge>
            </span>
          ) : (
            <span className="text-muted-foreground">选择数据库连接</span>
          )}
        </Button>
      </PopoverTrigger>
      <PopoverContent className="w-80" align="end">
        <div className="space-y-4">
          <div>
            <h4 className="font-medium text-sm mb-2">数据库连接</h4>
            <p className="text-xs text-muted-foreground mb-3">
              选择一个活动的数据库连接
            </p>
          </div>

          {connections.length === 0 ? (
            <div className="text-center py-6 text-sm text-muted-foreground">
              <Database className="w-8 h-8 mx-auto mb-2 opacity-50" />
              <p>还没有任何连接</p>
              <p className="text-xs mt-1">请先创建数据库连接</p>
            </div>
          ) : (
            <Select
              value={selectedConnectionId || ''}
              onValueChange={selectConnection}
            >
              <SelectTrigger>
                <SelectValue placeholder="选择连接" />
              </SelectTrigger>
              <SelectContent>
                {connections.map((connection) => (
                  <SelectItem key={connection.id} value={connection.id}>
                    <div className="flex items-center gap-2 py-1">
                      <span
                        className={`inline-block w-2 h-2 rounded-full ${getDatabaseTypeColor(
                          connection.databaseType
                        )}`}
                      ></span>
                      <span className="flex-1">{connection.name}</span>
                      <Badge variant="outline" className="text-xs ml-2">
                        {connection.databaseType.toUpperCase()}
                      </Badge>
                    </div>
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          )}

          {selectedConnection && (
            <div className="flex items-center gap-2 p-3 bg-primary/10 rounded-lg border border-primary/20">
              <Power className="w-4 h-4 text-primary flex-shrink-0" />
              <div className="flex-1 min-w-0">
                <p className="text-sm font-medium truncate">
                  {selectedConnection.name}
                </p>
                <p className="text-xs text-muted-foreground truncate">
                  {selectedConnection.databaseType.toUpperCase()}
                </p>
              </div>
            </div>
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
}
