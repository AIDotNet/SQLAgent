import { useState, useEffect } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Textarea } from '@/components/ui/textarea';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { connectionApi } from '@/services/api';
import type { CreateConnectionRequest } from '@/types/connection';

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
    <form onSubmit={handleSubmit} className="space-y-4">
      <div>
        <label className="block text-sm font-medium mb-2">连接名称</label>
        <Input
          value={formData.name}
          onChange={(e) => setFormData({ ...formData, name: e.target.value })}
          placeholder="例如: 生产数据库"
          required
        />
      </div>

      <div>
        <label className="block text-sm font-medium mb-2">数据库类型</label>
        <Select
          value={formData.databaseType}
          onValueChange={(value) =>
            setFormData({ ...formData, databaseType: value })
          }
        >
          <SelectTrigger>
            <SelectValue placeholder="选择数据库类型" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="sqlite">SQLite</SelectItem>
            <SelectItem value="mssql">SQL Server</SelectItem>
            <SelectItem value="postgresql">PostgreSQL</SelectItem>
            <SelectItem value="mysql">MySQL</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div>
        <label className="block text-sm font-medium mb-2">连接字符串</label>
        <Textarea
          value={formData.connectionString}
          onChange={(e) =>
            setFormData({ ...formData, connectionString: e.target.value })
          }
          placeholder="例如: Data Source=mydb.db"
          required
          rows={3}
        />
      </div>

      <div>
        <label className="block text-sm font-medium mb-2">描述（可选）</label>
        <Textarea
          value={formData.description}
          onChange={(e) =>
            setFormData({ ...formData, description: e.target.value })
          }
          placeholder="描述这个数据库连接的用途"
          rows={2}
        />
      </div>

      {createMutation.isError && (
        <div className="text-sm text-destructive">
          创建失败: {(createMutation.error as Error).message}
        </div>
      )}

      <div className="flex gap-2 justify-end">
        {onCancel && (
          <Button type="button" variant="outline" onClick={onCancel}>
            取消
          </Button>
        )}
        <Button type="submit" disabled={createMutation.isPending}>
          {createMutation.isPending ? '创建中...' : '创建连接'}
        </Button>
      </div>
    </form>
  );
}
