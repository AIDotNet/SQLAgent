import { Header } from './Header';
import { Sidebar } from './Sidebar';

import { Toaster } from "@/components/ui/sonner"

interface AppLayoutProps {
  children: React.ReactNode;
}

export function AppLayout({ children }: AppLayoutProps) {
  return (
    <div className="h-screen flex bg-background overflow-hidden">
      <Sidebar />
      <div className="flex-1 flex flex-col min-w-0 overflow-hidden">
        <Header />
        <main className="flex-1 overflow-hidden">
          {children}
          <Toaster />
        </main>
      </div>
    </div>
  );
}
