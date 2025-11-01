import { useState } from 'react';
import { Send } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/textarea';

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled?: boolean;
}

export function ChatInput({ onSend, disabled }: ChatInputProps) {
  const [input, setInput] = useState('');

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    if (input.trim() && !disabled) {
      onSend(input.trim());
      setInput('');
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit(e);
    }
  };

  return (
    <form onSubmit={handleSubmit} className="relative">
      <Textarea
        value={input}
        onChange={(e) => setInput(e.target.value)}
        onKeyDown={handleKeyDown}
        placeholder="输入您的问题，例如：显示所有用户... (Enter 发送，Shift+Enter 换行)"
        disabled={disabled}
        rows={3}
        className="resize-none pr-12 min-h-[80px]"
      />
      <Button 
        type="submit" 
        disabled={disabled || !input.trim()} 
        size="icon"
        className="absolute right-2 bottom-2 h-8 w-8"
      >
        <Send className="w-4 h-4" />
      </Button>
    </form>
  );
}
