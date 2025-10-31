import { Moon, Sun } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { useThemeStore } from '@/stores/themeStore';
import { useConnectionStore } from '@/stores/connectionStore';
import { Avatar, AvatarImage, AvatarFallback } from '@/components/ui/avatar';

export function Header() {
  const { theme, toggleTheme } = useThemeStore();
  const { selectedConnectionId, connections } = useConnectionStore();

  const selectedConnection = connections.find((c) => c.id === selectedConnectionId);

  return (
    <header className="border-b bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/60">
      <div className="flex h-16 items-center justify-between px-4 lg:px-6">
        <div className="flex items-center gap-3">
          {selectedConnection ? (
            <div className="text-sm text-muted-foreground">
              当前连接: <span className="font-medium text-foreground">{selectedConnection.name}</span>
            </div>
          ) : (
            <div className="text-sm text-muted-foreground">未选择连接</div>
          )}
        </div>

        <div className="flex items-center gap-3">
          <div className="flex items-center gap-2">
            <Avatar className="h-8 w-8">
              <AvatarImage src="" alt="用户" />
              <AvatarFallback>U</AvatarFallback>
            </Avatar>
            <span className="text-sm font-medium">多云</span>
          </div>

          <Button
            variant="ghost"
            size="icon"
            onClick={toggleTheme}
            title={theme === 'dark' ? '切换到亮色模式' : '切换到暗色模式'}
          >
            {theme === 'dark' ? (
              <Sun className="w-5 h-5" />
            ) : (
              <Moon className="w-5 h-5" />
            )}
          </Button>
        </div>
      </div>
    </header>
  );
}
