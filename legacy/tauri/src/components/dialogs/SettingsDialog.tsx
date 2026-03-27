/**
 * SettingsDialog - App settings configuration
 */

import { useMemo, useState, useEffect } from 'react';
import {
  CloudDownload,
  Check,
  MonitorPlay,
  Palette,
  Pencil,
  Plug,
  Rows3,
  Shuffle,
} from 'lucide-react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { Slider } from '@/components/ui/slider';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Switch } from '@/components/ui/switch';
import { TabsContent } from '@/components/ui/tabs';
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group';
import { VerticalTabs, type VerticalTabItem } from '@/components/tabs';
import {
  useCatalogStore,
  useEditorStore,
  useLiveStore,
  useSettingsStore,
  useWorkspaceStore,
} from '@/lib/stores';
import {
  allowMediaLibraryDir,
  getMonitors,
  setContentDir,
  type MonitorInfo,
} from '@/lib/tauri-api';
import { open as openDialog } from '@tauri-apps/plugin-dialog';
import { getVersion } from '@tauri-apps/api/app';
import { cn } from '@/lib/utils';
import {
  getDocumentsDataDirPath,
  getMediaLibraryDirPath,
  initializeAppData,
} from '@/lib/services/appDataService';

interface SettingsDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function SettingsDialog({ open, onOpenChange }: SettingsDialogProps) {
  const { settings, updateSettings, setTheme, remapRecentFilePaths } = useSettingsStore();
  const { remapPresentationPaths } = useCatalogStore();
  const { remapSelectedPresentationPath } = useWorkspaceStore();
  const { remapPresentationPath } = useLiveStore();
  const { autoSave, remapFilePath } = useEditorStore();
  const [monitors, setMonitors] = useState<MonitorInfo[]>([]);
  const [selectedMonitors, setSelectedMonitors] = useState<string[]>(
    settings.output.monitorIds || []
  );
  const [currentVersion, setCurrentVersion] = useState<string>('...');
  const isTauriApp = useMemo(
    () => '__TAURI_INTERNALS__' in window || '__TAURI__' in window,
    []
  );

  useEffect(() => {
    if (open) {
      let active = true;
      const fetchMonitors = () => {
        getMonitors()
          .then((next) => {
            if (active) setMonitors(next);
          })
          .catch(console.error);
      };

      fetchMonitors();
      const interval = window.setInterval(fetchMonitors, 2000);
      return () => {
        active = false;
        window.clearInterval(interval);
      };
    }
  }, [open]);

  useEffect(() => {
    setSelectedMonitors(settings.output.monitorIds || []);
  }, [settings.output.monitorIds]);

  useEffect(() => {
    if (!open) return;
    if (!isTauriApp) {
      setCurrentVersion('Web preview');
      return;
    }
    getVersion()
      .then(setCurrentVersion)
      .catch(() => setCurrentVersion('unknown'));
  }, [isTauriApp, open]);

  const formatLastChecked = (timestamp?: string | null) => {
    if (!timestamp) return 'Never';
    const parsed = Date.parse(timestamp);
    if (Number.isNaN(parsed)) return 'Unknown';
    return new Date(parsed).toLocaleString();
  };

  const handleMonitorSelection = (next: string[]) => {
    setSelectedMonitors(next);
    updateSettings({
      output: { ...settings.output, monitorIds: next },
    });
  };

  const monitorPreview = useMemo(() => {
    if (monitors.length === 0) return null;
    const minX = Math.min(...monitors.map((m) => m.x));
    const minY = Math.min(...monitors.map((m) => m.y));
    const maxX = Math.max(...monitors.map((m) => m.x + m.width));
    const maxY = Math.max(...monitors.map((m) => m.y + m.height));
    return {
      viewBox: `${minX} ${minY} ${maxX - minX} ${maxY - minY}`,
    };
  }, [monitors]);

  const handleSelectContentFolder = async () => {
    const isTauriApp = '__TAURI_INTERNALS__' in window || '__TAURI__' in window;
    if (!isTauriApp) return;
    const currentPath = await getDocumentsDataDirPath();
    const selection = await openDialog({ directory: true, multiple: false });
    const selectedPath = Array.isArray(selection) ? selection[0] : selection;
    if (!selectedPath) return;

    try {
      await setContentDir(selectedPath, {
        moveExisting: true,
        mediaLibraryDir: settings.mediaLibraryDir ?? null,
      });
      await initializeAppData();

      const newContentDir = await getDocumentsDataDirPath();
      const mediaLibraryDir = await getMediaLibraryDirPath();
      await allowMediaLibraryDir(mediaLibraryDir);

      updateSettings({ contentDir: newContentDir, mediaLibraryDir });
      remapPresentationPaths(currentPath, newContentDir);
      remapRecentFilePaths(currentPath, newContentDir);
      remapSelectedPresentationPath(currentPath, newContentDir);
      remapFilePath(currentPath, newContentDir);
      remapPresentationPath(currentPath, newContentDir);
    } catch (error) {
      console.error('Failed to set content folder:', error);
    }
  };

  const autoSaveConfig = useMemo(() => {
    const config = {
      idle: {
        label: 'Idle',
        className: 'bg-muted text-muted-foreground border-muted/40',
      },
      pending: {
        label: 'Pending',
        className: 'bg-yellow-500/10 text-yellow-600 border-yellow-500/20',
      },
      saving: {
        label: 'Saving',
        className: 'bg-blue-500/10 text-blue-600 border-blue-500/20',
      },
      saved: {
        label: 'Saved',
        className: 'bg-green-500/10 text-green-600 border-green-500/20',
      },
      conflict: {
        label: 'Conflict',
        className: 'bg-orange-500/10 text-orange-600 border-orange-500/20',
      },
      error: {
        label: 'Error',
        className: 'bg-red-500/10 text-red-600 border-red-500/20',
      },
    };

    return config[autoSave.status] || config.idle;
  }, [autoSave.status]);

  const autoSaveDetail = useMemo(() => {
    if (autoSave.status === 'error' && autoSave.lastError) {
      return autoSave.lastError;
    }
    if (autoSave.lastSaved) {
      const timestamp = new Date(autoSave.lastSaved);
      if (!Number.isNaN(timestamp.getTime())) {
        return `Last saved ${timestamp.toLocaleString()}`;
      }
    }
    return 'Not saved yet';
  }, [autoSave.lastError, autoSave.lastSaved, autoSave.status]);

  const settingsTabs: VerticalTabItem[] = [
    {
      value: 'output',
      label: 'Output',
      icon: <MonitorPlay aria-hidden="true" className="-ms-0.5 me-1.5 opacity-60" size={16} />,
    },
    {
      value: 'show',
      label: 'Show',
      icon: <Rows3 aria-hidden="true" className="-ms-0.5 me-1.5 opacity-60" size={16} />,
    },
    {
      value: 'editor',
      label: 'Edit',
      icon: <Pencil aria-hidden="true" className="-ms-0.5 me-1.5 opacity-60" size={16} />,
    },
    {
      value: 'reflow',
      label: 'Reflow',
      icon: <Shuffle aria-hidden="true" className="-ms-0.5 me-1.5 opacity-60" size={16} />,
    },
    {
      value: 'integrations',
      label: 'Integrations',
      icon: <Plug aria-hidden="true" className="-ms-0.5 me-1.5 opacity-60" size={16} />,
    },
    {
      value: 'appearance',
      label: 'Appearance',
      icon: <Palette aria-hidden="true" className="-ms-0.5 me-1.5 opacity-60" size={16} />,
    },
    {
      value: 'updates',
      label: 'Updates',
      icon: <CloudDownload aria-hidden="true" className="-ms-0.5 me-1.5 opacity-60" size={16} />,
    },
  ];

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-6xl">
        <DialogHeader>
          <DialogTitle>Settings</DialogTitle>
          <DialogDescription>
            Configure Church Presenter settings
          </DialogDescription>
        </DialogHeader>

        <VerticalTabs defaultValue="output" className="mt-4" tabs={settingsTabs}>
          <ScrollArea className="h-[70vh]">
          <TabsContent value="output" className="space-y-6 p-4">
            <div className="space-y-6">
              <div className="space-y-2">
                <p className="text-sm font-semibold text-foreground">Audience Displays</p>
                <div className="rounded-md border bg-card/50">
                  <div className="flex flex-col gap-4 px-4 py-4">
                    <div className="flex items-center justify-center rounded-md border bg-muted/40 p-4">
                      {monitorPreview ? (
                        <svg
                          viewBox={monitorPreview.viewBox}
                          className="h-52 w-full"
                          preserveAspectRatio="xMidYMid meet"
                        >
                          {monitors.map((monitor) => {
                            const isSelected = selectedMonitors.includes(String(monitor.index));
                            const monitorId = String(monitor.index);
                            const fontSize = Math.max(
                              36,
                              Math.min(monitor.width, monitor.height) * 0.28
                            );
                            return (
                              <g key={monitor.index}>
                                <rect
                                  x={monitor.x}
                                  y={monitor.y}
                                  width={monitor.width}
                                  height={monitor.height}
                                  rx={16}
                                  role="button"
                                  tabIndex={0}
                                  onClick={() => {
                                    const next = isSelected
                                      ? selectedMonitors.filter((id) => id !== monitorId)
                                      : [...selectedMonitors, monitorId];
                                    handleMonitorSelection(next);
                                  }}
                                  onKeyDown={(event) => {
                                    if (event.key === 'Enter' || event.key === ' ') {
                                      event.preventDefault();
                                      const next = isSelected
                                        ? selectedMonitors.filter((id) => id !== monitorId)
                                        : [...selectedMonitors, monitorId];
                                      handleMonitorSelection(next);
                                    }
                                  }}
                                  className={cn(
                                    'cursor-pointer stroke-4',
                                    isSelected
                                      ? 'fill-primary/30 stroke-primary'
                                      : 'fill-background stroke-muted-foreground/40'
                                  )}
                                />
                                <text
                                  x={monitor.x + monitor.width / 2}
                                  y={monitor.y + monitor.height / 2}
                                  textAnchor="middle"
                                  dominantBaseline="middle"
                                  className={cn(
                                    'font-semibold',
                                    isSelected ? 'fill-primary' : 'fill-muted-foreground'
                                  )}
                                  style={{ fontSize }}
                                >
                                  {monitor.index + 1}
                                </text>
                              </g>
                            );
                          })}
                        </svg>
                      ) : (
                        <div className="text-xs text-muted-foreground">No displays detected.</div>
                      )}
                    </div>
                    <ToggleGroup
                      type="multiple"
                      variant="outline"
                      size="sm"
                      spacing={2}
                      value={selectedMonitors}
                      onValueChange={handleMonitorSelection}
                      className="w-full flex-nowrap justify-center gap-3 overflow-x-auto pb-1"
                    >
                      {monitors.map((monitor) => {
                        const id = String(monitor.index);
                        const isSelected = selectedMonitors.includes(id);
                        const previewPadding = Math.max(
                          12,
                          Math.min(monitor.width, monitor.height) * 0.08
                        );
                        return (
                          <ToggleGroupItem
                            key={monitor.index}
                            value={id}
                            aria-label={`Toggle display ${monitor.index + 1}`}
                            className={cn(
                              'flex h-auto min-w-[120px] flex-col items-center gap-2 rounded-lg border px-3 py-2 text-left',
                              isSelected
                                ? 'border-primary/60 bg-primary/10 text-foreground'
                                : 'bg-background/60 text-muted-foreground hover:bg-accent'
                            )}
                          >
                            <svg
                              viewBox={`${-previewPadding} ${-previewPadding} ${
                                monitor.width + previewPadding * 2
                              } ${monitor.height + previewPadding * 2}`}
                              className="h-20 w-full"
                              preserveAspectRatio="xMidYMid meet"
                            >
                              <rect
                                x={0}
                                y={0}
                                width={monitor.width}
                                height={monitor.height}
                                rx={10}
                                className={cn(
                                  'stroke-[2.5]',
                                  isSelected
                                    ? 'fill-primary/30 stroke-primary'
                                    : 'fill-muted/30 stroke-muted-foreground/20'
                                )}
                              />
                              <text
                                x={monitor.width / 2}
                                y={monitor.height / 2}
                                textAnchor="middle"
                                dominantBaseline="middle"
                                className={cn(
                                  'text-[14px] font-semibold',
                                  isSelected ? 'fill-primary' : 'fill-muted-foreground'
                                )}
                              >
                                {monitor.index + 1}
                              </text>
                            </svg>
                            <div className="flex w-full items-center justify-between">
                              <span className="inline-flex h-6 w-6 items-center justify-center rounded-full border border-border bg-background text-[11px] font-semibold text-foreground">
                                {monitor.index + 1}
                              </span>
                              {isSelected && <Check className="h-4 w-4 text-primary" />}
                            </div>
                            <div
                              className={cn(
                                'text-[11px] font-medium',
                                isSelected ? 'text-foreground' : 'text-muted-foreground'
                              )}
                            >
                              {monitor.name || `Display ${monitor.index + 1}`}
                            </div>
                            <div className="text-[10px] text-muted-foreground">
                              {monitor.width}x{monitor.height}
                              {monitor.refresh_rate ? ` · ${monitor.refresh_rate}Hz` : ''}
                            </div>
                          </ToggleGroupItem>
                        );
                      })}
                    </ToggleGroup>
                  </div>
                </div>
              </div>

              <div className="space-y-2">
                <p className="text-sm font-semibold text-foreground">Output Options</p>
                <div className="divide-y rounded-md border bg-card/50">
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <Label>Output Scaling</Label>
                    <div className="flex w-full sm:w-[240px] sm:justify-end">
                      <Select
                        value={settings.output.scaling}
                        onValueChange={(v) =>
                          updateSettings({
                            output: { ...settings.output, scaling: v as 'fit' | 'fill' },
                          })
                        }
                      >
                        <SelectTrigger className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="fit">Fit (letterbox)</SelectItem>
                          <SelectItem value="fill">Fill (crop)</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                  </div>
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <Label>Default Aspect Ratio</Label>
                    <div className="flex w-full sm:w-[240px] sm:justify-end">
                      <Select
                        value={settings.output.aspectRatio}
                        onValueChange={(v) =>
                          updateSettings({
                            output: {
                              ...settings.output,
                              aspectRatio: v as '16:9' | '4:3' | '16:10',
                            },
                          })
                        }
                      >
                        <SelectTrigger className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="16:9">16:9 (Widescreen)</SelectItem>
                          <SelectItem value="4:3">4:3 (Standard)</SelectItem>
                          <SelectItem value="16:10">16:10</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="show" className="space-y-6 p-4">
            <div className="space-y-6">
              <div className="space-y-2">
                <p className="text-sm font-semibold text-foreground">Default View</p>
                <div className="rounded-md border bg-card/50">
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Default Center View</Label>
                      <p className="text-xs text-muted-foreground">
                        Default content shown when nothing is selected
                      </p>
                    </div>
                    <div className="flex w-full sm:w-[240px] sm:justify-end">
                      <Select
                        value={settings.show.defaultCenterView}
                        onValueChange={(v) =>
                          updateSettings({
                            show: {
                              ...settings.show,
                              defaultCenterView: v as 'slides' | 'playlist' | 'library',
                            },
                          })
                        }
                      >
                        <SelectTrigger className="w-full">
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value="slides">Slides</SelectItem>
                          <SelectItem value="playlist">Playlist</SelectItem>
                          <SelectItem value="library">Library</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                  </div>
                </div>
              </div>

              <div className="space-y-2">
                <p className="text-sm font-semibold text-foreground">Slide Thumbnails</p>
                <div className="divide-y rounded-md border bg-card/50">
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <Label>Slide Thumbnail Size</Label>
                    <div className="flex w-full items-center gap-4 sm:w-[320px]">
                      <Slider
                        value={[settings.show.thumbnailSize]}
                        onValueChange={([v]) =>
                          updateSettings({
                            show: { ...settings.show, thumbnailSize: v },
                          })
                        }
                        min={140}
                        max={320}
                        step={20}
                        className="flex-1"
                      />
                      <span className="w-16 text-sm text-muted-foreground">
                        {settings.show.thumbnailSize}px
                      </span>
                    </div>
                  </div>
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Show Slide Labels</Label>
                      <p className="text-xs text-muted-foreground">
                        Display section labels on thumbnails
                      </p>
                    </div>
                    <Switch
                      checked={settings.show.showSlideLabels}
                      onCheckedChange={(checked) =>
                        updateSettings({
                          show: { ...settings.show, showSlideLabels: checked },
                        })
                      }
                    />
                  </div>
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Double-click Takes Live</Label>
                      <p className="text-xs text-muted-foreground">
                        Send slide live when double-clicked
                      </p>
                    </div>
                    <Switch
                      checked={settings.show.autoTakeOnDoubleClick}
                      onCheckedChange={(checked) =>
                        updateSettings({
                          show: { ...settings.show, autoTakeOnDoubleClick: checked },
                        })
                      }
                    />
                  </div>
                </div>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="editor" className="space-y-6 p-4">
            <div className="space-y-6">
              <div className="space-y-2">
                <p className="text-sm font-semibold text-foreground">Auto-Save</p>
                <div className="divide-y rounded-md border bg-card/50">
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Auto-Save</Label>
                      <p className="text-xs text-muted-foreground">
                        Automatically save presentations as you edit (saves quickly after changes stop)
                      </p>
                    </div>
                    <Switch
                      checked={settings.editor.autoSaveEnabled}
                      onCheckedChange={(checked) =>
                        updateSettings({
                          editor: { ...settings.editor, autoSaveEnabled: checked },
                        })
                      }
                    />
                  </div>
                  {settings.editor.autoSaveEnabled && (
                    <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between sm:pl-10">
                      <div className="space-y-1">
                        <Label>Save on Create/Import</Label>
                        <p className="text-xs text-muted-foreground">
                          Automatically save new presentations when created or imported
                        </p>
                      </div>
                      <Switch
                        checked={settings.editor.autoSaveOnCreate}
                        onCheckedChange={(checked) =>
                          updateSettings({
                            editor: { ...settings.editor, autoSaveOnCreate: checked },
                          })
                        }
                      />
                    </div>
                  )}
                </div>
              </div>

              <div className="space-y-2">
                <p className="text-sm font-semibold text-foreground">Content Folder</p>
                <div className="divide-y rounded-md border bg-card/50">
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-start sm:justify-between">
                    <div className="space-y-1">
                      <Label>Content Folder</Label>
                      <p className="text-xs text-muted-foreground">
                        Libraries, playlists, presentations, and media are stored here.
                      </p>
                    </div>
                    <div className="flex w-full flex-col gap-2 sm:w-[360px] sm:items-end">
                      <div className="flex flex-wrap gap-2 sm:justify-end">
                        <Button variant="outline" onClick={handleSelectContentFolder}>
                          Move Folder
                        </Button>
                      </div>
                    </div>
                  </div>
                  <div className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Location</Label>
                      <p className="text-xs text-muted-foreground">
                        Current folder for all local content.
                      </p>
                    </div>
                    <p className="text-xs text-muted-foreground">
                      {settings.contentDir || 'Loading...'}
                    </p>
                  </div>
                  <div className="flex flex-col gap-2 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Sync Status</Label>
                      <p className="text-xs text-muted-foreground">
                        Auto-save status for the active presentation.
                      </p>
                    </div>
                    <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                      <Badge variant="outline" className={autoSaveConfig.className}>
                        {autoSaveConfig.label}
                      </Badge>
                      <span>{autoSaveDetail}</span>
                    </div>
                  </div>
                </div>
              </div>

              <div className="space-y-2">
                <p className="text-sm font-semibold text-foreground">Editor Grid</p>
                <div className="divide-y rounded-md border bg-card/50">
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Show Grid</Label>
                      <p className="text-xs text-muted-foreground">
                        Display alignment grid in the slide editor
                      </p>
                    </div>
                    <Switch
                      checked={settings.editor.showGrid}
                      onCheckedChange={(checked) =>
                        updateSettings({
                          editor: { ...settings.editor, showGrid: checked },
                        })
                      }
                    />
                  </div>
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <div className="space-y-1">
                      <Label>Snap to Grid</Label>
                      <p className="text-xs text-muted-foreground">
                        Snap elements to grid when moving
                      </p>
                    </div>
                    <Switch
                      checked={settings.editor.snapToGrid}
                      onCheckedChange={(checked) =>
                        updateSettings({
                          editor: { ...settings.editor, snapToGrid: checked },
                        })
                      }
                    />
                  </div>
                  <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                    <Label>Grid Size (pixels)</Label>
                    <div className="flex w-full items-center gap-4 sm:w-[320px]">
                      <Slider
                        value={[settings.editor.gridSize]}
                        onValueChange={([v]) =>
                          updateSettings({
                            editor: { ...settings.editor, gridSize: v },
                          })
                        }
                        min={5}
                        max={50}
                        step={5}
                        className="flex-1"
                      />
                      <span className="w-16 text-sm text-muted-foreground">
                        {settings.editor.gridSize}px
                      </span>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="reflow" className="space-y-6 p-4">
            <div className="space-y-2">
              <p className="text-sm font-semibold text-foreground">Reflow Preview</p>
              <div className="divide-y rounded-md border bg-card/50">
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <Label>Text Size</Label>
                  <div className="flex w-full items-center gap-4 sm:w-[320px]">
                    <Slider
                      value={[settings.reflow.textSize]}
                      onValueChange={([v]) =>
                        updateSettings({
                          reflow: { ...settings.reflow, textSize: v },
                        })
                      }
                      min={11}
                      max={18}
                      step={1}
                      className="flex-1"
                    />
                    <span className="w-16 text-sm text-muted-foreground">
                      {settings.reflow.textSize}px
                    </span>
                  </div>
                </div>
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <Label>Preview Density</Label>
                  <div className="flex w-full sm:w-[240px] sm:justify-end">
                    <Select
                      value={settings.reflow.previewDensity}
                      onValueChange={(v) =>
                        updateSettings({
                          reflow: { ...settings.reflow, previewDensity: v as 'comfortable' | 'compact' },
                        })
                      }
                    >
                      <SelectTrigger className="w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="comfortable">Comfortable</SelectItem>
                        <SelectItem value="compact">Compact</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="space-y-1">
                    <Label>Show Slide Labels</Label>
                    <p className="text-xs text-muted-foreground">
                      Display section labels on slide previews
                    </p>
                  </div>
                  <Switch
                    checked={settings.reflow.showSlideLabels}
                    onCheckedChange={(checked) =>
                      updateSettings({
                        reflow: { ...settings.reflow, showSlideLabels: checked },
                      })
                    }
                  />
                </div>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="integrations" className="space-y-6 p-4">
            <div className="space-y-2">
              <p className="text-sm font-semibold text-foreground">Music Manager</p>
              <div className="divide-y rounded-md border bg-card/50">
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="space-y-1">
                    <Label>Default Song Action</Label>
                    <p className="text-xs text-muted-foreground">
                      Preferred action when working with Music Manager songs
                    </p>
                  </div>
                  <div className="flex w-full sm:w-[240px] sm:justify-end">
                    <Select
                      value={settings.integrations.musicManager.defaultSongAction}
                      onValueChange={(v) =>
                        updateSettings({
                          integrations: {
                            ...settings.integrations,
                            musicManager: {
                              ...settings.integrations.musicManager,
                              defaultSongAction: v as 'import' | 'link',
                            },
                          },
                        })
                      }
                    >
                      <SelectTrigger className="w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="import">Import</SelectItem>
                        <SelectItem value="link">Link</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="space-y-1">
                    <Label>Prefer Set Import View</Label>
                    <p className="text-xs text-muted-foreground">
                      Open set import view first when available
                    </p>
                  </div>
                  <Switch
                    checked={settings.integrations.musicManager.preferSetImportView}
                    onCheckedChange={(checked) =>
                      updateSettings({
                        integrations: {
                          ...settings.integrations,
                          musicManager: {
                            ...settings.integrations.musicManager,
                            preferSetImportView: checked,
                          },
                        },
                      })
                    }
                  />
                </div>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="appearance" className="space-y-6 p-4">
            <div className="space-y-2">
              <p className="text-sm font-semibold text-foreground">Appearance</p>
              <div className="divide-y rounded-md border bg-card/50">
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <Label>Theme</Label>
                  <div className="flex w-full sm:w-[240px] sm:justify-end">
                    <Select
                      value={settings.theme}
                      onValueChange={(v) => setTheme(v as 'light' | 'dark' | 'system')}
                    >
                      <SelectTrigger className="w-full">
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="light">Light</SelectItem>
                        <SelectItem value="dark">Dark</SelectItem>
                        <SelectItem value="system">System</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                </div>
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <Label>Recent Files Limit</Label>
                  <div className="flex w-full items-center gap-4 sm:w-[320px]">
                    <Slider
                      value={[settings.maxRecentFiles]}
                      onValueChange={([v]) => updateSettings({ maxRecentFiles: v })}
                      min={5}
                      max={25}
                      step={5}
                      className="flex-1"
                    />
                    <span className="w-16 text-sm text-muted-foreground">
                      {settings.maxRecentFiles}
                    </span>
                  </div>
                </div>
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <Label>Clear Recent Files</Label>
                  <Button
                    variant="outline"
                    onClick={() => useSettingsStore.getState().clearRecentFiles()}
                  >
                    Clear
                  </Button>
                </div>
              </div>
            </div>
          </TabsContent>

          <TabsContent value="updates" className="space-y-6 p-4">
            <div className="space-y-2">
              <p className="text-sm font-semibold text-foreground">Updates</p>
              <div className="divide-y rounded-md border bg-card/50">
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="space-y-1">
                    <Label>Current Version</Label>
                    <p className="text-xs text-muted-foreground">
                      The version installed on this device.
                    </p>
                  </div>
                  <div className="text-sm font-medium">
                    {isTauriApp ? currentVersion : 'Web preview'}
                  </div>
                </div>
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="space-y-1">
                    <Label>Last Checked</Label>
                    <p className="text-xs text-muted-foreground">
                      Most recent update check.
                    </p>
                  </div>
                  <div className="text-sm font-medium">
                    {isTauriApp
                      ? formatLastChecked(settings.updates.lastCheckedAt)
                      : 'Not available'}
                  </div>
                </div>
              </div>

              <div className="rounded-md border bg-card/50">
                <div className="flex flex-col gap-3 px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="space-y-1">
                    <Label>Automatic Updates</Label>
                    <p className="text-xs text-muted-foreground">
                      Check for updates on startup
                    </p>
                  </div>
                  <Switch
                    checked={settings.updates.autoCheck}
                    onCheckedChange={(checked) =>
                      updateSettings({
                        updates: { ...settings.updates, autoCheck: checked },
                      })
                    }
                  />
                </div>
              </div>
            </div>
          </TabsContent>
          </ScrollArea>
        </VerticalTabs>
      </DialogContent>
    </Dialog>
  );
}
