import type {
  CompletionRequest,
  SSEMessage,
} from '../types/message';
import { resolveApiUrl } from './config';

export type SSEMessageHandler = (message: SSEMessage) => void;

export class SSEClient {
  private abortController: AbortController | null = null;

  async sendMessage(
    request: CompletionRequest,
    onMessage: SSEMessageHandler
  ): Promise<void> {
    this.abortController = new AbortController();

    try {
  const response = await fetch(resolveApiUrl('/chat/completion'), {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify(request),
        signal: this.abortController.signal,
      });

      if (!response.ok) {
        throw new Error(`HTTP error! status: ${response.status}`);
      }

      const reader = response.body?.getReader();
      if (!reader) {
        throw new Error('No response body');
      }

      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();

        if (done) {
          break;
        }

        buffer += decoder.decode(value, { stream: true });
        const lines = buffer.split('\n');

        // 保留最后一行（可能不完整）
        buffer = lines.pop() || '';

        for (const line of lines) {
          if (line.startsWith('data: ')) {
            const data = line.slice(6);
            try {
              const message = JSON.parse(data) as SSEMessage;
              onMessage(message);
            } catch (e) {
              console.error('Failed to parse SSE message:', e, data);
            }
          }
        }
      }
    } catch (error: any) {
      if (error.name === 'AbortError') {
        console.log('SSE connection aborted');
      } else {
        throw error;
      }
    }
  }

  cancel() {
    if (this.abortController) {
      this.abortController.abort();
      this.abortController = null;
    }
  }
}

export const sseClient = new SSEClient();
