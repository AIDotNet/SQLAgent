import { create } from 'zustand';
import type { ChatMessage } from '../types/message';

interface ChatStore {
  messages: ChatMessage[];
  isStreaming: boolean;
  addMessage: (message: ChatMessage) => void;
  updateLastMessage: (updates: Partial<ChatMessage> | ((prev: ChatMessage) => Partial<ChatMessage>)) => void;
  deleteMessage: (messageId: string) => void;
  clearMessages: () => void;
  setStreaming: (streaming: boolean) => void;
}

export const useChatStore = create<ChatStore>()((set) => ({
  messages: [],
  isStreaming: false,

  addMessage: (message) =>
    set((state) => ({
      messages: [...state.messages, message],
    })),

  updateLastMessage: (updates) =>
    set((state) => {
      const messages = [...state.messages];
      const lastIndex = messages.length - 1;
      if (lastIndex >= 0) {
        const currentMessage = messages[lastIndex];
        const updateValues = typeof updates === 'function' ? updates(currentMessage) : updates;
        messages[lastIndex] = { ...currentMessage, ...updateValues };
      }
      return { messages };
    }),

  deleteMessage: (messageId) =>
    set((state) => ({
      messages: state.messages.filter((msg) => msg.id !== messageId),
    })),

  clearMessages: () => set({ messages: [] }),

  setStreaming: (streaming) => set({ isStreaming: streaming }),
}));
