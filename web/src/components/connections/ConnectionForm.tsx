import { useState, useEffect } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { connectionApi } from '@/services/api';
import type { CreateConnectionRequest } from '@/types/connection';
import { Database, Info } from 'lucide-react';

// 默认连接字符串模板
const DEFAULT_CONNECTION_STRINGS: Record<string, string> = {
  sqlite: 'Data Source=sqlbox.db',
  mssql: 'Server=localhost;Database=sqlbox;User Id=sa;Password=your_password;TrustServerCertificate=True;',
  postgresql: 'Host=localhost;Port=5432;Database=sqlbox;Username=postgres;Password=your_password;',
  mysql: 'Server=localhost;Port=3306;Database=sqlbox;User=root;Password=your_password;',
};

interface ConnectionFormProps {
  onSuccess?: () => void;
  onCancel?: () => void;
}

export function ConnectionForm({ onSuccess, onCancel }: ConnectionFormProps) {
  const queryClient = useQueryClient();
  const [formData, setFormData] = useState<CreateConnectionRequest>({
    name: '',
    databaseType: 'sqlite',
    connectionString: DEFAULT_CONNECTION_STRINGS.sqlite,
    description: '',
  });

  // 当数据库类型改变时，自动更新连接字符串（仅当连接字符串为空或为默认值时）
  useEffect(() => {
    const currentDefault = DEFAULT_CONNECTION_STRINGS[formData.databaseType];
    // 如果当前连接字符串为空，或者是某个默认值，则更新为新的默认值
    const isDefaultOrEmpty =
      !formData.connectionString ||
      Object.values(DEFAULT_CONNECTION_STRINGS).includes(
        formData.connectionString
      );

    if (isDefaultOrEmpty && currentDefault) {
      setFormData((prev) => ({
        ...prev,
        connectionString: currentDefault,
      }));
    }
  }, [formData.databaseType]);

  const createMutation = useMutation({
    mutationFn: connectionApi.create,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['connections'] });
      onSuccess?.();
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    createMutation.mutate(formData);
  };

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <div className="space-y-2">
        <Label htmlFor="name" className="text-sm font-medium flex items-center gap-2">
          <Database className="w-4 h-4" />
          连接名称
        </Label>
        <Input
          id="name"
          value={formData.name}
          onChange={(e) => setFormData({ ...formData, name: e.target.value })}
          placeholder="例如: 生产数据库"
          required
          className="h-10"
        />
        <p className="text-xs text-muted-foreground">为这个连接起一个易于识别的名称</p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="databaseType" className="text-sm font-medium">
          数据库类型
        </Label>
        <Select
          value={formData.databaseType}
          onValueChange={(value) =>
            setFormData({ ...formData, databaseType: value })
          }
        >
          <SelectTrigger id="databaseType" className="h-10">
            <SelectValue placeholder="选择数据库类型" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="sqlite">
              <div className="flex items-center gap-2">
                <span className="inline-block w-2 h-2 rounded-full bg-blue-500"></span>
                SQLite
              </div>
            </SelectItem>
            <SelectItem value="mssql">
              <div className="flex items-center gap-2">
                <span className="inline-block w-2 h-2 rounded-full bg-red-500"></span>
                SQL Server
              </div>
            </SelectItem>
            <SelectItem value="postgresql">
              <div className="flex items-center gap-2">
                <span className="inline-block w-2 h-2 rounded-full bg-indigo-500"></span>
                PostgreSQL
              </div>
            </SelectItem>
            <SelectItem value="mysql">
              <div className="flex items-center gap-2">
                <span className="inline-block w-2 h-2 rounded-full bg-orange-500"></span>
                MySQL
              </div>
            </SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-2">
        <Label htmlFor="connectionString" className="text-sm font-medium">
          连接字符串
        </Label>
        <Textarea
          id="connectionString"
          value={formData.connectionString}
          onChange={(e) =>
            setFormData({ ...formData, connectionString: e.target.value })
          }
          placeholder="例如: Data Source=mydb.db"
          required
          rows={4}
          className="font-mono text-sm resize-none"
        />
        <div className="flex items-start gap-2 p-3 bg-blue-500/10 border border-blue-200 dark:border-blue-800 rounded-lg">
          <Info className="w-4 h-4 text-blue-600 dark:text-blue-400 flex-shrink-0 mt-0.5" />
          <p className="text-xs text-blue-700 dark:text-blue-300">
            根据所选的数据库类型，连接字符串会自动填充示例格式。请根据实际情况修改。
          </p>
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="description" className="text-sm font-medium">
          描述（可选）
        </Label>
        <Textarea
          id="description"
          value={formData.description}
          onChange={(e) =>
            setFormData({ ...formData, description: e.target.value })
          }
          placeholder="描述这个数据库连接的用途"
          rows={3}
          className="resize-none"
        />
      </div>

      {createMutation.isError && (
        <div className="flex items-start gap-2 p-3 bg-destructive/10 border border-destructive/20 rounded-lg">
          <Info className="w-4 h-4 text-destructive flex-shrink-0 mt-0.5" />
          <div className="text-sm text-destructive">
            创建失败: {(createMutation.error as Error).message}
          </div>
        </div>
      )}

      <div className="flex gap-3 justify-end pt-4 border-t">
        {onCancel && (
          <Button type="button" variant="outline" onClick={onCancel} className="min-w-24">
            取消
          </Button>
        )}
        <Button type="submit" disabled={createMutation.isPending} className="min-w-24">
          {createMutation.isPending ? '创建中...' : '创建连接'}
        </Button>
      </div>
    </form>
  );
}
