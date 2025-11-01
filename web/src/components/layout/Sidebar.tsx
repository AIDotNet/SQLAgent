import { Link, useLocation } from 'react-router-dom';
import { MessageSquare, Database, Settings, Brain } from 'lucide-react';

export function Sidebar() {
  const location = useLocation();

  const items = [
    { to: '/', icon: MessageSquare, label: '对话' },
    { to: '/connections', icon: Database, label: '连接管理' },
    { to: '/providers', icon: Brain, label: 'AI 提供商' },
  ];

  return (
    <aside className="w-16 shrink-0 border-r bg-sidebar text-sidebar-foreground flex flex-col items-center justify-between py-2">
      <div className="flex flex-col items-center gap-2">
        <Link
          to="/"
          aria-label="SQLBox"
          title="SQLBox"
          className="mb-2 mt-1 inline-flex items-center justify-center w-10 h-10 rounded-lg"
        >
          <Database className="w-6 h-6" />
        </Link>

        <nav className="flex flex-col items-center gap-1">
          {items.map((item) => {
            const active = location.pathname === item.to;
            const Icon = item.icon;
            return (
              <Link
                key={item.to}
                to={item.to}
                aria-label={item.label}
                title={item.label}
                className={
                  `w-12 h-12 rounded-md inline-flex items-center justify-center transition-colors
                  focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50
                  ${active ? 'bg-sidebar-accent text-sidebar-accent-foreground' : 'text-muted-foreground hover:bg-sidebar-accent hover:text-foreground'}`
                }
              >
                <Icon className="w-6 h-6" />
              </Link>
            );
          })}
        </nav>
      </div>

      <div className="flex flex-col items-center">
        <Link
          to="/settings"
          aria-label="系统设置"
          title="系统设置"
          className="w-12 h-12 rounded-md inline-flex items-center justify-center text-muted-foreground hover:bg-sidebar-accent hover:text-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring/50"
        >
          <Settings className="w-6 h-6" />
        </Link>
      </div>
    </aside>
  );
}