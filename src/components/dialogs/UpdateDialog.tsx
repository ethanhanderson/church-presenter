/**
 * UpdateDialog - check and install app updates
 */

import { useEffect, useMemo, useState } from 'react';
import { check } from '@tauri-apps/plugin-updater';
import { relaunch } from '@tauri-apps/plugin-process';
import { getVersion } from '@tauri-apps/api/app';
import { useSettingsStore } from '@/lib/stores';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Separator } from '@/components/ui/separator';

type UpdateInfo = Awaited<ReturnType<typeof check>>;

type UpdateStatus =
  | 'idle'
  | 'checking'
  | 'available'
  | 'downloading'
  | 'installed'
  | 'up-to-date'
  | 'error';

interface UpdateDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  initialUpdate?: UpdateInfo;
}

export function UpdateDialog({ open, onOpenChange, initialUpdate }: UpdateDialogProps) {
  const { settings, updateSettings } = useSettingsStore();
  const [status, setStatus] = useState<UpdateStatus>('idle');
  const [update, setUpdate] = useState<UpdateInfo>(null);
  const [error, setError] = useState<string | null>(null);
  const [progress, setProgress] = useState<{ downloaded: number; total?: number }>({
    downloaded: 0,
  });
  const [currentVersion, setCurrentVersion] = useState<string>('...');

  const isTauri = useMemo(
    () => '__TAURI_INTERNALS__' in window || '__TAURI__' in window,
    []
  );

  useEffect(() => {
    if (!open) return;
    setError(null);
    setProgress({ downloaded: 0 });
    if (initialUpdate) {
      setUpdate(initialUpdate);
      setStatus('available');
    } else {
      setUpdate(null);
      setStatus('idle');
    }
  }, [initialUpdate, open]);

  useEffect(() => {
    if (!open || !isTauri) return;
    getVersion()
      .then(setCurrentVersion)
      .catch(() => setCurrentVersion('unknown'));
  }, [open, isTauri]);

  const handleCheck = async () => {
    if (!isTauri) return;
    setStatus('checking');
    setError(null);
    updateSettings({
      updates: { ...settings.updates, lastCheckedAt: new Date().toISOString() },
    });
    try {
      const result = await check();
      if (result) {
        setUpdate(result);
        setStatus('available');
      } else {
        setUpdate(null);
        setStatus('up-to-date');
      }
    } catch (err) {
      setStatus('error');
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const handleInstall = async () => {
    if (!update) return;
    setStatus('downloading');
    setError(null);
    try {
      await update.downloadAndInstall((event) => {
        switch (event.event) {
          case 'Started':
            setProgress({ downloaded: 0, total: event.data.contentLength });
            break;
          case 'Progress':
            setProgress((prev) => ({
              downloaded: prev.downloaded + event.data.chunkLength,
              total: prev.total,
            }));
            break;
          case 'Finished':
            setStatus('installed');
            break;
        }
      });
    } catch (err) {
      setStatus('error');
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const handleRelaunch = async () => {
    try {
      await relaunch();
    } catch (err) {
      setStatus('error');
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const renderStatus = () => {
    if (!isTauri) {
      return 'Updates are only available in the desktop app.';
    }
    switch (status) {
      case 'checking':
        return 'Checking for updates...';
      case 'available':
        return `Update ${update?.version ?? ''} is available.`;
      case 'downloading':
        return 'Downloading update...';
      case 'installed':
        return 'Update installed. Restart to finish.';
      case 'up-to-date':
        return 'You are up to date.';
      case 'error':
        return error || 'Update failed.';
      default:
        return 'Check for updates to keep Church Presenter current.';
    }
  };

  const renderProgress = () => {
    if (status !== 'downloading') return null;
    const total = progress.total ?? 0;
    if (!total) return `${Math.round(progress.downloaded / 1024)} KB downloaded`;
    const percent = Math.round((progress.downloaded / total) * 100);
    return `${percent}% (${Math.round(progress.downloaded / 1024)} KB of ${Math.round(
      total / 1024
    )} KB)`;
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Updates</DialogTitle>
          <DialogDescription>Current version: {currentVersion}</DialogDescription>
        </DialogHeader>

        <div className="space-y-3 text-sm">
          <p>{renderStatus()}</p>
          {status === 'available' && update?.body && (
            <>
              <Separator />
              <div className="space-y-1">
                <p className="text-xs text-muted-foreground">Release notes</p>
                <p className="whitespace-pre-wrap text-xs">{update.body}</p>
              </div>
            </>
          )}
          {status === 'downloading' && (
            <p className="text-xs text-muted-foreground">{renderProgress()}</p>
          )}
        </div>

        <div className="flex flex-wrap gap-2 justify-end">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Close
          </Button>
          <Button
            variant="secondary"
            onClick={handleCheck}
            disabled={!isTauri || status === 'checking' || status === 'downloading'}
          >
            Check for updates
          </Button>
          {status === 'available' && (
            <Button onClick={handleInstall} disabled={!isTauri}>
              Download & Install
            </Button>
          )}
          {status === 'installed' && (
            <Button onClick={handleRelaunch} disabled={!isTauri}>
              Restart now
            </Button>
          )}
        </div>
      </DialogContent>
    </Dialog>
  );
}
