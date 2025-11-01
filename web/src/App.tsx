import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { useEffect } from 'react';
import { useThemeStore } from './stores/themeStore';
import { useAIProviderStore } from './stores/aiProviderStore';
import { useConnectionStore } from './stores/connectionStore';
import { aiProviderService } from './services/aiProviderService';
import { connectionService } from './services/connectionService';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      retry: 1,
    },
  },
});

function ThemeProvider({ children }: { children: React.ReactNode }) {
  const theme = useThemeStore((state) => state.theme);

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark');
  }, [theme]);

  return <>{children}</>;
}

function DataLoader({ children }: { children: React.ReactNode }) {
  const { setProviders } = useAIProviderStore();
  const { setConnections } = useConnectionStore();

  useEffect(() => {
    // 加载 AI 提供商
    aiProviderService.getAll()
      .then(setProviders)
      .catch(console.error);

    // 加载数据库连接
    connectionService.getAll()
      .then(setConnections)
      .catch(console.error);
  }, [setProviders, setConnections]);

  return <>{children}</>;
}

import ConnectionsPage from './pages/ConnectionsPage';
import ChatPage from './pages/ChatPage';
import ProvidersPage from './pages/ProvidersPage';
import SettingsPage from './pages/SettingsPage';
function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <DataLoader>
          <BrowserRouter>
            <Routes>
              {/* 主页 - 聊天界面 */}
              <Route path="/" element={<ChatPage />} />
              
              {/* 连接管理页面 */}
              <Route path="/connections" element={<ConnectionsPage />} />
              
              {/* AI 提供商管理页面 */}
              <Route path="/providers" element={<ProvidersPage />} />

              {/* 系统设置页面 */}
              <Route path="/settings" element={<SettingsPage />} />
              
              {/* 默认重定向 */}
              <Route path="*" element={<Navigate to="/" replace />} />
            </Routes>
          </BrowserRouter>
        </DataLoader>
      </ThemeProvider>
    </QueryClientProvider>
  );
}

export default App;
