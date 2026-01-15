/**
 * AppSidebar - Left sidebar with libraries and playlists
 */

import { useEffect, useState } from 'react';
import {
  Library,
  ListMusic,
  Plus,
  FolderOpen,
  ChevronRight,
  ChevronLeft,
  MoreHorizontal,
  Pencil,
  Trash2,
  FileText,
  Cloud,
  CloudOff,
  AlertTriangle,
  RefreshCw,
  ExternalLink,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ScrollArea } from '@/components/ui/scroll-area';
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible';
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from '@/components/ui/context-menu';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { useCatalogStore } from '@/lib/stores';
import type { Library as LibraryType, Playlist, PresentationRef, ExternalSyncStatus } from '@/lib/models';
import { cn } from '@/lib/utils';
import { openUrl } from '@tauri-apps/plugin-opener';

interface AppSidebarProps {
  mode?: 'show' | 'edit';
  selectedLibraryId: string | null;
  selectedPlaylistId: string | null;
  selectedPresentationPath: string | null;
  onSelectLibrary: (id: string) => void;
  onSelectPlaylist: (id: string) => void;
  onSelectPresentation: (path: string, context: 'library' | 'playlist') => void;
  onNewLibrary: () => void;
  onNewPlaylist: () => void;
  onNewPresentation: () => void;
  onImportSongs?: () => void;
  onImportSets?: () => void;
  onEditLibrary: (library: LibraryType) => void;
  onEditPlaylist: (playlist: Playlist) => void;
  onDeleteLibrary: (library: LibraryType) => void;
  onDeletePlaylist: (playlist: Playlist) => void;
  onOpenPresentation: (path: string) => void;
  onRemoveFromLibrary: (libraryId: string, path: string) => void;
  onRemoveFromPlaylist: (playlistId: string, path: string) => void;
  collapsed: boolean;
  onToggleCollapse: () => void;
}

export function AppSidebar({
  mode = 'show',
  selectedLibraryId,
  selectedPlaylistId,
  selectedPresentationPath,
  onSelectLibrary,
  onSelectPlaylist,
  onSelectPresentation,
  onNewLibrary,
  onNewPlaylist,
  onNewPresentation,
  onImportSongs,
  onImportSets,
  onEditLibrary,
  onEditPlaylist,
  onDeleteLibrary,
  onDeletePlaylist,
  onOpenPresentation,
  onRemoveFromLibrary,
  onRemoveFromPlaylist,
  collapsed,
  onToggleCollapse,
}: AppSidebarProps) {
  const { catalog } = useCatalogStore();
  const [librariesOpen, setLibrariesOpen] = useState(true);
  const [playlistsOpen, setPlaylistsOpen] = useState(true);
  const [expandedLibraries, setExpandedLibraries] = useState<Set<string>>(new Set());
  const [expandedPlaylists, setExpandedPlaylists] = useState<Set<string>>(new Set());

  const toggleLibrary = (id: string) => {
    setExpandedLibraries((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  const togglePlaylist = (id: string) => {
    setExpandedPlaylists((prev) => {
      const next = new Set(prev);
      if (next.has(id)) {
        next.delete(id);
      } else {
        next.add(id);
      }
      return next;
    });
  };

  useEffect(() => {
    if (!selectedPresentationPath) return;

    const libraryMatch = catalog.libraries.find((library) =>
      library.presentations.some((presentation) => presentation.path === selectedPresentationPath)
    );
    if (libraryMatch) {
      setLibrariesOpen(true);
      setExpandedLibraries((prev) => {
        const next = new Set(prev);
        next.add(libraryMatch.id);
        return next;
      });
    }

    const playlistMatch = catalog.playlists.find((playlist) =>
      playlist.items.some((presentation) => presentation.path === selectedPresentationPath)
    );
    if (playlistMatch) {
      setPlaylistsOpen(true);
      setExpandedPlaylists((prev) => {
        const next = new Set(prev);
        next.add(playlistMatch.id);
        return next;
      });
    }
  }, [catalog.libraries, catalog.playlists, selectedPresentationPath]);

  if (collapsed) {
    return (
      <div className="flex h-full flex-col items-center bg-sidebar text-sidebar-foreground">
        <div className="p-1">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="h-7 w-7"
                onClick={onToggleCollapse}
              >
                <ChevronRight className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent side="right">
              <p>Expand Library</p>
            </TooltipContent>
          </Tooltip>
        </div>
      </div>
    );
  }

  const showPlaylists = mode === 'show';
  const showMusicManagerActions = mode === 'show' && (onImportSongs || onImportSets);

  return (
    <div className="flex h-full flex-col bg-sidebar text-sidebar-foreground">
      {/* Header */}
      <div className="flex h-14 items-center justify-between gap-2 border-b border-sidebar-border px-4 py-3">
        <h2 className="text-sm font-semibold">
          {showPlaylists ? 'Library' : 'Libraries'}
        </h2>
        <div className="flex items-center gap-1">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="h-7 w-7"
                onClick={onToggleCollapse}
              >
                <ChevronLeft className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent side="bottom">
              <p>Collapse Library</p>
            </TooltipContent>
          </Tooltip>
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" className="h-7 w-7">
                <Plus className="h-4 w-4" />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuItem onClick={onNewPresentation}>
                <FileText className="mr-2 h-4 w-4" />
                New Presentation
              </DropdownMenuItem>
              <DropdownMenuSeparator />
              <DropdownMenuItem onClick={onNewLibrary}>
                <Library className="mr-2 h-4 w-4" />
                New Library
              </DropdownMenuItem>
              {showPlaylists && (
                <DropdownMenuItem onClick={onNewPlaylist}>
                  <ListMusic className="mr-2 h-4 w-4" />
                  New Playlist
                </DropdownMenuItem>
              )}
            </DropdownMenuContent>
          </DropdownMenu>
        </div>
      </div>

      <ScrollArea className="flex-1">
        <div className="p-2">
          {showMusicManagerActions && (
            <div className="space-y-2 px-2 pb-2">
              <div className="text-[11px] font-medium text-muted-foreground">
                Music Manager
              </div>
              <div className="flex flex-col gap-2">
                {onImportSongs && (
                  <Button variant="outline" size="sm" onClick={onImportSongs}>
                    <Cloud className="mr-2 h-3 w-3" />
                    Import Songs
                  </Button>
                )}
                {onImportSets && (
                  <Button variant="outline" size="sm" onClick={onImportSets}>
                    <ListMusic className="mr-2 h-3 w-3" />
                    Import Set List
                  </Button>
                )}
              </div>
              <div className="border-b border-sidebar-border pt-1" />
            </div>
          )}
          {/* Libraries Section */}
          <Collapsible open={librariesOpen} onOpenChange={setLibrariesOpen}>
            <CollapsibleTrigger className="flex w-full items-center gap-1 rounded-md px-2 py-1.5 text-sm font-medium hover:bg-sidebar-accent">
              <ChevronRight
                className={cn(
                  'h-4 w-4 transition-transform',
                  librariesOpen && 'rotate-90'
                )}
              />
              <Library className="mr-1 h-4 w-4" />
              Libraries
              <span className="ml-auto text-xs text-muted-foreground">
                {catalog.libraries.length}
              </span>
            </CollapsibleTrigger>
            <CollapsibleContent className="pl-4">
              {catalog.libraries.length === 0 ? (
                <p className="px-2 py-4 text-xs text-muted-foreground">
                  No libraries yet
                </p>
              ) : (
                <div className="space-y-px">
                  {catalog.libraries.map((library) => (
                    <div key={library.id}>
                      <LibraryItem
                        library={library}
                        isSelected={selectedLibraryId === library.id}
                        isExpanded={expandedLibraries.has(library.id)}
                        selectedPresentationPath={selectedPresentationPath}
                        onSelect={() => onSelectLibrary(library.id)}
                        onToggle={() => toggleLibrary(library.id)}
                        onEdit={() => onEditLibrary(library)}
                        onDelete={() => onDeleteLibrary(library)}
                        onSelectPresentation={(path) => onSelectPresentation(path, 'library')}
                        onOpenPresentation={onOpenPresentation}
                        onRemovePresentation={(path) => onRemoveFromLibrary(library.id, path)}
                      />
                    </div>
                  ))}
                </div>
              )}
            </CollapsibleContent>
          </Collapsible>

          {/* Playlists Section */}
          {showPlaylists && (
            <Collapsible open={playlistsOpen} onOpenChange={setPlaylistsOpen} className="mt-2">
              <CollapsibleTrigger className="flex w-full items-center gap-1 rounded-md px-2 py-1.5 text-sm font-medium hover:bg-sidebar-accent">
                <ChevronRight
                  className={cn(
                    'h-4 w-4 transition-transform',
                    playlistsOpen && 'rotate-90'
                  )}
                />
                <ListMusic className="mr-1 h-4 w-4" />
                Playlists
                <span className="ml-auto text-xs text-muted-foreground">
                  {catalog.playlists.length}
                </span>
              </CollapsibleTrigger>
              <CollapsibleContent className="pl-4">
                {catalog.playlists.length === 0 ? (
                  <p className="px-2 py-4 text-xs text-muted-foreground">
                    No playlists yet
                  </p>
                ) : (
                  <div className="space-y-px">
                    {catalog.playlists.map((playlist) => (
                      <div key={playlist.id}>
                        <PlaylistItem
                          playlist={playlist}
                          isSelected={selectedPlaylistId === playlist.id}
                          isExpanded={expandedPlaylists.has(playlist.id)}
                          selectedPresentationPath={selectedPresentationPath}
                          onSelect={() => onSelectPlaylist(playlist.id)}
                          onToggle={() => togglePlaylist(playlist.id)}
                          onEdit={() => onEditPlaylist(playlist)}
                          onDelete={() => onDeletePlaylist(playlist)}
                          onSelectPresentation={(path) => onSelectPresentation(path, 'playlist')}
                          onOpenPresentation={onOpenPresentation}
                          onRemovePresentation={(path) => onRemoveFromPlaylist(playlist.id, path)}
                        />
                      </div>
                    ))}
                  </div>
                )}
              </CollapsibleContent>
            </Collapsible>
          )}
        </div>
      </ScrollArea>
    </div>
  );
}

interface LibraryItemProps {
  library: LibraryType;
  isSelected: boolean;
  isExpanded: boolean;
  selectedPresentationPath: string | null;
  onSelect: () => void;
  onToggle: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onSelectPresentation: (path: string) => void;
  onOpenPresentation: (path: string) => void;
  onRemovePresentation: (path: string) => void;
}

function LibraryItem({
  library,
  isSelected,
  isExpanded,
  selectedPresentationPath,
  onSelect,
  onToggle,
  onEdit,
  onDelete,
  onSelectPresentation,
  onOpenPresentation,
  onRemovePresentation,
}: LibraryItemProps) {
  const hasSelectedPresentation = !!selectedPresentationPath
    && library.presentations.some((presentation) => presentation.path === selectedPresentationPath);

  return (
    <ContextMenu>
      <ContextMenuTrigger>
        <Collapsible open={isExpanded} onOpenChange={onToggle}>
          <div
            className={cn(
              'flex items-center rounded-md hover:bg-sidebar-accent',
              isSelected && !hasSelectedPresentation && 'bg-sidebar-accent'
            )}
          >
            <CollapsibleTrigger className="p-1">
              <ChevronRight
                className={cn(
                  'h-3 w-3 transition-transform',
                  isExpanded && 'rotate-90'
                )}
              />
            </CollapsibleTrigger>
            <button
              className="flex flex-1 items-center gap-2 py-1.5 pr-2 text-sm"
              onClick={onSelect}
            >
              <FolderOpen className="h-4 w-4 text-muted-foreground" />
              <span className="truncate">{library.name}</span>
              <span className="ml-auto text-xs text-muted-foreground">
                {library.presentations.length}
              </span>
            </button>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="ghost" size="icon" className="h-6 w-6 opacity-0 group-hover:opacity-100">
                  <MoreHorizontal className="h-3 w-3" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={onEdit}>
                  <Pencil className="mr-2 h-4 w-4" />
                  Rename
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={onDelete} className="text-destructive">
                  <Trash2 className="mr-2 h-4 w-4" />
                  Delete
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
          <CollapsibleContent className="pl-6">
            {library.presentations.map((pres) => (
              <div key={pres.path}>
                <PresentationItem
                  presentation={pres}
                  isSelected={selectedPresentationPath === pres.path}
                  onSelect={() => onSelectPresentation(pres.path)}
                  onOpen={() => onOpenPresentation(pres.path)}
                  onRemove={() => onRemovePresentation(pres.path)}
                />
              </div>
            ))}
          </CollapsibleContent>
        </Collapsible>
      </ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onClick={onEdit}>
          <Pencil className="mr-2 h-4 w-4" />
          Rename
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem onClick={onDelete} className="text-destructive">
          <Trash2 className="mr-2 h-4 w-4" />
          Delete
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  );
}

interface PlaylistItemProps {
  playlist: Playlist;
  isSelected: boolean;
  isExpanded: boolean;
  selectedPresentationPath: string | null;
  onSelect: () => void;
  onToggle: () => void;
  onEdit: () => void;
  onDelete: () => void;
  onSelectPresentation: (path: string) => void;
  onOpenPresentation: (path: string) => void;
  onRemovePresentation: (path: string) => void;
}

function PlaylistItem({
  playlist,
  isSelected,
  isExpanded,
  selectedPresentationPath,
  onSelect,
  onToggle,
  onEdit,
  onDelete,
  onSelectPresentation,
  onOpenPresentation,
  onRemovePresentation,
}: PlaylistItemProps) {
  const isLinked = !!playlist.externalSet;
  const syncStatus = playlist.sync?.status;
  const conflictUrl = playlist.sync?.conflictUrl;
  const hasSelectedPresentation = !!selectedPresentationPath
    && playlist.items.some((presentation) => presentation.path === selectedPresentationPath);

  const handleOpenConflict = async () => {
    if (conflictUrl) {
      try {
        await openUrl(conflictUrl);
      } catch (error) {
        console.error('Failed to open conflict URL:', error);
      }
    }
  };

  return (
    <ContextMenu>
      <ContextMenuTrigger>
        <Collapsible open={isExpanded} onOpenChange={onToggle}>
          <div
            className={cn(
              'flex items-center rounded-md hover:bg-sidebar-accent',
              isSelected && !hasSelectedPresentation && 'bg-sidebar-accent'
            )}
          >
            <CollapsibleTrigger className="p-1">
              <ChevronRight
                className={cn(
                  'h-3 w-3 transition-transform',
                  isExpanded && 'rotate-90'
                )}
              />
            </CollapsibleTrigger>
            <button
              className="flex flex-1 items-center gap-2 py-1.5 pr-2 text-sm"
              onClick={onSelect}
            >
              <ListMusic className="h-4 w-4 text-muted-foreground" />
              <span className="truncate">{playlist.name}</span>
              {isLinked && (
                <SyncStatusBadge status={syncStatus} />
              )}
              <span className="ml-auto text-xs text-muted-foreground">
                {playlist.items.length}
              </span>
            </button>
          </div>
          <CollapsibleContent className="pl-6">
            {playlist.items.map((pres, index) => (
              <div key={`${pres.path}-${index}`}>
                <PresentationItem
                  presentation={pres}
                  isSelected={selectedPresentationPath === pres.path}
                  onSelect={() => onSelectPresentation(pres.path)}
                  onOpen={() => onOpenPresentation(pres.path)}
                  onRemove={() => onRemovePresentation(pres.path)}
                />
              </div>
            ))}
          </CollapsibleContent>
        </Collapsible>
      </ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onClick={onEdit}>
          <Pencil className="mr-2 h-4 w-4" />
          Rename
        </ContextMenuItem>
        {isLinked && syncStatus === 'conflict' && conflictUrl && (
          <>
            <ContextMenuSeparator />
            <ContextMenuItem onClick={handleOpenConflict}>
              <ExternalLink className="mr-2 h-4 w-4" />
              Open in Music Manager
            </ContextMenuItem>
          </>
        )}
        <ContextMenuSeparator />
        <ContextMenuItem onClick={onDelete} className="text-destructive">
          <Trash2 className="mr-2 h-4 w-4" />
          Delete
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  );
}

// Sync status badge component
interface SyncStatusBadgeProps {
  status?: ExternalSyncStatus;
}

function SyncStatusBadge({ status }: SyncStatusBadgeProps) {
  if (!status) return null;

  const config: Record<ExternalSyncStatus, { icon: typeof Cloud; className: string; label: string }> = {
    linked: {
      icon: Cloud,
      className: 'text-blue-500',
      label: 'Linked to Music Manager',
    },
    synced: {
      icon: Cloud,
      className: 'text-green-500',
      label: 'Synced',
    },
    pending: {
      icon: RefreshCw,
      className: 'text-yellow-500',
      label: 'Sync pending',
    },
    conflict: {
      icon: AlertTriangle,
      className: 'text-orange-500',
      label: 'Sync conflict - click to resolve',
    },
    error: {
      icon: CloudOff,
      className: 'text-red-500',
      label: 'Sync error',
    },
  };

  const { icon: Icon, className, label } = config[status];

  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <span className={cn('shrink-0', className)}>
          <Icon className="h-3 w-3" />
        </span>
      </TooltipTrigger>
      <TooltipContent side="right">
        <p>{label}</p>
      </TooltipContent>
    </Tooltip>
  );
}

interface PresentationItemProps {
  presentation: PresentationRef;
  isSelected: boolean;
  onSelect: () => void;
  onOpen: () => void;
  onRemove: () => void;
}

function PresentationItem({
  presentation,
  isSelected,
  onSelect,
  onOpen,
  onRemove,
}: PresentationItemProps) {
  return (
    <ContextMenu>
      <ContextMenuTrigger>
        <button
          className={cn(
            'flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-sidebar-accent',
            isSelected && 'bg-sidebar-accent'
          )}
          onClick={onSelect}
          onDoubleClick={onOpen}
        >
          <FileText className="h-4 w-4 text-muted-foreground" />
          <span className="truncate">{presentation.title}</span>
        </button>
      </ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onClick={onOpen}>
          <FolderOpen className="mr-2 h-4 w-4" />
          Open
        </ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem onClick={onRemove} className="text-destructive">
          <Trash2 className="mr-2 h-4 w-4" />
          Remove
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  );
}
