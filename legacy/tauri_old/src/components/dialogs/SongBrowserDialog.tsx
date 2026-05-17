/**
 * SongBrowserDialog - Dialog for browsing and importing songs from Music Manager
 */

import { useMemo, useState, useEffect, useCallback, useRef } from 'react';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Checkbox } from '@/components/ui/checkbox';
import { Separator } from '@/components/ui/separator';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import {
  Search,
  Music,
  RefreshCw,
  AlertCircle,
  CloudOff,
} from 'lucide-react';
import { useMusicManagerStore } from '@/lib/stores';
import type { Song } from '@/lib/supabase';
import { cn } from '@/lib/utils';

interface SongBrowserDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onImportSongs: (songs: Song[]) => void;
  onLinkSong?: (song: Song) => void;
  mode?: 'import' | 'link';
}

export function SongBrowserDialog({
  open,
  onOpenChange,
  onImportSongs,
  onLinkSong,
  mode = 'import',
}: SongBrowserDialogProps) {
  const {
    isConnected,
    connectionError,
    groups,
    songs,
    currentGroup,
    isLoadingGroups,
    isLoadingSongs,
    songSearchResults,
    isSearching,
    testConnection,
    loadGroups,
    selectGroup,
    searchSongs,
    clearSearch,
  } = useMusicManagerStore();

  const [selectedSongIds, setSelectedSongIds] = useState<Set<string>>(new Set());
  const [searchInput, setSearchInput] = useState('');
  const isLinkMode = mode === 'link';
  const hasLoadedAllRef = useRef(false);

  // Initialize connection when dialog opens
  useEffect(() => {
    if (open && !isConnected && groups.length === 0) {
      loadGroups();
    }
  }, [open, isConnected, groups.length, loadGroups]);

  // Load all songs once groups are ready
  useEffect(() => {
    if (!open) {
      hasLoadedAllRef.current = false;
      return;
    }
    if (!isConnected || groups.length === 0) return;
    if (currentGroup || songs.length > 0 || isLoadingSongs) return;
    if (hasLoadedAllRef.current) return;

    hasLoadedAllRef.current = true;
    selectGroup(null);
  }, [open, isConnected, groups.length, currentGroup, songs.length, isLoadingSongs, selectGroup]);

  // Debounced search
  useEffect(() => {
    const timer = setTimeout(() => {
      if (searchInput.trim()) {
        searchSongs(searchInput);
      } else {
        clearSearch();
      }
    }, 300);

    return () => clearTimeout(timer);
  }, [searchInput, searchSongs, clearSearch]);

  // Reset state when dialog closes
  useEffect(() => {
    if (!open) {
      setSelectedSongIds(new Set());
      setSearchInput('');
      clearSearch();
    }
  }, [open, clearSearch]);

  const handleGroupChange = useCallback((groupId: string) => {
    selectGroup(groupId === 'all' ? null : groupId);
    setSelectedSongIds(new Set());
    setSearchInput('');
    clearSearch();
  }, [selectGroup, clearSearch]);

  const toggleSong = useCallback((songId: string) => {
    setSelectedSongIds((prev) => {
      const next = isLinkMode ? new Set<string>() : new Set(prev);
      if (next.has(songId)) next.delete(songId);
      else next.add(songId);
      return next;
    });
  }, [isLinkMode]);

  const clearSelection = useCallback(() => setSelectedSongIds(new Set()), []);

  const handleRetry = useCallback(() => {
    testConnection();
  }, [testConnection]);

  // Determine which songs to display
  const displaySongs = searchInput.trim() ? songSearchResults : songs;

  const songById = useMemo(() => {
    const map = new Map<string, Song>();
    for (const s of songs) map.set(s.id, s);
    for (const s of songSearchResults) map.set(s.id, s);
    return map;
  }, [songs, songSearchResults]);

  const selectedSongs = useMemo(() => {
    // Keep a stable import order based on the visible list first.
    const orderedVisible = displaySongs.filter((s) => selectedSongIds.has(s.id));
    const visibleIds = new Set(orderedVisible.map((s) => s.id));
    const remaining = [...selectedSongIds]
      .filter((id) => !visibleIds.has(id))
      .map((id) => songById.get(id))
      .filter(Boolean) as Song[];
    return [...orderedVisible, ...remaining];
  }, [displaySongs, selectedSongIds, songById]);

  const visibleSelectedCount = useMemo(() => {
    return displaySongs.reduce((count, s) => (selectedSongIds.has(s.id) ? count + 1 : count), 0);
  }, [displaySongs, selectedSongIds]);

  const allVisibleSelected = displaySongs.length > 0 && visibleSelectedCount === displaySongs.length;
  const someVisibleSelected = visibleSelectedCount > 0 && !allVisibleSelected;

  const selectAllVisible = useCallback(() => {
    setSelectedSongIds((prev) => {
      const next = new Set(prev);
      for (const s of displaySongs) next.add(s.id);
      return next;
    });
  }, [displaySongs]);

  const toggleSelectAllVisible = useCallback(() => {
    setSelectedSongIds((prev) => {
      const next = new Set(prev);
      if (allVisibleSelected) {
        for (const s of displaySongs) next.delete(s.id);
      } else {
        for (const s of displaySongs) next.add(s.id);
      }
      return next;
    });
  }, [allVisibleSelected, displaySongs]);

  const handleConfirm = useCallback(() => {
    if (selectedSongs.length === 0) return;

    if (isLinkMode) {
      onLinkSong?.(selectedSongs[0]!);
    } else {
      onImportSongs(selectedSongs);
    }
    onOpenChange(false);
  }, [selectedSongs, isLinkMode, onLinkSong, onImportSongs, onOpenChange]);

  const handleConfirmSingle = useCallback((song: Song) => {
    if (isLinkMode) {
      onLinkSong?.(song);
    } else {
      onImportSongs([song]);
    }
    onOpenChange(false);
  }, [isLinkMode, onLinkSong, onImportSongs, onOpenChange]);

  const titleText = isLinkMode ? 'Link Song to Music Manager' : 'Import Song from Music Manager';
  const descriptionText = isLinkMode
    ? 'Choose a song to link this presentation'
    : 'Search, select one or more songs, then import';
  const confirmText = isLinkMode
    ? 'Link Song'
    : selectedSongIds.size <= 1 ? 'Import Song' : `Import ${selectedSongIds.size} Songs`;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Music className="h-5 w-5" />
            {titleText}
          </DialogTitle>
          <DialogDescription>
            {descriptionText}
          </DialogDescription>
        </DialogHeader>

        {/* Connection Error State */}
        {connectionError && !isConnected && (
          <div className="flex flex-col items-center justify-center py-8 gap-4">
            <CloudOff className="h-12 w-12 text-muted-foreground" />
            <div className="text-center space-y-1">
              <p className="font-medium text-destructive">Connection Failed</p>
              <p className="text-sm text-muted-foreground">{connectionError}</p>
            </div>
            <Button variant="outline" onClick={handleRetry}>
              <RefreshCw className="mr-2 h-4 w-4" />
              Retry
            </Button>
          </div>
        )}

        {/* Loading Groups State */}
        {isLoadingGroups && !connectionError && (
          <div className="flex flex-col items-center justify-center py-8 gap-4">
            <RefreshCw className="h-8 w-8 animate-spin text-muted-foreground" />
            <p className="text-sm text-muted-foreground">Connecting to Music Manager...</p>
          </div>
        )}

        {/* Connected State */}
        {isConnected && !isLoadingGroups && (
          <div className="flex flex-col gap-4 flex-1 min-h-0">
            {/* Filters Row */}
            <div className="flex gap-3">
              <div className="flex-1">
                <Label htmlFor="search" className="sr-only">Search songs</Label>
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                  <Input
                    id="search"
                    placeholder="Search songs by title..."
                    value={searchInput}
                    onChange={(e) => setSearchInput(e.target.value)}
                    className="pl-9"
                    autoFocus
                  />
                </div>
              </div>
              <div className="w-48">
                <Label htmlFor="group" className="sr-only">Music Group</Label>
                <Select
                  value={currentGroup?.id || 'all'}
                  onValueChange={handleGroupChange}
                >
                  <SelectTrigger id="group">
                    <SelectValue placeholder="All Groups" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="all">All Groups</SelectItem>
                    {groups.map((group) => (
                      <SelectItem key={group.id} value={group.id}>
                        {group.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
            </div>

            {/* Selection toolbar */}
            {!isLinkMode && displaySongs.length > 0 && (
              <div className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-2 min-w-0">
                  <Checkbox
                    checked={allVisibleSelected ? true : someVisibleSelected ? 'indeterminate' : false}
                    onCheckedChange={toggleSelectAllVisible}
                    aria-label={allVisibleSelected ? 'Unselect all visible songs' : 'Select all visible songs'}
                  />
                  <button
                    type="button"
                    onClick={toggleSelectAllVisible}
                    className="text-sm font-medium cursor-pointer underline-offset-4"
                  >
                    {allVisibleSelected ? 'Unselect all' : 'Select all'}
                  </button>
                  <Separator orientation="vertical" className="h-4 mx-1" />
                  <span className="text-sm text-muted-foreground truncate">
                    {selectedSongIds.size} selected
                  </span>
                  {searchInput.trim() && (
                    <Badge variant="secondary" className="shrink-0">
                      filtered
                    </Badge>
                  )}
                </div>
                <div className="flex items-center gap-2 shrink-0">
                  <Button
                    type="button"
                    variant="outline"
                    size="sm"
                    onClick={selectAllVisible}
                    disabled={allVisibleSelected}
                  >
                    Add all
                  </Button>
                  {selectedSongIds.size > 0 && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="sm"
                      onClick={clearSelection}
                    >
                      Clear
                    </Button>
                  )}
                </div>
              </div>
            )}

            {/* Songs List */}
            <ScrollArea className="h-[45vh] w-full border rounded-md">
              {isLoadingSongs || isSearching ? (
                <div className="p-4 space-y-3">
                  {[...Array(5)].map((_, i) => (
                    <Skeleton key={i} className="h-14 w-full" />
                  ))}
                </div>
              ) : displaySongs.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-12 text-muted-foreground">
                  <AlertCircle className="h-8 w-8 mb-2" />
                  <p>
                    {searchInput.trim()
                      ? 'No songs found matching your search'
                      : 'No songs available'}
                  </p>
                </div>
              ) : (
                <div className="divide-y">
                  {displaySongs.map((song) => (
                    <SongItem
                      key={song.id}
                      song={song}
                      isSelected={selectedSongIds.has(song.id)}
                      onToggle={() => toggleSong(song.id)}
                      onDoubleClick={() => handleConfirmSingle(song)}
                      groupName={groups.find((g) => g.id === song.group_id)?.name}
                      showGroupBadge={!currentGroup}
                    />
                  ))}
                </div>
              )}
            </ScrollArea>

          </div>
        )}

        <DialogFooter className="bg-background z-10">
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={handleConfirm}
            disabled={selectedSongIds.size === 0 || !isConnected}
          >
            {confirmText}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

interface SongItemProps {
  song: Song;
  isSelected: boolean;
  onToggle: () => void;
  onDoubleClick: () => void;
  groupName?: string;
  showGroupBadge?: boolean;
}

function SongItem({
  song,
  isSelected,
  onToggle,
  onDoubleClick,
  groupName,
  showGroupBadge,
}: SongItemProps) {
  const handleKeyDown = (event: React.KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      onToggle();
    }
  };

  return (
    <div
      className={cn(
        'w-full flex items-center gap-3 p-3 text-left text-sm transition-colors',
        'hover:bg-accent',
        isSelected && 'bg-accent'
      )}
      onClick={onToggle}
      onDoubleClick={onDoubleClick}
      onKeyDown={handleKeyDown}
      role="button"
      tabIndex={0}
    >
      <Music className="h-4 w-4 text-muted-foreground shrink-0" />
      <div className="flex-1 min-w-0">
        <p className="font-medium truncate">{song.title}</p>
        {showGroupBadge && groupName && (
          <Badge variant="secondary" className="mt-1 w-fit">
            {groupName}
          </Badge>
        )}
      </div>
      <Checkbox
        checked={isSelected}
        onCheckedChange={onToggle}
        onClick={(e) => e.stopPropagation()}
        aria-label={isSelected ? 'Unselect song' : 'Select song'}
      />
    </div>
  );
}
