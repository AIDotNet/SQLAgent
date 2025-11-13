import { useState } from 'react';
import { Plus } from 'lucide-react';
import { AppLayout } from '../components/layout/AppLayout';
import { ConnectionList } from '../components/connections/ConnectionList';
import { ConnectionForm } from '../components/connections/ConnectionForm';
import { Button } from '../components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '../components/ui/dialog';

export default function ConnectionsPage() {
  const [isCreateDialogOpen, setIsCreateDialogOpen] = useState(false);

  return (
    <AppLayout>
      <div className="h-full flex flex-col overflow-hidden">
        {/* Header Section */}
        <div className="flex-shrink-0 border-b bg-background">
          <div className="px-6 py-4 md:px-8">
            <div className="max-w-7xl mx-auto">
              <div className="flex justify-between items-center">
                <div>
                  <h1 className="text-2xl font-bold">数据库连接管理</h1>
                  <p className="text-muted-foreground mt-1 text-sm">
                    管理和配置您的数据库连接
                  </p>
                </div>
                <Button 
                  onClick={() => setIsCreateDialogOpen(true)} 
                  size="default"
                >
                  <Plus className="mr-2 h-4 w-4" />
                  新建连接
                </Button>
              </div>
            </div>
          </div>
        </div>

        {/* Content Section with scroll */}
        <div className="flex-1 overflow-y-auto">
          <div className="px-6 py-6 md:px-8 md:py-8">
            <div className="max-w-7xl mx-auto">
              <ConnectionList />
            </div>
          </div>
        </div>

        {/* Create Connection Dialog */}
        <Dialog open={isCreateDialogOpen} onOpenChange={setIsCreateDialogOpen}>
          <DialogContent className="sm:max-w-[600px] max-h-[90vh] overflow-y-auto">
            <DialogHeader>
              <DialogTitle className="text-2xl">创建新连接</DialogTitle>
              <DialogDescription className="text-base">
                配置您的数据库连接信息，支持多种数据库类型。
              </DialogDescription>
            </DialogHeader>
            <ConnectionForm
              onSuccess={() => {
                setIsCreateDialogOpen(false);
              }}
              onCancel={() => setIsCreateDialogOpen(false)}
            />
          </DialogContent>
        </Dialog>
      </div>
    </AppLayout>
  );
}
