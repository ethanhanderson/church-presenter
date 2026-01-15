/**
 * AppMenubar - Top application menu bar
 */

import {
  Menubar,
  MenubarContent,
  MenubarItem,
  MenubarMenu,
  MenubarSeparator,
  MenubarShortcut,
  MenubarTrigger,
  MenubarSub,
  MenubarSubContent,
  MenubarSubTrigger,
  MenubarCheckboxItem,
} from '@/components/ui/menubar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useSettingsStore, useEditorStore } from '@/lib/stores';
import { Cloud, CloudOff, Loader2, AlertCircle, Check, Save } from 'lucide-react';
import { cn } from '@/lib/utils';

interface AppMenubarProps {
  onNewPresentation: () => void;
  onOpenPresentation: () => void;
  onSavePresentation: () => void;
  onSaveAs: () => void;
  onOpenSettings: () => void;
  onNewLibrary: () => void;
  onNewPlaylist: () => void;
  onUndo: () => void;
  onRedo: () => void;
  onSaveAllAndReport: () => void;
  onCheckForUpdates: () => void;
}

export function AppMenubar({
  onNewPresentation,
  onOpenPresentation,
  onSavePresentation,
  onSaveAs,
  onOpenSettings,
  onNewLibrary,
  onNewPlaylist,
  onUndo,
  onRedo,
  onSaveAllAndReport,
  onCheckForUpdates,
}: AppMenubarProps) {
  const { settings, setTheme } = useSettingsStore();
  const { presentation, isDirty, undoStack, redoStack, autoSave } = useEditorStore();

  return (
    <Menubar className="border-b border-border rounded-none px-2">
      {/* File Menu */}
      <MenubarMenu>
        <MenubarTrigger className="text-sm">File</MenubarTrigger>
        <MenubarContent>
          <MenubarItem onClick={onNewPresentation}>
            New Presentation
            <MenubarShortcut>Ctrl+N</MenubarShortcut>
          </MenubarItem>
          <MenubarItem onClick={onOpenPresentation}>
            Open...
            <MenubarShortcut>Ctrl+O</MenubarShortcut>
          </MenubarItem>
          <MenubarSeparator />
          <MenubarItem onClick={onSavePresentation} disabled={!presentation}>
            Save
            <MenubarShortcut>Ctrl+S</MenubarShortcut>
          </MenubarItem>
          <MenubarItem onClick={onSaveAs} disabled={!presentation}>
            Save As...
            <MenubarShortcut>Ctrl+Shift+S</MenubarShortcut>
          </MenubarItem>
          <MenubarSeparator />
          <MenubarSub>
            <MenubarSubTrigger>Recent Files</MenubarSubTrigger>
            <MenubarSubContent>
              {settings.recentFiles.length === 0 ? (
                <MenubarItem disabled>No recent files</MenubarItem>
              ) : (
                settings.recentFiles.map((file) => (
                  <MenubarItem key={file.path} onClick={() => {/* TODO: Open file */}}>
                    {file.title}
                  </MenubarItem>
                ))
              )}
            </MenubarSubContent>
          </MenubarSub>
          <MenubarSeparator />
          <MenubarItem onClick={onOpenSettings}>
            Settings...
            <MenubarShortcut>Ctrl+,</MenubarShortcut>
          </MenubarItem>
        </MenubarContent>
      </MenubarMenu>

      {/* Edit Menu */}
      <MenubarMenu>
        <MenubarTrigger className="text-sm">Edit</MenubarTrigger>
        <MenubarContent>
          <MenubarItem onClick={onUndo} disabled={undoStack.length === 0}>
            Undo
            <MenubarShortcut>Ctrl+Z</MenubarShortcut>
          </MenubarItem>
          <MenubarItem onClick={onRedo} disabled={redoStack.length === 0}>
            Redo
            <MenubarShortcut>Ctrl+Y</MenubarShortcut>
          </MenubarItem>
          <MenubarSeparator />
          <MenubarItem disabled>
            Cut
            <MenubarShortcut>Ctrl+X</MenubarShortcut>
          </MenubarItem>
          <MenubarItem disabled>
            Copy
            <MenubarShortcut>Ctrl+C</MenubarShortcut>
          </MenubarItem>
          <MenubarItem disabled>
            Paste
            <MenubarShortcut>Ctrl+V</MenubarShortcut>
          </MenubarItem>
          <MenubarSeparator />
          <MenubarItem disabled>
            Delete
            <MenubarShortcut>Del</MenubarShortcut>
          </MenubarItem>
          <MenubarItem disabled>
            Select All
            <MenubarShortcut>Ctrl+A</MenubarShortcut>
          </MenubarItem>
        </MenubarContent>
      </MenubarMenu>

      {/* View Menu */}
      <MenubarMenu>
        <MenubarTrigger className="text-sm">View</MenubarTrigger>
        <MenubarContent>
          <MenubarSub>
            <MenubarSubTrigger>Theme</MenubarSubTrigger>
            <MenubarSubContent>
              <MenubarCheckboxItem
                checked={settings.theme === 'light'}
                onClick={() => setTheme('light')}
              >
                Light
              </MenubarCheckboxItem>
              <MenubarCheckboxItem
                checked={settings.theme === 'dark'}
                onClick={() => setTheme('dark')}
              >
                Dark
              </MenubarCheckboxItem>
              <MenubarCheckboxItem
                checked={settings.theme === 'system'}
                onClick={() => setTheme('system')}
              >
                System
              </MenubarCheckboxItem>
            </MenubarSubContent>
          </MenubarSub>
          <MenubarSeparator />
          <MenubarItem disabled>
            Toggle Sidebar
            <MenubarShortcut>Ctrl+B</MenubarShortcut>
          </MenubarItem>
          <MenubarItem disabled>
            Toggle Preview
            <MenubarShortcut>Ctrl+P</MenubarShortcut>
          </MenubarItem>
        </MenubarContent>
      </MenubarMenu>

      {/* Library Menu */}
      <MenubarMenu>
        <MenubarTrigger className="text-sm">Library</MenubarTrigger>
        <MenubarContent>
          <MenubarItem onClick={onNewLibrary}>
            New Library
          </MenubarItem>
          <MenubarItem onClick={onNewPlaylist}>
            New Playlist
          </MenubarItem>
        </MenubarContent>
      </MenubarMenu>

      {/* Presentation Menu */}
      <MenubarMenu>
        <MenubarTrigger className="text-sm">Presentation</MenubarTrigger>
        <MenubarContent>
          <MenubarItem disabled={!presentation}>
            Add Slide
            <MenubarShortcut>Ctrl+Enter</MenubarShortcut>
          </MenubarItem>
          <MenubarItem disabled={!presentation}>
            Duplicate Slide
            <MenubarShortcut>Ctrl+D</MenubarShortcut>
          </MenubarItem>
          <MenubarSeparator />
          <MenubarSub>
            <MenubarSubTrigger disabled={!presentation}>Add Section</MenubarSubTrigger>
            <MenubarSubContent>
              <MenubarItem>Verse</MenubarItem>
              <MenubarItem>Chorus</MenubarItem>
              <MenubarItem>Bridge</MenubarItem>
              <MenubarItem>Pre-Chorus</MenubarItem>
              <MenubarItem>Tag</MenubarItem>
              <MenubarItem>Intro</MenubarItem>
              <MenubarItem>Outro</MenubarItem>
              <MenubarSeparator />
              <MenubarItem>Custom...</MenubarItem>
            </MenubarSubContent>
          </MenubarSub>
          <MenubarSeparator />
          <MenubarItem disabled={!presentation}>
            Manage Themes...
          </MenubarItem>
        </MenubarContent>
      </MenubarMenu>

      {/* Help Menu */}
      <MenubarMenu>
        <MenubarTrigger className="text-sm">Help</MenubarTrigger>
        <MenubarContent>
          <MenubarItem onClick={onCheckForUpdates}>
            Check for Updates...
          </MenubarItem>
          <MenubarSeparator />
          <MenubarItem>
            Keyboard Shortcuts
            <MenubarShortcut>Ctrl+/</MenubarShortcut>
          </MenubarItem>
          <MenubarSeparator />
          <MenubarItem>About Church Presenter</MenubarItem>
        </MenubarContent>
      </MenubarMenu>

      {/* Auto-save status indicator */}
      <div className="ml-auto flex items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          className="h-7 gap-1.5 text-xs"
          onClick={onSaveAllAndReport}
        >
          <Save className="h-3.5 w-3.5" />
          Save All
        </Button>
        {presentation && (
          <AutoSaveStatusIndicator
            status={autoSave.status}
            isDirty={isDirty}
            lastSaved={autoSave.lastSaved}
            error={autoSave.lastError}
          />
        )}
      </div>
    </Menubar>
  );
}

interface AutoSaveStatusIndicatorProps {
  status: 'idle' | 'pending' | 'saving' | 'saved' | 'error' | 'conflict';
  isDirty: boolean;
  lastSaved: string | null;
  error: string | null;
}

function AutoSaveStatusIndicator({ status, isDirty, lastSaved, error }: AutoSaveStatusIndicatorProps) {
  const formatLastSaved = (timestamp: string) => {
    const date = new Date(timestamp);
    const now = new Date();
    const diff = now.getTime() - date.getTime();
    
    if (diff < 60000) return 'just now';
    if (diff < 3600000) return `${Math.floor(diff / 60000)}m ago`;
    return date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
  };

  if (status === 'saving') {
    return (
      <Badge variant="secondary" className="gap-1.5 text-xs font-normal">
        <Loader2 className="h-3 w-3 animate-spin" />
        Saving...
      </Badge>
    );
  }

  if (status === 'error') {
    return (
      <Badge variant="destructive" className="gap-1.5 text-xs font-normal" title={error || 'Save failed'}>
        <AlertCircle className="h-3 w-3" />
        Save failed
      </Badge>
    );
  }

  if (status === 'conflict') {
    return (
      <Badge variant="destructive" className="gap-1.5 text-xs font-normal bg-amber-500 hover:bg-amber-600">
        <AlertCircle className="h-3 w-3" />
        Conflict detected
      </Badge>
    );
  }

  if (status === 'saved' && !isDirty) {
    return (
      <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
        <Check className="h-3 w-3 text-green-500" />
        <span>Saved {lastSaved ? formatLastSaved(lastSaved) : ''}</span>
      </div>
    );
  }

  if (isDirty) {
    return (
      <div className={cn(
        "flex items-center gap-1.5 text-xs",
        status === 'pending' ? "text-amber-600 dark:text-amber-500" : "text-muted-foreground"
      )}>
        {status === 'pending' ? (
          <Cloud className="h-3 w-3" />
        ) : (
          <CloudOff className="h-3 w-3" />
        )}
        <span>Unsaved changes</span>
      </div>
    );
  }

  return null;
}
