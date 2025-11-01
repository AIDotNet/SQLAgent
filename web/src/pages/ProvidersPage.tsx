import { useEffect, useState } from 'react';
import { Plus, Trash2, Edit, Power, PowerOff } from 'lucide-react';
import { AppLayout } from '../components/layout/AppLayout';
import { Button } from '../components/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '../components/ui/card';
import { Badge } from '../components/ui/badge';
import { useAIProviderStore } from '../stores/aiProviderStore';
import { aiProviderService } from '../services/aiProviderService';
import type { AIProvider } from '../types/aiProvider';
import { toast } from "sonner"

import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '../components/ui/dialog';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '../components/ui/alert-dialog';
import { ProviderForm } from '../components/providers/ProviderForm';

export default function ProvidersPage() {
  const { providers, setProviders, addProvider, updateProvider, deleteProvider } = useAIProviderStore();
  const [loading, setLoading] = useState(true);
  const [isDialogOpen, setIsDialogOpen] = useState(false);
  const [editingProvider, setEditingProvider] = useState<AIProvider | null>(null);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [deleteDialogOpen, setDeleteDialogOpen] = useState(false);
  const [providerToDelete, setProviderToDelete] = useState<string | null>(null);

  useEffect(() => {
    loadProviders();
  }, []);

  const loadProviders = async () => {
    try {
      setLoading(true);
      const data = await aiProviderService.getAll();
      setProviders(data);
    } catch (error) {
      console.error('Failed to load providers:', error);
    } finally {
      setLoading(false);
    }
  };

  const handleCreate = () => {
    setEditingProvider(null);
    setIsDialogOpen(true);
  };

  const handleEdit = (provider: AIProvider) => {
    setEditingProvider(provider);
    setIsDialogOpen(true);
  };

  const handleSave = async (input: any) => {
    try {
      if (editingProvider) {
        const updated = await aiProviderService.update(editingProvider.id, input);
        updateProvider(updated);
      } else {
        const created = await aiProviderService.create(input);
        addProvider(created);
      }
      setIsDialogOpen(false);
      setEditingProvider(null);
    } catch (error: any) {
      toast.error(error.message || 'Failed to save provider');
    }
  };

  const handleDelete = (id: string) => {
    setProviderToDelete(id);
    setDeleteDialogOpen(true);
  };

  const confirmDelete = async () => {
    if (!providerToDelete) return;

    try {
      setDeletingId(providerToDelete);
      await aiProviderService.delete(providerToDelete);
      deleteProvider(providerToDelete);
      toast.success('提供商已删除');
    } catch (error: any) {
      toast.error(error.message || '删除提供商失败');
    } finally {
      setDeletingId(null);
      setDeleteDialogOpen(false);
      setProviderToDelete(null);
    }
  };

  const getProviderTypeLabel = (type: string) => {
    switch (type) {
      case 'OpenAI':
        return 'OpenAI';
      case 'AzureOpenAI':
        return 'Azure OpenAI';
      case 'CustomOpenAI':
        return '自定义 OpenAI';
      default:
        return type;
    }
  };

  if (loading) {
    return (
      <AppLayout>
        <div className="flex items-center justify-center h-64">
          <div className="text-muted-foreground">加载中...</div>
        </div>
      </AppLayout>
    );
  }

  return (
    <AppLayout>
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold tracking-tight">AI 提供商</h1>
            <p className="text-muted-foreground">
              配置和管理您的 AI 提供商（OpenAI、Azure OpenAI 等）
            </p>
          </div>
          <Button onClick={handleCreate}>
            <Plus className="mr-2 h-4 w-4" />
            添加提供商
          </Button>
        </div>

        {providers.length === 0 ? (
          <Card>
            <CardContent className="py-12 text-center">
              <p className="text-muted-foreground mb-4">
                还没有配置任何 AI 提供商
              </p>
              <Button onClick={handleCreate}>
                <Plus className="mr-2 h-4 w-4" />
                添加第一个提供商
              </Button>
            </CardContent>
          </Card>
        ) : (
          <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
            {providers.map((provider) => (
              <Card key={provider.id}>
                <CardHeader>
                  <div className="flex items-start justify-between">
                    <div className="space-y-1">
                      <CardTitle className="flex items-center gap-2">
                        {provider.name}
                        {provider.isEnabled ? (
                          <Badge variant="default" className="ml-2">
                            <Power className="mr-1 h-3 w-3" />
                            启用
                          </Badge>
                        ) : (
                          <Badge variant="secondary" className="ml-2">
                            <PowerOff className="mr-1 h-3 w-3" />
                            禁用
                          </Badge>
                        )}
                      </CardTitle>
                      <CardDescription>
                        {getProviderTypeLabel(provider.type)}
                      </CardDescription>
                    </div>
                  </div>
                </CardHeader>
                <CardContent>
                  <div className="space-y-2 text-sm">
                    {provider.endpoint && (
                      <div>
                        <span className="text-muted-foreground">端点:</span>{' '}
                        <span className="font-mono text-xs">
                          {provider.endpoint}
                        </span>
                      </div>
                    )}
                    <div>
                      <span className="text-muted-foreground">API Key:</span>{' '}
                      <span className="font-mono text-xs">{provider.apiKey}</span>
                    </div>
                    <div>
                      <span className="text-muted-foreground">可用模型:</span>
                      <div className="flex flex-wrap gap-1 mt-1">
                        {provider.availableModels.map((model) => (
                          <Badge key={model} variant="outline" className="text-xs">
                            {model}
                          </Badge>
                        ))}
                      </div>
                    </div>
                    {provider.defaultModel && (
                      <div>
                        <span className="text-muted-foreground">默认模型:</span>{' '}
                        <Badge variant="secondary" className="text-xs">
                          {provider.defaultModel}
                        </Badge>
                      </div>
                    )}
                  </div>
                  <div className="flex gap-2 mt-4">
                    <Button
                      variant="outline"
                      size="sm"
                      onClick={() => handleEdit(provider)}
                    >
                      <Edit className="mr-2 h-4 w-4" />
                      编辑
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      onClick={() => handleDelete(provider.id)}
                      disabled={deletingId === provider.id}
                    >
                      <Trash2 className="mr-2 h-4 w-4" />
                      删除
                    </Button>
                  </div>
                </CardContent>
              </Card>
            ))}
          </div>
        )}
      </div>

      <Dialog open={isDialogOpen} onOpenChange={setIsDialogOpen}>
        <DialogContent className="max-w-2xl max-h-[90vh] overflow-y-auto">
          <DialogHeader>
            <DialogTitle>
              {editingProvider ? '编辑 AI 提供商' : '添加 AI 提供商'}
            </DialogTitle>
            <DialogDescription>
              配置您的 AI 提供商信息，包括 API 端点、密钥和可用模型
            </DialogDescription>
          </DialogHeader>
          {isDialogOpen && <ProviderForm
            provider={editingProvider}
            onSave={handleSave}
            onCancel={() => setIsDialogOpen(false)}
          />}
        </DialogContent>
      </Dialog>

      <AlertDialog open={deleteDialogOpen} onOpenChange={setDeleteDialogOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>确认删除</AlertDialogTitle>
            <AlertDialogDescription>
              您确定要删除这个 AI 提供商吗？此操作无法撤销。
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>取消</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              删除
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </AppLayout>
  );
}
