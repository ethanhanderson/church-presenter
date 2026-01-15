/**
 * SaveReportDialog - Report of save and auto-save status
 */

import type { ReactElement } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { ScrollArea } from '@/components/ui/scroll-area';
import { CheckCircle2, AlertTriangle, AlertCircle, Info } from 'lucide-react';
import { cn } from '@/lib/utils';

export type SaveReportStatus = 'success' | 'warning' | 'error' | 'info';

export interface SaveReportItem {
  status: SaveReportStatus;
  title: string;
  detail?: string;
}

interface SaveReportDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  items: SaveReportItem[];
}

const statusStyles: Record<
  SaveReportStatus,
  { badge: string; row: string; icon: ReactElement; label: string }
> = {
  success: {
    badge: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900 dark:text-emerald-200',
    row: 'border-emerald-200 dark:border-emerald-800 bg-emerald-50/60 dark:bg-emerald-950/30',
    icon: <CheckCircle2 className="h-3.5 w-3.5" />,
    label: 'Saved',
  },
  warning: {
    badge: 'bg-amber-100 text-amber-700 dark:bg-amber-900 dark:text-amber-200',
    row: 'border-amber-200 dark:border-amber-800 bg-amber-50/60 dark:bg-amber-950/30',
    icon: <AlertTriangle className="h-3.5 w-3.5" />,
    label: 'Attention',
  },
  error: {
    badge: 'bg-red-100 text-red-700 dark:bg-red-900 dark:text-red-200',
    row: 'border-red-200 dark:border-red-800 bg-red-50/60 dark:bg-red-950/30',
    icon: <AlertCircle className="h-3.5 w-3.5" />,
    label: 'Error',
  },
  info: {
    badge: 'bg-blue-100 text-blue-700 dark:bg-blue-900 dark:text-blue-200',
    row: 'border-blue-200 dark:border-blue-800 bg-blue-50/60 dark:bg-blue-950/30',
    icon: <Info className="h-3.5 w-3.5" />,
    label: 'Info',
  },
};

export function SaveReportDialog({ open, onOpenChange, items }: SaveReportDialogProps) {
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-xl">
        <DialogHeader>
          <DialogTitle>Save & Auto-Save Report</DialogTitle>
          <DialogDescription>
            Review items that were saved or still need attention.
          </DialogDescription>
        </DialogHeader>

        <ScrollArea className="max-h-[50vh] pr-2">
          <div className="space-y-2">
            {items.length === 0 ? (
              <div className="rounded-md border border-dashed p-4 text-sm text-muted-foreground">
                No report items available.
              </div>
            ) : (
              items.map((item, index) => {
                const styles = statusStyles[item.status];
                return (
                  <div
                    key={`${item.status}-${index}`}
                    className={cn('rounded-md border p-3 text-sm', styles.row)}
                  >
                    <div className="flex items-center gap-2">
                      <Badge variant="secondary" className={cn('text-xs', styles.badge)}>
                        <span className="mr-1">{styles.icon}</span>
                        {styles.label}
                      </Badge>
                      <span className="font-medium">{item.title}</span>
                    </div>
                    {item.detail && (
                      <div className="mt-1 text-xs text-muted-foreground">
                        {item.detail}
                      </div>
                    )}
                  </div>
                );
              })
            )}
          </div>
        </ScrollArea>

        <DialogFooter>
          <Button onClick={() => onOpenChange(false)}>Close</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
