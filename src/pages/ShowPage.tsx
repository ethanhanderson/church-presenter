/**
 * ShowPage - presentation playback and playlist view
 */

import { useEffect, useMemo, useState, useCallback } from 'react';
import { usePanelRef, type PanelSize } from 'react-resizable-panels';
import {
  ResizableHandle,
  ResizablePanel,
  ResizablePanelGroup,
} from '@/components/ui/resizable';
import { AppSidebar } from '@/components/layout/AppSidebar';
import { RightPanel } from '@/components/layout/RightPanel';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Button } from '@/components/ui/button';
import { SlideThumbnail } from '@/components/preview';
import {
  useCatalogStore,
  useEditorStore,
  useLiveStore,
  useSettingsStore,
  useShowStore,
} from '@/lib/stores';
import type { Library, Playlist } from '@/lib/models';
import { getSlideGroupKey, getSlideGroupLabel } from '@/lib/models';

interface ShowPageProps {
  selectedLibraryId: string | null;
  selectedPlaylistId: string | null;
  selectedPresentationPath: string | null;
  onSelectLibrary: (id: string) => void;
  onSelectPlaylist: (id: string) => void;
  onSelectPresentation: (path: string, context: 'library' | 'playlist') => void;
  onNewLibrary: () => void;
  onNewPlaylist: () => void;
  onNewPresentation: () => void;
  onEditLibrary: (library: Library) => void;
  onEditPlaylist: (playlist: Playlist) => void;
  onDeleteLibrary: (library: Library) => void;
  onDeletePlaylist: (playlist: Playlist) => void;
  onOpenPresentation: (path: string) => void;
  onRemoveFromLibrary: (libraryId: string, path: string) => void;
  onRemoveFromPlaylist: (playlistId: string, path: string) => void;
  onImportSongs: () => void;
  onImportSets: () => void;
  onOpenOutputSettings: () => void;
}

export function ShowPage({
  selectedLibraryId,
  selectedPlaylistId,
  selectedPresentationPath,
  onSelectLibrary,
  onSelectPlaylist,
  onSelectPresentation,
  onNewLibrary,
  onNewPlaylist,
  onNewPresentation,
  onEditLibrary,
  onEditPlaylist,
  onDeleteLibrary,
  onDeletePlaylist,
  onOpenPresentation,
  onRemoveFromLibrary,
  onRemoveFromPlaylist,
  onImportSongs,
  onImportSets,
  onOpenOutputSettings,
}: ShowPageProps) {
  const leftSidebarRef = usePanelRef();
  const rightSidebarRef = usePanelRef();
  const [isLeftSidebarCollapsed, setIsLeftSidebarCollapsed] = useState(false);
  const [isRightSidebarCollapsed, setIsRightSidebarCollapsed] = useState(false);

  const LEFT_COLLAPSED_SIZE = 3;
  const RIGHT_COLLAPSED_SIZE = 3;

  const handleToggleLeftSidebar = () => {
    const panel = leftSidebarRef.current;
    if (!panel) return;
    if (panel.isCollapsed()) {
      panel.expand();
      setIsLeftSidebarCollapsed(false);
    } else {
      panel.collapse();
      setIsLeftSidebarCollapsed(true);
    }
  };

  const handleToggleRightSidebar = () => {
    const panel = rightSidebarRef.current;
    if (!panel) return;
    if (panel.isCollapsed()) {
      panel.expand();
      setIsRightSidebarCollapsed(false);
    } else {
      panel.collapse();
      setIsRightSidebarCollapsed(true);
    }
  };

  const handleLeftPanelResize = useCallback((size: PanelSize) => {
    // Detect collapse when panel is dragged to collapsed size
    setIsLeftSidebarCollapsed(size.asPercentage <= LEFT_COLLAPSED_SIZE + 0.5);
  }, []);

  const handleRightPanelResize = useCallback((size: PanelSize) => {
    // Detect collapse when panel is dragged to collapsed size
    setIsRightSidebarCollapsed(size.asPercentage <= RIGHT_COLLAPSED_SIZE + 0.5);
  }, []);

  return (
    <ResizablePanelGroup direction="horizontal" className="flex-1">
      <ResizablePanel
        defaultSize={18}
        minSize={15}
        maxSize={35}
        collapsible
        collapsedSize={LEFT_COLLAPSED_SIZE}
        panelRef={leftSidebarRef}
        onResize={handleLeftPanelResize}
      >
        <AppSidebar
          mode="show"
          selectedLibraryId={selectedLibraryId}
          selectedPlaylistId={selectedPlaylistId}
          selectedPresentationPath={selectedPresentationPath}
          onSelectLibrary={onSelectLibrary}
          onSelectPlaylist={onSelectPlaylist}
          onSelectPresentation={onSelectPresentation}
          onNewLibrary={onNewLibrary}
          onNewPlaylist={onNewPlaylist}
          onNewPresentation={onNewPresentation}
          onEditLibrary={onEditLibrary}
          onEditPlaylist={onEditPlaylist}
          onDeleteLibrary={onDeleteLibrary}
          onDeletePlaylist={onDeletePlaylist}
          onOpenPresentation={onOpenPresentation}
          onRemoveFromLibrary={onRemoveFromLibrary}
          onRemoveFromPlaylist={onRemoveFromPlaylist}
          collapsed={isLeftSidebarCollapsed}
          onToggleCollapse={handleToggleLeftSidebar}
          onImportSongs={onImportSongs}
          onImportSets={onImportSets}
        />
      </ResizablePanel>

      <ResizableHandle />

      <ResizablePanel defaultSize={58} minSize={35}>
        <ShowCenter
          selectedLibraryId={selectedLibraryId}
          selectedPlaylistId={selectedPlaylistId}
          onNewPresentation={onNewPresentation}
          onOpenPresentation={onOpenPresentation}
        />
      </ResizablePanel>

      <ResizableHandle />

      <ResizablePanel
        defaultSize={24}
        minSize={22}
        maxSize={40}
        collapsible
        collapsedSize={RIGHT_COLLAPSED_SIZE}
        panelRef={rightSidebarRef}
        onResize={handleRightPanelResize}
      >
        <RightPanel
          onOpenOutputSettings={onOpenOutputSettings}
          collapsed={isRightSidebarCollapsed}
          onToggleCollapse={handleToggleRightSidebar}
        />
      </ResizablePanel>
    </ResizablePanelGroup>
  );
}

interface ShowCenterProps {
  selectedLibraryId: string | null;
  selectedPlaylistId: string | null;
  onNewPresentation: () => void;
  onOpenPresentation: (path: string) => void;
}

function ShowCenter({
  selectedLibraryId,
  selectedPlaylistId,
  onNewPresentation,
  onOpenPresentation,
}: ShowCenterProps) {
  const { catalog } = useCatalogStore();
  const { presentation } = useEditorStore();
  const { isLive, currentSlideId, goToSlide } = useLiveStore();
  const { settings } = useSettingsStore();
  const { selectedSlideId, setSelectedSlideId, clearSelectedSlide } = useShowStore();

  const selectedLibrary = selectedLibraryId
    ? catalog.libraries.find((l) => l.id === selectedLibraryId)
    : null;
  const selectedPlaylist = selectedPlaylistId
    ? catalog.playlists.find((p) => p.id === selectedPlaylistId)
    : null;
  const fallbackLibrary =
    !selectedLibrary && settings.show.defaultCenterView === 'library'
      ? catalog.libraries[0] || null
      : null;
  const fallbackPlaylist =
    !selectedPlaylist && settings.show.defaultCenterView === 'playlist'
      ? catalog.playlists[0] || null
      : null;

  const currentTheme = useMemo(() => {
    if (!presentation) return null;
    return (
      presentation.themes.find((t) => t.id === presentation.manifest.themeId) || null
    );
  }, [presentation]);
  const groupLabelById = useMemo(() => {
    if (!presentation) return new Map<string, string>();
    const seenGroups = new Set<string>();
    const labels = new Map<string, string>();
    for (const slide of presentation.slides) {
      const key = getSlideGroupKey(slide);
      const label = getSlideGroupLabel(slide);
      if (!key || !label || seenGroups.has(key)) continue;
      seenGroups.add(key);
      labels.set(slide.id, label);
    }
    return labels;
  }, [presentation]);

  useEffect(() => {
    if (!presentation) {
      if (selectedSlideId) clearSelectedSlide();
      return;
    }

    const hasSelected =
      selectedSlideId && presentation.slides.some((slide) => slide.id === selectedSlideId);
    if (!hasSelected) {
      const fallback = currentSlideId || presentation.slides[0]?.id || null;
      setSelectedSlideId(fallback);
    }
  }, [
    presentation,
    selectedSlideId,
    currentSlideId,
    setSelectedSlideId,
    clearSelectedSlide,
  ]);

  useEffect(() => {
    if (!isLive || !currentSlideId || currentSlideId === selectedSlideId) return;
    setSelectedSlideId(currentSlideId);
  }, [isLive, currentSlideId, selectedSlideId, setSelectedSlideId]);

  if (presentation) {
    return (
      <div className="flex h-full flex-col">
      <div className="flex h-14 items-center justify-between border-b px-4 py-3">
          <div>
            <div className="text-sm font-semibold">{presentation.manifest.title}</div>
            <div className="text-xs text-muted-foreground">
              {presentation.slides.length} slides
            </div>
          </div>
          <div className="text-xs text-muted-foreground">
            {isLive ? 'Live control enabled' : 'Preview mode'}
          </div>
        </div>
        <ScrollArea className="flex-1 p-4">
          <div
            className="grid gap-3 overflow-visible"
            style={{
              gridTemplateColumns: `repeat(auto-fill, minmax(${settings.show.thumbnailSize}px, 1fr))`,
            }}
          >
            {presentation.slides.map((slide, index) => (
              <div key={slide.id} className="p-1 overflow-visible">
                <SlideThumbnail
                  slide={slide}
                  theme={currentTheme}
                  isSelected={selectedSlideId === slide.id}
                  isActive={isLive && currentSlideId === slide.id}
                  showLabel={settings.show.showSlideLabels}
                  slideNumber={index + 1}
                  groupLabel={groupLabelById.get(slide.id)}
                  onClick={() => {
                    setSelectedSlideId(slide.id);
                    if (isLive) {
                      goToSlide(slide.id);
                    }
                  }}
                  onDoubleClick={() => {
                    if (settings.show.autoTakeOnDoubleClick) {
                      goToSlide(slide.id);
                    }
                  }}
                />
              </div>
            ))}
          </div>
        </ScrollArea>
      </div>
    );
  }

  const playlistToShow = selectedPlaylist || fallbackPlaylist;
  if (playlistToShow) {
    return (
      <PlaylistView
        playlist={playlistToShow}
        onNewPresentation={onNewPresentation}
        onOpenPresentation={onOpenPresentation}
      />
    );
  }

  const libraryToShow = selectedLibrary || fallbackLibrary;
  if (libraryToShow) {
    return (
      <LibraryView
        library={libraryToShow}
        onNewPresentation={onNewPresentation}
        onOpenPresentation={onOpenPresentation}
      />
    );
  }

  return <ShowHomeView onNewPresentation={onNewPresentation} />;
}

interface ShowHomeViewProps {
  onNewPresentation: () => void;
}

function ShowHomeView({ onNewPresentation }: ShowHomeViewProps) {
  return (
    <div className="flex h-full items-center justify-center p-8">
      <div className="text-center max-w-md space-y-4">
        <div className="space-y-2">
          <h2 className="text-xl font-semibold">Show Mode</h2>
          <p className="text-muted-foreground text-sm">
            Select a library or playlist to start presenting.
          </p>
        </div>
        <Button onClick={onNewPresentation}>New Presentation</Button>
      </div>
    </div>
  );
}

interface LibraryViewProps {
  library: Library;
  onNewPresentation: () => void;
  onOpenPresentation: (path: string) => void;
}

function LibraryView({ library, onNewPresentation, onOpenPresentation }: LibraryViewProps) {
  return (
    <div className="flex h-full flex-col">
      <div className="flex h-14 items-center justify-between border-b px-4 py-3">
        <div>
          <h2 className="text-lg font-semibold">{library.name}</h2>
          <p className="text-sm text-muted-foreground">
            {library.presentations.length} presentation
            {library.presentations.length !== 1 ? 's' : ''}
          </p>
        </div>
        <Button onClick={onNewPresentation}>New Presentation</Button>
      </div>
      <ScrollArea className="flex-1 p-4">
        {library.presentations.length === 0 ? (
          <div className="text-sm text-muted-foreground">No presentations yet.</div>
        ) : (
          <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-4">
            {library.presentations.map((pres) => (
              <button
                key={pres.path}
                className="rounded-md border bg-card p-3 text-left transition-colors hover:bg-accent"
                onClick={() => onOpenPresentation(pres.path)}
              >
                <div className="text-sm font-medium truncate">{pres.title}</div>
                <div className="text-xs text-muted-foreground truncate">
                  Updated {new Date(pres.updatedAt).toLocaleDateString()}
                </div>
              </button>
            ))}
          </div>
        )}
      </ScrollArea>
    </div>
  );
}

interface PlaylistViewProps {
  playlist: Playlist;
  onNewPresentation: () => void;
  onOpenPresentation: (path: string) => void;
}

function PlaylistView({ playlist, onNewPresentation, onOpenPresentation }: PlaylistViewProps) {
  return (
    <div className="flex h-full flex-col">
      <div className="flex h-14 items-center justify-between border-b px-4 py-3">
        <div>
          <h2 className="text-lg font-semibold">{playlist.name}</h2>
          <p className="text-sm text-muted-foreground">
            {playlist.items.length} item{playlist.items.length !== 1 ? 's' : ''}
          </p>
        </div>
        <Button onClick={onNewPresentation}>Add Presentation</Button>
      </div>
      <ScrollArea className="flex-1 p-4">
        {playlist.items.length === 0 ? (
          <div className="text-sm text-muted-foreground">Playlist is empty.</div>
        ) : (
          <div className="space-y-2">
            {playlist.items.map((item, index) => (
              <button
                key={`${item.path}-${index}`}
                className="flex w-full items-center gap-3 rounded-md border bg-card px-3 py-2 text-left transition-colors hover:bg-accent"
                onClick={() => onOpenPresentation(item.path)}
              >
                <span className="flex h-7 w-7 items-center justify-center rounded-full bg-muted text-xs font-medium">
                  {index + 1}
                </span>
                <span className="flex-1 truncate text-sm font-medium">
                  {item.title}
                </span>
              </button>
            ))}
          </div>
        )}
      </ScrollArea>
    </div>
  );
}
