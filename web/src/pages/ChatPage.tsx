import { useNavigate } from 'react-router-dom';
import { AlertCircle, Database } from 'lucide-react';
import { AppLayout } from '../components/layout/AppLayout';
import { ChatContainer } from '../components/chat/ChatContainer';
import { useConnectionStore } from '../stores/connectionStore';
import { Button } from '../components/ui/button';
import { Alert, AlertDescription, AlertTitle } from '../components/ui/alert';

export default function ChatPage() {
  const navigate = useNavigate();
  const { connections, getSelectedConnection } = useConnectionStore();
  const currentConnection = getSelectedConnection();

  // 如果没有任何连接，显示提示
  if (connections.length === 0) {
    return (
      <AppLayout>
        <div className="py-2">
          <Alert>
            <AlertCircle className="h-4 w-4" />
            <AlertTitle>未找到数据库连接</AlertTitle>
            <AlertDescription className="mt-2">
              <p className="mb-4">
                您还没有配置任何数据库连接。请先创建一个连接后再开始对话。
              </p>
              <Button onClick={() => navigate('/connections')}>
                <Database className="mr-2 h-4 w-4" />
                前往连接管理
              </Button>
            </AlertDescription>
          </Alert>
        </div>
      </AppLayout>
    );
  }

  // 如果没有选择连接，显示选择提示
  if (!currentConnection) {
    return (
      <AppLayout>
        <div className="py-2">
          <Alert>
            <Database className="h-4 w-4" />
            <AlertTitle>请选择数据库连接</AlertTitle>
            <AlertDescription className="mt-2">
              <p className="mb-4">
                请在顶部菜单选择一个数据库连接后开始对话。
              </p>
              <Button onClick={() => navigate('/connections')}>
                查看所有连接
              </Button>
            </AlertDescription>
          </Alert>
        </div>
      </AppLayout>
    );
  }

  return (
    <AppLayout>
      <div className="flex-1 min-h-0 flex flex-col">
        <ChatContainer />
      </div>
    </AppLayout>
  );
}
