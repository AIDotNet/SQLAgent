import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type { AIProvider } from '../types/aiProvider';

interface AIProviderState {
  providers: AIProvider[];
  selectedProviderId: string | null;
  selectedModel: string | null;
  
  setProviders: (providers: AIProvider[]) => void;
  addProvider: (provider: AIProvider) => void;
  updateProvider: (provider: AIProvider) => void;
  deleteProvider: (id: string) => void;
  
  selectProvider: (providerId: string | null) => void;
  selectModel: (model: string | null) => void;
  
  getSelectedProvider: () => AIProvider | null;
}

export const useAIProviderStore = create<AIProviderState>()(
  persist(
    (set, get) => ({
      providers: [],
      selectedProviderId: null,
      selectedModel: null,

      setProviders: (providers) => set({ providers }),

      addProvider: (provider) =>
        set((state) => ({
          providers: [...state.providers, provider],
        })),

      updateProvider: (provider) =>
        set((state) => ({
          providers: state.providers.map((p) =>
            p.id === provider.id ? provider : p
          ),
        })),

      deleteProvider: (id) =>
        set((state) => ({
          providers: state.providers.filter((p) => p.id !== id),
          selectedProviderId:
            state.selectedProviderId === id ? null : state.selectedProviderId,
        })),

      selectProvider: (providerId) => {
        set({ selectedProviderId: providerId });
        // 如果选择了新的提供商，重置模型选择
        if (providerId) {
          const provider = get().providers.find((p) => p.id === providerId);
          if (provider) {
            // 自动选择默认模型
            set({ selectedModel: provider.defaultModel || provider.availableModels[0] || null });
          }
        } else {
          set({ selectedModel: null });
        }
      },

      selectModel: (model) => set({ selectedModel: model }),

      getSelectedProvider: () => {
        const state = get();
        return (
          state.providers.find((p) => p.id === state.selectedProviderId) || null
        );
      },
    }),
    {
      name: 'ai-provider-storage',
      partialize: (state) => ({
        selectedProviderId: state.selectedProviderId,
        selectedModel: state.selectedModel,
      }),
    }
  )
);
