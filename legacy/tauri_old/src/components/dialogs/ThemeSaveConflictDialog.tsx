/**
 * ThemeSaveConflictDialog - Resolve conflicts for theme saves
 */

import { useMemo } from 'react';
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
import { Separator } from '@/components/ui/separator';
import { AlertTriangle, Clock, Layers, FileText } from 'lucide-react';
import type { ThemeTemplate } from '@/lib/models';
import type { ThemeConflictData } from '@/hooks';

interface ThemeSaveConflictDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  conflict: ThemeConflictData | null;
  onResolve: (choice: 'local' | 'remote') => void;
}

const getLatestThemeUpdatedAt = (themes: ThemeTemplate[]) =>
  themes.reduce<string | null>((latest, theme) => {
    if (!theme.updatedAt) return latest;
    if (!latest) return theme.updatedAt;
    return new Date(theme.updatedAt) > new Date(latest) ? theme.updatedAt : latest;
  }, null);

function formatDate(dateString: string | null | undefined): string {
  if (!dateString) return 'Unknown';
  const date = new Date(dateString);
  return date.toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}

export function ThemeSaveConflictDialog({
  open,
  onOpenChange,
  conflict,
  onResolve,
}: ThemeSaveConflictDialogProps) {
  const summary = useMemo(() => {
    if (!conflict) return null;
    const { localThemes, remoteThemes } = conflict;
    return {
      localCount: localThemes.length,
      remoteCount: remoteThemes.length,
      localUpdatedAt: getLatestThemeUpdatedAt(localThemes),
      remoteUpdatedAt: getLatestThemeUpdatedAt(remoteThemes),
    };
  }, [conflict]);

  if (!conflict || !summary) return null;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-amber-600 dark:text-amber-500">
            <AlertTriangle className="h-5 w-5" />
            Theme Save Conflict
          </DialogTitle>
          <DialogDescription>
            Themes were modified in another location. Choose which version to keep.
          </DialogDescription>
        </DialogHeader>

        <div className="grid grid-cols-2 gap-4 text-sm">
          <div className="rounded-lg border bg-blue-50 dark:bg-blue-950/30 border-blue-200 dark:border-blue-800 p-3">
            <div className="flex items-center justify-between mb-2">
              <Badge variant="outline" className="bg-blue-100 dark:bg-blue-900 border-blue-300">
                Your Changes
              </Badge>
            </div>
            <div className="space-y-2 text-muted-foreground">
              <div className="flex items-center gap-2">
                <Layers className="h-3.5 w-3.5" />
                <span>{summary.localCount} themes</span>
              </div>
              <div className="flex items-center gap-2">
                <Clock className="h-3.5 w-3.5" />
                <span>{formatDate(summary.localUpdatedAt)}</span>
              </div>
            </div>
          </div>

          <div className="rounded-lg border bg-amber-50 dark:bg-amber-950/30 border-amber-200 dark:border-amber-800 p-3">
            <div className="flex items-center justify-between mb-2">
              <Badge variant="outline" className="bg-amber-100 dark:bg-amber-900 border-amber-300">
                On Disk
              </Badge>
            </div>
            <div className="space-y-2 text-muted-foreground">
              <div className="flex items-center gap-2">
                <Layers className="h-3.5 w-3.5" />
                <span>{summary.remoteCount} themes</span>
              </div>
              <div className="flex items-center gap-2">
                <Clock className="h-3.5 w-3.5" />
                <span>{formatDate(summary.remoteUpdatedAt)}</span>
              </div>
            </div>
          </div>
        </div>

        <Separator />

        <div className="flex items-center gap-2 text-xs text-muted-foreground">
          <FileText className="h-3.5 w-3.5" />
          <span className="truncate">{conflict.filePath}</span>
        </div>

        <DialogFooter className="gap-2 sm:gap-0">
          <Button variant="outline" onClick={() => onResolve('remote')}>
            Keep Disk Version
          </Button>
          <Button onClick={() => onResolve('local')}>Keep My Changes</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
