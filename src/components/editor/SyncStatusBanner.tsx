/**
 * SyncStatusBanner - Shows sync status for linked presentations
 */

import {
  Cloud,
  CloudOff,
  AlertTriangle,
  RefreshCw,
  ExternalLink,
  Link2,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import type { SyncMetadata, ExternalSongLink } from '@/lib/models';
import { openUrl } from '@tauri-apps/plugin-opener';
import { cn } from '@/lib/utils';

interface SyncStatusBannerProps {
  externalSong?: ExternalSongLink;
  sync?: SyncMetadata;
  className?: string;
}

const statusConfig = {
  linked: {
    icon: Link2,
    text: 'Linked to Music Manager',
    variant: 'secondary' as const,
    className: 'bg-blue-500/10 text-blue-500 border-blue-500/20',
  },
  synced: {
    icon: Cloud,
    text: 'Synced with Music Manager',
    variant: 'secondary' as const,
    className: 'bg-green-500/10 text-green-500 border-green-500/20',
  },
  pending: {
    icon: RefreshCw,
    text: 'Sync pending...',
    variant: 'secondary' as const,
    className: 'bg-yellow-500/10 text-yellow-500 border-yellow-500/20',
  },
  conflict: {
    icon: AlertTriangle,
    text: 'Sync conflict detected',
    variant: 'destructive' as const,
    className: 'bg-orange-500/10 text-orange-500 border-orange-500/20',
  },
  error: {
    icon: CloudOff,
    text: 'Sync error',
    variant: 'destructive' as const,
    className: 'bg-red-500/10 text-red-500 border-red-500/20',
  },
};

export function SyncStatusBanner({ externalSong, sync, className }: SyncStatusBannerProps) {
  if (!externalSong) return null;

  const status = sync?.status || 'linked';
  const conflictUrl = sync?.conflictUrl;

  const handleOpenConflict = async () => {
    if (conflictUrl) {
      try {
        await openUrl(conflictUrl);
      } catch (error) {
        console.error('Failed to open conflict URL:', error);
      }
    }
  };

  const config = statusConfig[status];
  const Icon = config.icon;

  return (
    <div
      className={cn(
        'flex items-center gap-2 px-3 py-1.5 text-sm border-b',
        config.className,
        className
      )}
    >
      <Icon className="h-4 w-4 shrink-0" />
      <span className="flex-1 truncate">{config.text}</span>
      
      {status === 'conflict' && conflictUrl && (
        <Button
          variant="ghost"
          size="sm"
          className="h-6 px-2 text-xs"
          onClick={handleOpenConflict}
        >
          <ExternalLink className="mr-1 h-3 w-3" />
          Resolve
        </Button>
      )}
      
      {status === 'pending' && (
        <Badge variant="outline" className="text-xs">
          <RefreshCw className="mr-1 h-3 w-3 animate-spin" />
          Syncing
        </Badge>
      )}
    </div>
  );
}

export function SyncStatusBadge({ externalSong, sync, className }: SyncStatusBannerProps) {
  if (!externalSong) return null;

  const status = sync?.status || 'linked';
  const config = statusConfig[status];
  const Icon = config.icon;

  return (
    <Badge
      variant={config.variant}
      className={cn('h-7 gap-1 border px-2 text-xs', config.className, className)}
    >
      <Icon className="h-3.5 w-3.5" />
      <span className="max-w-48 truncate">{config.text}</span>
    </Badge>
  );
}
