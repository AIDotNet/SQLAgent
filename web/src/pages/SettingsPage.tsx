import { useEffect, useMemo, useState } from 'react';
import { AppLayout } from '../components/layout/AppLayout';
import { Button } from '../components/ui/button';
import { Input } from '../components/ui/input';
import { Label } from '../components/ui/label';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '../components/ui/select';
import { Switch } from '../components/ui/switch';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '../components/ui/card';
import { aiProviderService } from '../services/aiProviderService';
import type { AIProvider } from '../types/aiProvider';
import { resolveApiUrl } from '../services/config';
import { toast } from "sonner"

type DistanceMetric = 'Cosine' | 'Euclidean' | 'DotProduct';

type SystemSettings = {
  // Embedding
  embeddingProviderId?: string | null;
  embeddingModel: string;

  // Vector store
  vectorDbPath: string;
  vectorCollection: string;
  distanceMetric: DistanceMetric;
  autoCreateCollection: boolean;
  vectorCacheExpireMinutes?: number | null;

  // Default chat
  defaultChatProviderId?: string | null;
  defaultChatModel?: string | null;
};

export default function SettingsPage() {
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [providers, setProviders] = useState<AIProvider[]>([]);
  const [settings, setSettings] = useState<SystemSettings>({
    embeddingProviderId: null,
    embeddingModel: 'text-embedding-ada-002',
    vectorDbPath: 'Data Source=vectors.db',
    vectorCollection: 'table_vectors',
    distanceMetric: 'Cosine',
    autoCreateCollection: true,
    vectorCacheExpireMinutes: null,
    defaultChatProviderId: null,
    defaultChatModel: '',
  });

  const providerOptions = useMemo(() => {
    return providers.map((p) => ({ value: p.id, label: p.name }));
  }, [providers]);

  useEffect(() => {
    const load = async () => {
      try {
        setLoading(true);
        const [prov, sys] = await Promise.all([
          aiProviderService.getAll(),
          fetch(resolveApiUrl('/settings')).then(async (r) => {
            if (!r.ok) throw new Error('加载系统设置失败');
            return r.json();
          }),
        ]);
        setProviders(prov);
        // 兼容后端 null/undefined
        setSettings({
          embeddingProviderId: sys.embeddingProviderId ?? null,
          embeddingModel: sys.embeddingModel ?? 'text-embedding-ada-002',
          vectorDbPath: sys.vectorDbPath ?? 'Data Source=vectors.db',
          vectorCollection: sys.vectorCollection ?? 'table_vectors',
          distanceMetric: (sys.distanceMetric ?? 'Cosine') as DistanceMetric,
          autoCreateCollection: Boolean(sys.autoCreateCollection ?? true),
          vectorCacheExpireMinutes:
            typeof sys.vectorCacheExpireMinutes === 'number' ? sys.vectorCacheExpireMinutes : null,
          defaultChatProviderId: sys.defaultChatProviderId ?? null,
          defaultChatModel: sys.defaultChatModel ?? '',
        });
      } catch (err) {
        console.error(err);
        toast.error('加载系统设置失败');
      } finally {
        setLoading(false);
      }
    };
    load();
  }, []);

  const handleSave = async () => {
    try {
      setSaving(true);
      const payload: SystemSettings = {
        ...settings,
        // 正常化空值
        embeddingProviderId: settings.embeddingProviderId || null,
        defaultChatProviderId: settings.defaultChatProviderId || null,
        defaultChatModel: settings.defaultChatModel || null,
        vectorCacheExpireMinutes:
          settings.vectorCacheExpireMinutes === null || settings.vectorCacheExpireMinutes === undefined
            ? null
            : Number(settings.vectorCacheExpireMinutes),
      };
      const res = await fetch(resolveApiUrl('/settings'), {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(payload),
      });
      if (!res.ok) {
        const t = await res.text();
        throw new Error(t || '保存失败');
      }
      toast.success('已保存系统设置');
    } catch (err: any) {
      toast.error(err?.message || '保存失败');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-64 text-muted-foreground">加载中...</div>
      </AppLayout>
    );
  }

  return (
    <AppLayout>
      <div className="space-y-6">
        <div>
          <h1 className="text-3xl font-bold tracking-tight">系统设置</h1>
          <p className="text-muted-foreground mt-2">配置索引嵌入模型、向量库参数以及默认对话提供商/模型</p>
        </div>

        <Card>
          <CardHeader>
            <CardTitle>索引（向量化）</CardTitle>
            <CardDescription>用于构建表向量索引的嵌入模型与提供商</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label>嵌入提供商</Label>
                <Select
                  value={settings.embeddingProviderId ?? '__none__'}
                  onValueChange={(v) => setSettings((s) => ({ ...s, embeddingProviderId: v === '__none__' ? null : v }))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="选择用于索引的提供商（可选）" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">（使用聊天提供商）</SelectItem>
                    {providerOptions.map((opt) => (
                      <SelectItem key={opt.value} value={opt.value}>
                        {opt.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="space-y-2">
                <Label htmlFor="embeddingModel">嵌入模型</Label>
                <Input
                  id="embeddingModel"
                  value={settings.embeddingModel}
                  onChange={(e) => setSettings((s) => ({ ...s, embeddingModel: e.target.value }))}
                  placeholder="如 text-embedding-ada-002"
                />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>向量存储（Sqlite-Vec）</CardTitle>
            <CardDescription>配置向量数据库文件、集合名称与距离度量等参数</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="vectorDbPath">数据库文件路径</Label>
                <Input
                  id="vectorDbPath"
                  value={settings.vectorDbPath}
                  onChange={(e) => setSettings((s) => ({ ...s, vectorDbPath: e.target.value }))}
                  placeholder="Data Source=vectors.db"
                />
              </div>
              <div className="space-y-2">
                <Label htmlFor="vectorCollection">集合名称</Label>
                <Input
                  id="vectorCollection"
                  value={settings.vectorCollection}
                  onChange={(e) => setSettings((s) => ({ ...s, vectorCollection: e.target.value }))}
                  placeholder="table_vectors"
                />
              </div>
              <div className="space-y-2">
                <Label>距离度量</Label>
                <Select
                  value={settings.distanceMetric}
                  onValueChange={(v) => setSettings((s) => ({ ...s, distanceMetric: v as DistanceMetric }))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="选择距离度量" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Cosine">Cosine（推荐）</SelectItem>
                    <SelectItem value="Euclidean">Euclidean</SelectItem>
                    <SelectItem value="DotProduct">DotProduct</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label>自动创建集合</Label>
                <div className="flex items-center gap-3">
                  <Switch
                    checked={settings.autoCreateCollection}
                    onCheckedChange={(val) => setSettings((s) => ({ ...s, autoCreateCollection: Boolean(val) }))}
                  />
                  <span className="text-sm text-muted-foreground">启动自动创建</span>
                </div>
              </div>
              <div className="space-y-2">
                <Label htmlFor="cacheMinutes">向量缓存过期（分钟，可空）</Label>
                <Input
                  id="cacheMinutes"
                  type="number"
                  value={settings.vectorCacheExpireMinutes ?? ''}
                  onChange={(e) => {
                    const v = e.target.value;
                    setSettings((s) => ({
                      ...s,
                      vectorCacheExpireMinutes: v === '' ? null : Number(v),
                    }));
                  }}
                  placeholder="留空表示不过期"
                />
              </div>
            </div>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>默认聊天</CardTitle>
            <CardDescription>默认聊天使用的提供商/模型（可被具体会话覆盖）</CardDescription>
          </CardHeader>
          <CardContent className="space-y-4">
            <div className="grid gap-4 md:grid-cols-2">
              <div className="space-y-2">
                <Label>默认聊天提供商</Label>
                <Select
                  value={settings.defaultChatProviderId ?? '__none__'}
                  onValueChange={(v) => setSettings((s) => ({ ...s, defaultChatProviderId: v === '__none__' ? null : v }))}
                >
                  <SelectTrigger>
                    <SelectValue placeholder="选择默认聊天提供商（可选）" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="__none__">（未设置）</SelectItem>
                    {providerOptions.map((opt) => (
                      <SelectItem key={opt.value} value={opt.value}>
                        {opt.label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-2">
                <Label htmlFor="defaultChatModel">默认聊天模型</Label>
                <Input
                  id="defaultChatModel"
                  value={settings.defaultChatModel ?? ''}
                  onChange={(e) => setSettings((s) => ({ ...s, defaultChatModel: e.target.value }))}
                  placeholder="例如 gpt-4o-mini"
                />
              </div>
            </div>
          </CardContent>
        </Card>

        <div className="flex justify-end">
          <Button onClick={handleSave} disabled={saving}>{saving ? '保存中...' : '保存设置'}</Button>
        </div>
      </div>
    </AppLayout>
  );
}