/**
 * SaveConflictDialog - Dialog for resolving save conflicts between local and remote versions
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
import { ScrollArea } from '@/components/ui/scroll-area';
import { Badge } from '@/components/ui/badge';
import { Separator } from '@/components/ui/separator';
import {
  AlertTriangle,
  Clock,
  FileText,
  Layers,
  CheckCircle2,
  XCircle,
  ArrowRight,
} from 'lucide-react';
import type { Presentation, Slide } from '@/lib/models';
import { cn } from '@/lib/utils';

interface ConflictData {
  localVersion: Presentation;
  remoteVersion: Presentation;
  filePath: string;
}

interface SaveConflictDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  conflict: ConflictData | null;
  onResolve: (choice: 'local' | 'remote') => void;
}

interface DiffItem {
  type: 'added' | 'removed' | 'modified' | 'unchanged';
  field: string;
  localValue?: string;
  remoteValue?: string;
}

function formatDate(dateString: string | undefined): string {
  if (!dateString) return 'Unknown';
  const date = new Date(dateString);
  return date.toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  });
}

function formatSlideContent(slide: Slide): string {
  const textLayers = slide.layers.filter(l => l.type === 'text');
  if (textLayers.length === 0) return '(empty slide)';
  return textLayers
    .map(l => (l as { content: string }).content)
    .join(' | ')
    .substring(0, 100);
}

function computeDiff(local: Presentation, remote: Presentation): DiffItem[] {
  const diff: DiffItem[] = [];

  // Compare titles
  if (local.manifest.title !== remote.manifest.title) {
    diff.push({
      type: 'modified',
      field: 'Title',
      localValue: local.manifest.title,
      remoteValue: remote.manifest.title,
    });
  }

  // Compare slide counts
  if (local.slides.length !== remote.slides.length) {
    diff.push({
      type: 'modified',
      field: 'Slide Count',
      localValue: `${local.slides.length} slides`,
      remoteValue: `${remote.slides.length} slides`,
    });
  }

  // Compare slide IDs to find added/removed
  const localSlideIds = new Set(local.slides.map(s => s.id));
  const remoteSlideIds = new Set(remote.slides.map(s => s.id));

  const addedSlides = local.slides.filter(s => !remoteSlideIds.has(s.id));
  const removedSlides = remote.slides.filter(s => !localSlideIds.has(s.id));

  for (const slide of addedSlides) {
    diff.push({
      type: 'added',
      field: `Slide: ${slide.sectionLabel || slide.type}`,
      localValue: formatSlideContent(slide),
    });
  }

  for (const slide of removedSlides) {
    diff.push({
      type: 'removed',
      field: `Slide: ${slide.sectionLabel || slide.type}`,
      remoteValue: formatSlideContent(slide),
    });
  }

  // Compare modified slides (same ID, different content)
  for (const localSlide of local.slides) {
    const remoteSlide = remote.slides.find(s => s.id === localSlide.id);
    if (remoteSlide) {
      const localContent = formatSlideContent(localSlide);
      const remoteContent = formatSlideContent(remoteSlide);
      if (localContent !== remoteContent) {
        diff.push({
          type: 'modified',
          field: `Slide: ${localSlide.sectionLabel || localSlide.type}`,
          localValue: localContent,
          remoteValue: remoteContent,
        });
      }
    }
  }

  // Compare theme count
  if (local.themes.length !== remote.themes.length) {
    diff.push({
      type: 'modified',
      field: 'Theme Count',
      localValue: `${local.themes.length} themes`,
      remoteValue: `${remote.themes.length} themes`,
    });
  }

  // Compare media count
  if (local.manifest.media.length !== remote.manifest.media.length) {
    diff.push({
      type: 'modified',
      field: 'Media Assets',
      localValue: `${local.manifest.media.length} files`,
      remoteValue: `${remote.manifest.media.length} files`,
    });
  }

  return diff;
}

export function SaveConflictDialog({
  open,
  onOpenChange,
  conflict,
  onResolve,
}: SaveConflictDialogProps) {
  const diff = useMemo(() => {
    if (!conflict) return [];
    return computeDiff(conflict.localVersion, conflict.remoteVersion);
  }, [conflict]);

  if (!conflict) return null;

  const { localVersion, remoteVersion } = conflict;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl max-h-[90vh] flex flex-col">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-amber-600 dark:text-amber-500">
            <AlertTriangle className="h-5 w-5" />
            Save Conflict Detected
          </DialogTitle>
          <DialogDescription>
            The presentation has been modified in another location. Choose which version to keep.
          </DialogDescription>
        </DialogHeader>

        <div className="flex-1 min-h-0 space-y-4">
          {/* Version Comparison Header */}
          <div className="grid grid-cols-2 gap-4">
            {/* Local Version */}
            <div className="p-3 rounded-lg border bg-blue-50 dark:bg-blue-950/30 border-blue-200 dark:border-blue-800">
              <div className="flex items-center justify-between mb-2">
                <Badge variant="outline" className="bg-blue-100 dark:bg-blue-900 border-blue-300">
                  Your Changes
                </Badge>
              </div>
              <div className="space-y-1 text-sm">
                <div className="flex items-center gap-2 text-muted-foreground">
                  <FileText className="h-3.5 w-3.5" />
                  <span className="truncate font-medium text-foreground">
                    {localVersion.manifest.title}
                  </span>
                </div>
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Clock className="h-3.5 w-3.5" />
                  <span>{formatDate(localVersion.manifest.updatedAt)}</span>
                </div>
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Layers className="h-3.5 w-3.5" />
                  <span>{localVersion.slides.length} slides</span>
                </div>
              </div>
            </div>

            {/* Remote Version */}
            <div className="p-3 rounded-lg border bg-emerald-50 dark:bg-emerald-950/30 border-emerald-200 dark:border-emerald-800">
              <div className="flex items-center justify-between mb-2">
                <Badge variant="outline" className="bg-emerald-100 dark:bg-emerald-900 border-emerald-300">
                  Saved Version
                </Badge>
              </div>
              <div className="space-y-1 text-sm">
                <div className="flex items-center gap-2 text-muted-foreground">
                  <FileText className="h-3.5 w-3.5" />
                  <span className="truncate font-medium text-foreground">
                    {remoteVersion.manifest.title}
                  </span>
                </div>
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Clock className="h-3.5 w-3.5" />
                  <span>{formatDate(remoteVersion.manifest.updatedAt)}</span>
                </div>
                <div className="flex items-center gap-2 text-muted-foreground">
                  <Layers className="h-3.5 w-3.5" />
                  <span>{remoteVersion.slides.length} slides</span>
                </div>
              </div>
            </div>
          </div>

          <Separator />

          {/* Differences List */}
          <div>
            <h4 className="text-sm font-medium mb-2 flex items-center gap-2">
              <ArrowRight className="h-4 w-4" />
              Differences ({diff.length})
            </h4>
            
            <ScrollArea className="h-[200px] border rounded-md">
              {diff.length === 0 ? (
                <div className="p-4 text-center text-muted-foreground text-sm">
                  No significant differences detected (timestamps only)
                </div>
              ) : (
                <div className="p-2 space-y-2">
                  {diff.map((item, index) => (
                    <DiffItemView key={index} item={item} />
                  ))}
                </div>
              )}
            </ScrollArea>
          </div>
        </div>

        <DialogFooter className="gap-2 sm:gap-0">
          <Button
            variant="outline"
            onClick={() => onOpenChange(false)}
          >
            Cancel
          </Button>
          <Button
            variant="outline"
            className="border-emerald-500 text-emerald-600 hover:bg-emerald-50 dark:hover:bg-emerald-950"
            onClick={() => {
              onResolve('remote');
              onOpenChange(false);
            }}
          >
            <XCircle className="mr-2 h-4 w-4" />
            Discard My Changes
          </Button>
          <Button
            className="bg-blue-600 hover:bg-blue-700"
            onClick={() => {
              onResolve('local');
              onOpenChange(false);
            }}
          >
            <CheckCircle2 className="mr-2 h-4 w-4" />
            Keep My Changes
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

interface DiffItemViewProps {
  item: DiffItem;
}

function DiffItemView({ item }: DiffItemViewProps) {
  const typeStyles = {
    added: 'bg-blue-50 dark:bg-blue-950/30 border-blue-200 dark:border-blue-800',
    removed: 'bg-red-50 dark:bg-red-950/30 border-red-200 dark:border-red-800',
    modified: 'bg-amber-50 dark:bg-amber-950/30 border-amber-200 dark:border-amber-800',
    unchanged: 'bg-muted border-border',
  };

  const typeBadgeStyles = {
    added: 'bg-blue-100 dark:bg-blue-900 text-blue-700 dark:text-blue-300',
    removed: 'bg-red-100 dark:bg-red-900 text-red-700 dark:text-red-300',
    modified: 'bg-amber-100 dark:bg-amber-900 text-amber-700 dark:text-amber-300',
    unchanged: 'bg-muted text-muted-foreground',
  };

  const typeLabels = {
    added: 'Added',
    removed: 'Removed',
    modified: 'Changed',
    unchanged: 'Same',
  };

  return (
    <div className={cn('p-2 rounded border text-sm', typeStyles[item.type])}>
      <div className="flex items-center gap-2 mb-1">
        <Badge variant="secondary" className={cn('text-xs', typeBadgeStyles[item.type])}>
          {typeLabels[item.type]}
        </Badge>
        <span className="font-medium">{item.field}</span>
      </div>
      
      {item.type === 'modified' && (
        <div className="grid grid-cols-2 gap-2 mt-1 text-xs">
          <div className="p-1.5 rounded bg-blue-100/50 dark:bg-blue-900/30 truncate">
            <span className="text-muted-foreground">Yours: </span>
            <span>{item.localValue}</span>
          </div>
          <div className="p-1.5 rounded bg-emerald-100/50 dark:bg-emerald-900/30 truncate">
            <span className="text-muted-foreground">Saved: </span>
            <span>{item.remoteValue}</span>
          </div>
        </div>
      )}
      
      {item.type === 'added' && item.localValue && (
        <div className="mt-1 text-xs text-muted-foreground truncate">
          {item.localValue}
        </div>
      )}
      
      {item.type === 'removed' && item.remoteValue && (
        <div className="mt-1 text-xs text-muted-foreground truncate">
          {item.remoteValue}
        </div>
      )}
    </div>
  );
}
