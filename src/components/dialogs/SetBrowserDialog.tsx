/**
 * SetBrowserDialog - Dialog for browsing and importing sets (set lists) from Music Manager
 */

import { useMemo, useState, useEffect, useCallback } from 'react';
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
import {
  ListMusic,
  Calendar,
  Search,
  RefreshCw,
  AlertCircle,
  CloudOff,
} from 'lucide-react';
import { useMusicManagerStore } from '@/lib/stores';
import type { Set as MusicSet, SetWithSongs } from '@/lib/supabase';
import { cn } from '@/lib/utils';

interface SetBrowserDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onImportSets: (sets: SetWithSongs[]) => void;
}

export function SetBrowserDialog({
  open,
  onOpenChange,
  onImportSets,
}: SetBrowserDialogProps) {
  const {
    isConnected,
    connectionError,
    groups,
    sets,
    currentGroup,
    isLoadingGroups,
    isLoadingSets,
    testConnection,
    loadGroups,
    selectGroup,
    loadSet,
  } = useMusicManagerStore();

  const [selectedSetIds, setSelectedSetIds] = useState<Set<string>>(new Set());
  const [focusedSetId, setFocusedSetId] = useState<string | null>(null);
  const [searchInput, setSearchInput] = useState('');
  const [isImporting, setIsImporting] = useState(false);
  const [importError, setImportError] = useState<string | null>(null);

  // Initialize connection when dialog opens
  useEffect(() => {
    if (open && !isConnected && groups.length === 0) {
      loadGroups();
    }
  }, [open, isConnected, groups.length, loadGroups]);

  // Auto-select first group if none selected
  useEffect(() => {
    if (open && isConnected && groups.length > 0 && !currentGroup) {
      selectGroup(groups[0].id);
    }
  }, [open, isConnected, groups, currentGroup, selectGroup]);

  // Reset state when dialog closes
  useEffect(() => {
    if (!open) {
      setSelectedSetIds(new Set());
      setFocusedSetId(null);
      setSearchInput('');
      setIsImporting(false);
      setImportError(null);
    }
  }, [open]);

  // Load full set details for the currently focused set (preview)
  useEffect(() => {
    if (focusedSetId) {
      loadSet(focusedSetId);
    }
  }, [focusedSetId, loadSet]);

  const handleGroupChange = useCallback((groupId: string) => {
    selectGroup(groupId);
    setSelectedSetIds(new Set());
    setFocusedSetId(null);
    setSearchInput('');
    setImportError(null);
  }, [selectGroup]);

  const toggleSet = useCallback((setId: string) => {
    setSelectedSetIds((prev) => {
      const next = new Set(prev);
      if (next.has(setId)) next.delete(setId);
      else next.add(setId);
      return next;
    });
    setFocusedSetId(setId);
  }, []);

  const clearSelection = useCallback(() => setSelectedSetIds(new Set()), []);

  const handleRetry = useCallback(() => {
    testConnection();
  }, [testConnection]);

  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleDateString(undefined, {
      weekday: 'short',
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  };

  const filteredSets = useMemo(() => {
    const q = searchInput.trim().toLowerCase();
    if (!q) return sets;
    return sets.filter((s) => {
      const title = (s.notes || '').toLowerCase();
      const date = formatDate(s.service_date).toLowerCase();
      return title.includes(q) || date.includes(q);
    });
  }, [sets, searchInput]);

  const visibleSelectedCount = useMemo(() => {
    return filteredSets.reduce((count, s) => (selectedSetIds.has(s.id) ? count + 1 : count), 0);
  }, [filteredSets, selectedSetIds]);

  const allVisibleSelected = filteredSets.length > 0 && visibleSelectedCount === filteredSets.length;
  const someVisibleSelected = visibleSelectedCount > 0 && !allVisibleSelected;

  const toggleSelectAllVisible = useCallback(() => {
    setSelectedSetIds((prev) => {
      const next = new Set(prev);
      if (allVisibleSelected) {
        for (const s of filteredSets) next.delete(s.id);
      } else {
        for (const s of filteredSets) next.add(s.id);
      }
      return next;
    });
  }, [allVisibleSelected, filteredSets]);

  const handleConfirm = useCallback(async () => {
    setImportError(null);
    const ids = [...selectedSetIds];
    if (ids.length === 0) return;

    setIsImporting(true);
    try {
      const results: SetWithSongs[] = [];
      for (const id of ids) {
        const setData = await loadSet(id);
        if (setData) results.push(setData);
      }

      if (results.length === 0) {
        setImportError('Could not load any selected set lists. Please try again.');
        return;
      }

      onImportSets(results);
      onOpenChange(false);
    } catch (e) {
      setImportError(e instanceof Error ? e.message : 'Failed to import set lists');
    } finally {
      setIsImporting(false);
    }
  }, [selectedSetIds, loadSet, onImportSets, onOpenChange]);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="flex flex-col overflow-hidden">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <ListMusic className="h-5 w-5" />
            Import Set List from Music Manager
          </DialogTitle>
          <DialogDescription>
            Select one or more set lists to create linked playlists
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
            {/* Group Selector */}
            <div className="flex gap-3">
              <div className="flex-1">
                <Label htmlFor="set-search" className="sr-only">Search set lists</Label>
                <div className="relative">
                  <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                  <Input
                    id="set-search"
                    placeholder="Search by notes or date..."
                    value={searchInput}
                    onChange={(e) => setSearchInput(e.target.value)}
                    className="pl-9"
                  />
                </div>
              </div>
              <div className="w-56">
                <Label htmlFor="group" className="sr-only">Music Group</Label>
                <Select
                  value={currentGroup?.id || ''}
                  onValueChange={handleGroupChange}
                >
                  <SelectTrigger id="group">
                    <SelectValue placeholder="Select a group" />
                  </SelectTrigger>
                  <SelectContent>
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
            {filteredSets.length > 0 && (
              <div className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-2 min-w-0">
                  <Checkbox
                    checked={allVisibleSelected ? true : someVisibleSelected ? 'indeterminate' : false}
                    onCheckedChange={toggleSelectAllVisible}
                    aria-label={allVisibleSelected ? 'Unselect all visible set lists' : 'Select all visible set lists'}
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
                    {selectedSetIds.size} selected
                  </span>
                </div>
                {selectedSetIds.size > 0 && (
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
            )}

            {/* Sets List */}
            <ScrollArea className="h-[38vh] w-full border rounded-md">
              {isLoadingSets ? (
                <div className="p-4 space-y-3">
                  {[...Array(4)].map((_, i) => (
                    <Skeleton key={i} className="h-16 w-full" />
                  ))}
                </div>
              ) : filteredSets.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-12 text-muted-foreground">
                  <AlertCircle className="h-8 w-8 mb-2" />
                  <p>
                    {searchInput.trim()
                      ? 'No set lists match your search'
                      : 'No set lists available for this group'}
                  </p>
                </div>
              ) : (
                <div className="divide-y">
                  {filteredSets.map((set) => (
                    <SetItem
                      key={set.id}
                      set={set}
                      isSelected={selectedSetIds.has(set.id)}
                      isFocused={focusedSetId === set.id}
                      onToggle={() => toggleSet(set.id)}
                      onDoubleClick={() => {
                        // Quick import: replace selection with this set and import
                        setSelectedSetIds(new Set([set.id]));
                        setFocusedSetId(set.id);
                        // Defer so state updates apply before confirm
                        queueMicrotask(() => {
                          handleConfirm();
                        });
                      }}
                      formatDate={formatDate}
                    />
                  ))}
                </div>
              )}
            </ScrollArea>

            {importError && (
              <p className="text-sm text-destructive">
                {importError}
              </p>
            )}
          </div>
        )}

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={handleConfirm}
            disabled={selectedSetIds.size === 0 || isImporting || !isConnected}
          >
            {isImporting
              ? 'Importing...'
              : selectedSetIds.size <= 1
                ? 'Import Set List'
                : `Import ${selectedSetIds.size} Set Lists`}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

interface SetItemProps {
  set: MusicSet;
  isSelected: boolean;
  isFocused: boolean;
  onToggle: () => void;
  onDoubleClick: () => void;
  formatDate: (date: string) => string;
}

function SetItem({ set, isSelected, isFocused, onToggle, onDoubleClick, formatDate }: SetItemProps) {
  return (
    <button
      className={cn(
        'w-full flex items-center gap-3 p-3 text-left text-sm transition-colors',
        'hover:bg-accent',
        isSelected && 'bg-accent',
        isFocused && 'ring-1 ring-inset ring-primary/20'
      )}
      onClick={onToggle}
      onDoubleClick={onDoubleClick}
    >
      <Calendar className="h-4 w-4 text-muted-foreground shrink-0" />
      <div className="flex-1 min-w-0">
        <p className="font-medium truncate">
          {set.notes?.trim() || formatDate(set.service_date)}
        </p>
        <p className="text-xs text-muted-foreground flex items-center gap-1">
          <Calendar className="h-3 w-3" />
          {formatDate(set.service_date)}
        </p>
      </div>
      <Checkbox
        checked={isSelected}
        onCheckedChange={onToggle}
        onClick={(e) => e.stopPropagation()}
        aria-label={isSelected ? 'Unselect set list' : 'Select set list'}
      />
    </button>
  );
}
