import { useEffect, useState } from 'react';
import { Brain, Check } from 'lucide-react';
import { Button } from '../ui/button';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../ui/select';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '../ui/dialog';
import { Label } from '../ui/label';
import { Badge } from '../ui/badge';
import { useAIProviderStore } from '../../stores/aiProviderStore';
import { aiProviderService } from '../../services/aiProviderService';

export function AIProviderSelector() {
  const {
    providers,
    selectedProviderId,
    selectedModel,
    selectProvider,
    selectModel,
    setProviders,
  } = useAIProviderStore();

  const [open, setOpen] = useState(false);
  const [models, setModels] = useState<string[]>([]);

  const selectedProvider = providers.find((p) => p.id === selectedProviderId);

  useEffect(() => {
    // 加载提供商列表
    aiProviderService.getAll().then(setProviders).catch(console.error);
  }, [setProviders]);

  useEffect(() => {
    // 当选择提供商时，加载其模型列表
    if (selectedProviderId && selectedProvider) {
      setModels(selectedProvider.availableModels);
    } else {
      setModels([]);
    }
  }, [selectedProviderId, selectedProvider]);

  const handleProviderChange = (providerId: string) => {
    selectProvider(providerId);
    const provider = providers.find((p) => p.id === providerId);
    if (provider && provider.availableModels.length > 0) {
      selectModel(provider.defaultModel || provider.availableModels[0]);
    }
  };

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="outline" size="sm" className="gap-2">
          <Brain className="w-4 h-4" />
          {selectedProvider ? (
            <span className="flex items-center gap-2">
              {selectedProvider.name}
              {selectedModel && (
                <Badge variant="secondary" className="text-xs">
                  {selectedModel}
                </Badge>
              )}
            </span>
          ) : (
            '选择 AI 提供商'
          )}
        </Button>
      </DialogTrigger>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>选择 AI 提供商和模型</DialogTitle>
          <DialogDescription>
            选择用于生成 SQL 的 AI 提供商和模型
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-4 py-4">
          <div className="space-y-2">
            <Label htmlFor="provider">AI 提供商</Label>
            <Select value={selectedProviderId || ''} onValueChange={handleProviderChange}>
              <SelectTrigger id="provider">
                <SelectValue placeholder="选择提供商" />
              </SelectTrigger>
              <SelectContent>
                {providers
                  .filter((p) => p.isEnabled)
                  .map((provider) => (
                    <SelectItem key={provider.id} value={provider.id}>
                      <div className="flex items-center gap-2">
                        <span>{provider.name}</span>
                        <Badge variant="outline" className="text-xs">
                          {provider.type}
                        </Badge>
                      </div>
                    </SelectItem>
                  ))}
              </SelectContent>
            </Select>
          </div>

          {models.length > 0 && (
            <div className="space-y-2">
              <Label htmlFor="model">模型</Label>
              <Select value={selectedModel || ''} onValueChange={selectModel}>
                <SelectTrigger id="model">
                  <SelectValue placeholder="选择模型" />
                </SelectTrigger>
                <SelectContent>
                  {models.map((model) => (
                    <SelectItem key={model} value={model}>
                      <div className="flex items-center gap-2">
                        <span>{model}</span>
                        {model === selectedProvider?.defaultModel && (
                          <Badge variant="secondary" className="text-xs">
                            默认
                          </Badge>
                        )}
                      </div>
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          )}

          {selectedProvider && selectedModel && (
            <div className="flex items-center gap-2 p-3 bg-muted rounded-md">
              <Check className="w-4 h-4 text-green-600" />
              <span className="text-sm">
                已选择: {selectedProvider.name} / {selectedModel}
              </span>
            </div>
          )}
        </div>
        <div className="flex justify-end">
          <Button onClick={() => setOpen(false)}>确定</Button>
        </div>
      </DialogContent>
    </Dialog>
  );
}
