/**
 * ReflowPage - slide text overview and previews
 */

import { useCallback, useEffect, useMemo, useRef, useState, type MouseEvent } from 'react';
import type { ImperativePanelHandle } from 'react-resizable-panels';
import {
  ResizableHandle,
  ResizablePanel,
  ResizablePanelGroup,
} from '@/components/ui/resizable';
import { AppSidebar } from '@/components/layout/AppSidebar';
import { ScrollArea } from '@/components/ui/scroll-area';
import { SlideThumbnail } from '@/components/preview';
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuSub,
  ContextMenuSubContent,
  ContextMenuSubTrigger,
  ContextMenuTrigger,
} from '@/components/ui/context-menu';
import { useEditorStore, useSettingsStore } from '@/lib/stores';
import type { Library, Slide, SongSection } from '@/lib/models';
import { SONG_SECTIONS, formatSectionLabel, getSlideGroupKey, getSlideGroupLabel } from '@/lib/models';
import { cn } from '@/lib/utils';

interface ReflowPageProps {
  selectedLibraryId: string | null;
  selectedPresentationPath: string | null;
  onSelectLibrary: (id: string) => void;
  onSelectPresentation: (path: string, context: 'library' | 'playlist') => void;
  onNewLibrary: () => void;
  onNewPresentation: () => void;
  onEditLibrary: (library: Library) => void;
  onDeleteLibrary: (library: Library) => void;
  onOpenPresentation: (path: string) => void;
  onRemoveFromLibrary: (libraryId: string, path: string) => void;
}

export function ReflowPage({
  selectedLibraryId,
  selectedPresentationPath,
  onSelectLibrary,
  onSelectPresentation,
  onNewLibrary,
  onNewPresentation,
  onEditLibrary,
  onDeleteLibrary,
  onOpenPresentation,
  onRemoveFromLibrary,
}: ReflowPageProps) {
  const leftSidebarRef = useRef<ImperativePanelHandle>(null);
  const [isLeftSidebarCollapsed, setIsLeftSidebarCollapsed] = useState(false);
  const { presentation, setSlideSection, updateSlide, applyTheme } = useEditorStore();
  const { settings } = useSettingsStore();
  const [selectedSlideIds, setSelectedSlideIds] = useState<string[]>([]);
  const [anchorSlideId, setAnchorSlideId] = useState<string | null>(null);

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

  const handleToggleLeftSidebar = () => {
    const panel = leftSidebarRef.current;
    if (!panel) return;
    if (panel.isCollapsed()) {
      panel.expand();
    } else {
      panel.collapse();
    }
  };

  const densityClass =
    settings.reflow.previewDensity === 'compact' ? 'gap-2' : 'gap-4';

  useEffect(() => {
    setSelectedSlideIds([]);
    setAnchorSlideId(null);
  }, [presentation?.manifest.presentationId]);

  const ensureSelection = useCallback(
    (slideId: string) => {
      if (selectedSlideIds.includes(slideId)) return;
      setSelectedSlideIds([slideId]);
      setAnchorSlideId(slideId);
    },
    [selectedSlideIds]
  );

  const handleSelectSlide = useCallback(
    (slideId: string, index: number, event: MouseEvent) => {
      if (!presentation) return;

      if (event.shiftKey && anchorSlideId) {
        const anchorIndex = presentation.slides.findIndex((slide) => slide.id === anchorSlideId);
        if (anchorIndex !== -1) {
          const [start, end] = anchorIndex < index ? [anchorIndex, index] : [index, anchorIndex];
          const rangeIds = presentation.slides.slice(start, end + 1).map((slide) => slide.id);
          setSelectedSlideIds(rangeIds);
          return;
        }
      }

      if (event.metaKey || event.ctrlKey) {
        setSelectedSlideIds((prev) =>
          prev.includes(slideId) ? prev.filter((id) => id !== slideId) : [...prev, slideId]
        );
        setAnchorSlideId(slideId);
        return;
      }

      setSelectedSlideIds([slideId]);
      setAnchorSlideId(slideId);
    },
    [anchorSlideId, presentation]
  );

  const handleSetSection = useCallback(
    (section: SongSection | null) => {
      if (!presentation || selectedSlideIds.length === 0) return;

      if (!section) {
        selectedSlideIds.forEach((slideId) => {
          updateSlide(slideId, { section: undefined, sectionLabel: undefined, sectionIndex: undefined });
        });
        return;
      }

      selectedSlideIds.forEach((slideId) => {
        setSlideSection(slideId, section);
      });
    },
    [presentation, selectedSlideIds, setSlideSection, updateSlide]
  );

  const handleApplyTheme = useCallback(
    (themeId: string) => {
      if (!presentation) return;
      applyTheme(themeId);
    },
    [presentation, applyTheme]
  );

  return (
    <ResizablePanelGroup direction="horizontal" className="flex-1">
      <ResizablePanel
        defaultSize={18}
        minSize={15}
        maxSize={35}
        collapsible
        collapsedSize={3}
        ref={leftSidebarRef}
        onCollapse={() => setIsLeftSidebarCollapsed(true)}
        onExpand={() => setIsLeftSidebarCollapsed(false)}
      >
        <AppSidebar
          mode="edit"
          selectedLibraryId={selectedLibraryId}
          selectedPlaylistId={null}
          selectedPresentationPath={selectedPresentationPath}
          onSelectLibrary={onSelectLibrary}
          onSelectPlaylist={() => undefined}
          onSelectPresentation={onSelectPresentation}
          onNewLibrary={onNewLibrary}
          onNewPlaylist={() => undefined}
          onNewPresentation={onNewPresentation}
          onEditLibrary={onEditLibrary}
          onEditPlaylist={() => undefined}
          onDeleteLibrary={onDeleteLibrary}
          onDeletePlaylist={() => undefined}
          onOpenPresentation={onOpenPresentation}
          onRemoveFromLibrary={onRemoveFromLibrary}
          onRemoveFromPlaylist={() => undefined}
          collapsed={isLeftSidebarCollapsed}
          onToggleCollapse={handleToggleLeftSidebar}
        />
      </ResizablePanel>

      <ResizableHandle />

      <ResizablePanel defaultSize={82} minSize={50}>
        {presentation ? (
          <div className="flex h-full">
            <div className="w-80 shrink-0 border-r">
              <ScrollArea className="h-full">
                <div className="space-y-2 p-3">
                  {presentation.slides.map((slide, index) => {
                    const text = getSlideText(slide);
                    const isSelected = selectedSlideIds.includes(slide.id);
                    return (
                      <ContextMenu key={slide.id}>
                        <ContextMenuTrigger asChild>
                          <button
                            className={cn(
                              'w-full rounded-md border p-2 text-left transition-colors hover:bg-accent',
                              isSelected && 'border-primary bg-accent'
                            )}
                            onClick={(event) => handleSelectSlide(slide.id, index, event)}
                            onContextMenu={() => ensureSelection(slide.id)}
                            style={{ fontSize: settings.reflow.textSize }}
                          >
                            <div className="text-[11px] text-muted-foreground">
                              Slide {index + 1}
                            </div>
                            <div className="whitespace-pre-wrap text-sm">
                              {text || 'No text'}
                            </div>
                          </button>
                        </ContextMenuTrigger>
                        <ContextMenuContent>
                          <ContextMenuSub>
                            <ContextMenuSubTrigger>Set Section</ContextMenuSubTrigger>
                            <ContextMenuSubContent>
                              {SONG_SECTIONS.map((section) => (
                                <ContextMenuItem
                                  key={section}
                                  onClick={() => handleSetSection(section)}
                                >
                                  {formatSectionLabel(section)}
                                </ContextMenuItem>
                              ))}
                              <ContextMenuSeparator />
                              <ContextMenuItem onClick={() => handleSetSection(null)}>
                                Clear Section
                              </ContextMenuItem>
                            </ContextMenuSubContent>
                          </ContextMenuSub>
                          <ContextMenuSub>
                            <ContextMenuSubTrigger>Apply Theme</ContextMenuSubTrigger>
                            <ContextMenuSubContent>
                              {presentation.themes.map((theme) => (
                                <ContextMenuItem
                                  key={theme.id}
                                  onClick={() => handleApplyTheme(theme.id)}
                                >
                                  {theme.name}
                                </ContextMenuItem>
                              ))}
                            </ContextMenuSubContent>
                          </ContextMenuSub>
                        </ContextMenuContent>
                      </ContextMenu>
                    );
                  })}
                </div>
              </ScrollArea>
            </div>
            <div className="flex-1 overflow-hidden">
              <ScrollArea className="h-full">
                <div className={cn('grid grid-cols-2 p-4', densityClass)}>
                  {presentation.slides.map((slide, index) => {
                    const isSelected = selectedSlideIds.includes(slide.id);
                    return (
                      <ContextMenu key={slide.id}>
                        <ContextMenuTrigger asChild>
                          <div
                            onClick={(event) => handleSelectSlide(slide.id, index, event)}
                            onContextMenu={() => ensureSelection(slide.id)}
                          >
                            <SlideThumbnail
                              slide={slide}
                              theme={currentTheme}
                              isSelected={isSelected}
                              showLabel={settings.reflow.showSlideLabels}
                              slideNumber={index + 1}
                              groupLabel={groupLabelById.get(slide.id)}
                            />
                          </div>
                        </ContextMenuTrigger>
                        <ContextMenuContent>
                          <ContextMenuSub>
                            <ContextMenuSubTrigger>Set Section</ContextMenuSubTrigger>
                            <ContextMenuSubContent>
                              {SONG_SECTIONS.map((section) => (
                                <ContextMenuItem
                                  key={section}
                                  onClick={() => handleSetSection(section)}
                                >
                                  {formatSectionLabel(section)}
                                </ContextMenuItem>
                              ))}
                              <ContextMenuSeparator />
                              <ContextMenuItem onClick={() => handleSetSection(null)}>
                                Clear Section
                              </ContextMenuItem>
                            </ContextMenuSubContent>
                          </ContextMenuSub>
                          <ContextMenuSub>
                            <ContextMenuSubTrigger>Apply Theme</ContextMenuSubTrigger>
                            <ContextMenuSubContent>
                              {presentation.themes.map((theme) => (
                                <ContextMenuItem
                                  key={theme.id}
                                  onClick={() => handleApplyTheme(theme.id)}
                                >
                                  {theme.name}
                                </ContextMenuItem>
                              ))}
                            </ContextMenuSubContent>
                          </ContextMenuSub>
                        </ContextMenuContent>
                      </ContextMenu>
                    );
                  })}
                </div>
              </ScrollArea>
            </div>
          </div>
        ) : (
          <div className="flex h-full items-center justify-center p-8">
            <div className="text-center max-w-md space-y-3">
              <h2 className="text-xl font-semibold">Reflow Mode</h2>
              <p className="text-muted-foreground text-sm">
                Open a presentation to see its text flow and slide previews.
              </p>
            </div>
          </div>
        )}
      </ResizablePanel>
    </ResizablePanelGroup>
  );
}

function getSlideText(slide: Slide): string {
  const textLayers = slide.layers.filter((layer) => layer.type === 'text');
  if (textLayers.length === 0) return '';
  return textLayers
    .map((layer) => ('content' in layer ? String(layer.content).trim() : ''))
    .filter(Boolean)
    .join('\n');
}
