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
      <div className="space-y-8">
        <div className="flex justify-between items-center">
          <div>
            <h1 className="text-3xl font-bold">数据库连接</h1>
            <p className="text-muted-foreground mt-2">
              管理和配置您的数据库连接
            </p>
          </div>
          <Button onClick={() => setIsCreateDialogOpen(true)} size="lg">
            <Plus className="mr-2 h-4 w-4" />
            新建连接
          </Button>
        </div>

        <ConnectionList />

        <Dialog open={isCreateDialogOpen} onOpenChange={setIsCreateDialogOpen}>
          <DialogContent className="sm:max-w-[600px]">
            <DialogHeader>
              <DialogTitle>创建新连接</DialogTitle>
              <DialogDescription>
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
