/**
 * PresentationEditor - Main editor for creating/editing presentations
 */

import { useEffect, useMemo, useState, useCallback, type MouseEvent, Profiler } from 'react';
import {
  closestCenter,
  DndContext,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { restrictToVerticalAxis } from '@dnd-kit/modifiers';
import {
  Plus,
  GripVertical,
  MoreHorizontal,
  Trash2,
  Copy,
  ChevronDown,
  Book,
  Users,
  Link2,
  Link2Off,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { ScrollArea } from '@/components/ui/scroll-area';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
  ContextMenuSub,
  ContextMenuSubContent,
  ContextMenuSubTrigger,
} from '@/components/ui/context-menu';
import { useCatalogStore, useEditorStore, useLiveStore, useMusicManagerStore, useThemeStore } from '@/lib/stores';
import { useCursorHover } from '@/components/cursor';
import { SlideThumbnail } from '@/components/preview/SlideRenderer';
import { SlideCanvas } from './SlideCanvas';
import { SlideInspector } from './SlideInspector';
import { SONG_SECTIONS, formatSectionLabel, getSlideGroupLabel } from '@/lib/models';
import type { Presentation, SongSection, Slide, ThemeTemplate, ThemeTemplateSlide } from '@/lib/models';
import { cn } from '@/lib/utils';
import { loadBundledFonts } from '@/lib/services/fontService';

interface PresentationEditorProps {
  onRequestLink?: () => void;
}

export function PresentationEditor({ onRequestLink }: PresentationEditorProps) {
  const {
    presentation,
    activeSlideId,
    selection,
    selectSlide,
    selectSlides,
    addSlide,
    duplicateSlide,
    duplicateSlides,
    deleteSlide,
    deleteSlides,
    moveSlide,
    setSlideSection,
    setSlidesSection,
    updateTitle,
    filePath,
    pendingMedia,
    unlinkExternalSong,
    setPresentationAspectRatio,
    applyThemeSlideToSlides,
  } = useEditorStore();

  const { isLive, currentSlideId, goToSlide } = useLiveStore();
  const { catalog } = useCatalogStore();
  const { groups, loadGroups, isLoadingGroups } = useMusicManagerStore();
  const { themes, loadThemes } = useThemeStore();

  const [isEditingTitle, setIsEditingTitle] = useState(false);
  const [lastSelectedSlideId, setLastSelectedSlideId] = useState<string | null>(null);
  const [rangeAnchorSlideId, setRangeAnchorSlideId] = useState<string | null>(null);
  const [themeMismatchDialogOpen, setThemeMismatchDialogOpen] = useState(false);
  const [pendingThemeApply, setPendingThemeApply] = useState<{
    theme: ThemeTemplate;
    themeSlide: ThemeTemplateSlide;
    slideIds: string[];
  } | null>(null);

  if (!presentation) return null;

  const activeSlide = presentation.slides.find((s) => s.id === activeSlideId);
  const aspectRatio = presentation.manifest.aspectRatio;
  const library = filePath
    ? catalog.libraries.find((l) =>
        l.presentations.some((p) => p.path === filePath)
      )
    : null;
  const linkedGroupName = presentation.manifest.externalSong
    ? groups.find((group) => group.id === presentation.manifest.externalSong?.groupId)?.name ||
      'Unknown group'
    : null;

  useEffect(() => {
    if (!presentation.manifest.externalSong) return;
    if (groups.length > 0 || isLoadingGroups) return;
    loadGroups();
  }, [presentation.manifest.externalSong, groups.length, isLoadingGroups, loadGroups]);

  useEffect(() => {
    void loadThemes();
  }, [loadThemes]);

  useEffect(() => {
    void loadBundledFonts(presentation, filePath);
  }, [presentation, filePath]);

  // Group slides by section
  const slidesBySection = useMemo(
    () => groupSlidesBySection(presentation.slides),
    [presentation.slides]
  );
  const slideNumberById = useMemo(
    () => new Map(presentation.slides.map((slide, index) => [slide.id, index + 1])),
    [presentation.slides]
  );
  const slideIds = useMemo(() => presentation.slides.map((slide) => slide.id), [
    presentation.slides,
  ]);

  const handleAddSlide = (section?: SongSection) => {
    addSlide('song', {
      section,
      afterSlideId: activeSlideId || undefined,
    });
  };

  const handleTakeSlide = (slideId: string) => {
    if (isLive) {
      goToSlide(slideId);
    }
  };

  const getSlideRangeSelection = useCallback(
    (startId: string, endId: string) => {
      const startIndex = slideIds.indexOf(startId);
      const endIndex = slideIds.indexOf(endId);
      if (startIndex === -1 || endIndex === -1) return [endId];
      const [from, to] =
        startIndex <= endIndex ? [startIndex, endIndex] : [endIndex, startIndex];
      return slideIds.slice(from, to + 1);
    },
    [slideIds]
  );

  const handleSelectSlide = useCallback(
    (slideId: string, event: MouseEvent) => {
      const isToggle = event.metaKey || event.ctrlKey;
      const isRange = event.shiftKey;
      const rangeAnchor =
        rangeAnchorSlideId ?? lastSelectedSlideId ?? activeSlideId ?? selection.slideIds[0] ?? null;

      if (isRange && rangeAnchor) {
        const range = getSlideRangeSelection(rangeAnchor, slideId);
        const nextSelection = Array.from(
          new Set([...selection.slideIds, ...range])
        );
        selectSlides(nextSelection, slideId);
      } else if (isToggle) {
        const isAlreadySelected = selection.slideIds.includes(slideId);
        if (isAlreadySelected) {
          const nextSelection = selection.slideIds.filter((id) => id !== slideId);
          if (nextSelection.length === 0) {
            selectSlide(null);
            setLastSelectedSlideId(null);
            setRangeAnchorSlideId(null);
            return;
          }
          const nextActive =
            activeSlideId && nextSelection.includes(activeSlideId)
              ? activeSlideId
              : nextSelection[0];
          selectSlides(nextSelection, nextActive);
          setLastSelectedSlideId(nextActive || null);
          return;
        }
        const nextSelection = [slideId, ...selection.slideIds];
        selectSlides(nextSelection, slideId);
        setRangeAnchorSlideId(rangeAnchorSlideId ?? slideId);
      } else {
        selectSlide(slideId);
        setRangeAnchorSlideId(slideId);
      }

      setLastSelectedSlideId(slideId);
    },
    [
      activeSlideId,
      getSlideRangeSelection,
      lastSelectedSlideId,
      rangeAnchorSlideId,
      selectSlide,
      selectSlides,
      selection.slideIds,
    ]
  );

  const handleContextSelectSlide = useCallback(
    (slideId: string) => {
      if (!selection.slideIds.includes(slideId)) {
        selectSlide(slideId);
      }
      setLastSelectedSlideId(slideId);
    },
    [selectSlide, selection.slideIds]
  );

  const getContextSlideSelection = useCallback(
    (slideId: string) =>
      selection.slideIds.includes(slideId) ? selection.slideIds : [slideId],
    [selection.slideIds]
  );

  const handleApplyTheme = useCallback(
    (theme: ThemeTemplate, themeSlide: ThemeTemplateSlide, slideIds: string[]) => {
      if (!presentation) return;
      const presentationAspect = presentation.manifest.aspectRatio ?? '16:9';
      if (theme.aspectRatio !== presentationAspect) {
        setPendingThemeApply({ theme, themeSlide, slideIds });
        setThemeMismatchDialogOpen(true);
        return;
      }
      applyThemeSlideToSlides(slideIds, theme, themeSlide);
    },
    [presentation, applyThemeSlideToSlides]
  );

  const resolveThemeMismatch = useCallback(
    (choice: 'match' | 'fit' | 'cancel') => {
      if (!pendingThemeApply) {
        setThemeMismatchDialogOpen(false);
        return;
      }
      const { theme, themeSlide, slideIds } = pendingThemeApply;
      if (choice === 'cancel') {
        setThemeMismatchDialogOpen(false);
        setPendingThemeApply(null);
        return;
      }
      if (choice === 'match') {
        setPresentationAspectRatio(theme.aspectRatio);
        applyThemeSlideToSlides(slideIds, theme, themeSlide);
      } else {
        applyThemeSlideToSlides(slideIds, theme, themeSlide, { scaleMode: 'fit' });
      }
      setThemeMismatchDialogOpen(false);
      setPendingThemeApply(null);
    },
    [pendingThemeApply, applyThemeSlideToSlides, setPresentationAspectRatio]
  );

  const externalSong = presentation.manifest.externalSong;
  const isLinked = !!externalSong;

  const handleLinkToggle = () => {
    if (isLinked) {
      unlinkExternalSong();
      return;
    }
    onRequestLink?.();
  };
  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );
  const handleDragEnd = useCallback(
    ({ active, over }: DragEndEvent) => {
      if (!over || active.id === over.id) return;
      const activeSection = active.data.current?.section ?? null;
      const overSection = over.data.current?.section ?? null;
      if (activeSection !== overSection) return;
      const fromIndex = presentation.slides.findIndex((slide) => slide.id === active.id);
      const toIndex = presentation.slides.findIndex((slide) => slide.id === over.id);
      if (fromIndex === -1 || toIndex === -1) return;
      moveSlide(String(active.id), toIndex);
    },
    [presentation.slides, moveSlide]
  );

  return (
    <Profiler
      id="PresentationEditor"
      onRender={(id, phase, actualDuration) => {
        // #region agent log
        fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({sessionId:'debug-session',runId:'pre-fix',hypothesisId:'I',location:'PresentationEditor.tsx:Profiler',message:'render_profile',data:{id,phase,actualDurationMs:Math.round(actualDuration)},timestamp:Date.now()})}).catch(()=>{});
        // #endregion
      }}
    >
      <div className="flex h-full flex-col">
      {/* Editor Header */}
      <div className="flex items-center justify-between border-b px-4 py-2">
        <div className="flex items-center gap-2">
          {isEditingTitle ? (
            <Input
              value={presentation.manifest.title}
              onChange={(e) => updateTitle(e.target.value)}
              onBlur={() => setIsEditingTitle(false)}
              onKeyDown={(e) => e.key === 'Enter' && setIsEditingTitle(false)}
              className="h-8 w-64"
              autoFocus
            />
          ) : (
            <button
              className="text-lg font-semibold hover:text-primary text-left"
              onClick={() => setIsEditingTitle(true)}
            >
              {presentation.manifest.title}
            </button>
          )}
          {(library || linkedGroupName) && (
            <div className="flex items-center gap-3 text-xs text-muted-foreground">
              {library && (
                <div className="flex items-center gap-1">
                  <Book className="h-3.5 w-3.5" />
                  <span className="max-w-48 truncate">{library.name}</span>
                </div>
              )}
              {linkedGroupName && (
                <div className="flex items-center gap-1">
                  <Users className="h-3.5 w-3.5" />
                  <span className="max-w-48 truncate">{linkedGroupName}</span>
                </div>
              )}
            </div>
          )}
        </div>

        <div className="flex items-center gap-2">
          <Button
            variant={isLinked ? 'outline' : 'secondary'}
            size="sm"
            onClick={handleLinkToggle}
            disabled={!isLinked && !onRequestLink}
          >
            {isLinked ? (
              <Link2Off className="mr-1 h-4 w-4" />
            ) : (
              <Link2 className="mr-1 h-4 w-4" />
            )}
            {isLinked ? 'Unlink Music Manager' : 'Link to Music Manager'}
          </Button>
        </div>
      </div>

      {/* Editor Content */}
      <div className="flex flex-1 min-h-0 overflow-hidden">
        {/* Slide List (Left) */}
        <div className="w-56 shrink-0 border-r bg-muted/30 flex min-h-0 flex-col">
          <div className="border-b p-2">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="sm" className="w-full justify-between">
                  <span className="flex items-center">
                    <Plus className="mr-1 h-4 w-4" />
                    Add Slide
                  </span>
                  <ChevronDown className="h-3 w-3" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="start">
                <DropdownMenuItem onClick={() => handleAddSlide()}>
                  Blank Slide
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuSub>
                  <DropdownMenuSubTrigger>With Section</DropdownMenuSubTrigger>
                  <DropdownMenuSubContent>
                    {SONG_SECTIONS.map((section) => (
                      <DropdownMenuItem
                        key={section}
                        onClick={() => handleAddSlide(section)}
                      >
                        {formatSectionLabel(section)}
                      </DropdownMenuItem>
                    ))}
                  </DropdownMenuSubContent>
                </DropdownMenuSub>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
          <ScrollArea className="flex-1 min-h-0">
            <DndContext
              sensors={sensors}
              collisionDetection={closestCenter}
              onDragEnd={handleDragEnd}
              modifiers={[restrictToVerticalAxis]}
            >
              <SortableContext items={slideIds} strategy={verticalListSortingStrategy}>
                <div className="p-2 space-y-4">
                  {slidesBySection.map((group) => (
                    <div key={group.section || 'unsorted'}>
                      {group.section && (
                        <div className="flex items-center gap-2 px-2 py-1 text-xs font-medium text-muted-foreground">
                          <span>{group.label}</span>
                          <span className="text-muted-foreground/50">
                            ({group.slides.length})
                          </span>
                        </div>
                      )}
                      <div className="space-y-1">
                        {group.slides.map((slide, index) => (
                          <SlideListItem
                            key={slide.id}
                            slide={slide}
                            index={index}
                            isSelected={selection.slideIds.includes(slide.id)}
                            isActive={activeSlideId === slide.id}
                            isLive={isLive && currentSlideId === slide.id}
                            slideNumber={slideNumberById.get(slide.id)}
                            groupLabel={index === 0 ? getSlideGroupLabel(slide) : null}
                            presentation={presentation}
                            presentationPath={filePath}
                            pendingMedia={pendingMedia}
                            themes={themes}
                            onSelect={(event) => handleSelectSlide(slide.id, event)}
                            onContextSelect={() => handleContextSelectSlide(slide.id)}
                            onTake={() => handleTakeSlide(slide.id)}
                            onApplyTheme={(theme, themeSlide) =>
                              handleApplyTheme(
                                theme,
                                themeSlide,
                                getContextSlideSelection(slide.id)
                              )
                            }
                            onDuplicate={() => {
                              const targetIds = getContextSlideSelection(slide.id);
                              if (targetIds.length > 1) {
                                duplicateSlides(targetIds);
                              } else {
                                duplicateSlide(slide.id);
                              }
                            }}
                            onDelete={() => {
                              const targetIds = getContextSlideSelection(slide.id);
                              if (targetIds.length > 1) {
                                deleteSlides(targetIds);
                              } else {
                                deleteSlide(slide.id);
                              }
                            }}
                            onSetSection={(section) => {
                              const targetIds = getContextSlideSelection(slide.id);
                              if (targetIds.length > 1) {
                                setSlidesSection(targetIds, section);
                              } else {
                                setSlideSection(slide.id, section);
                              }
                            }}
                          />
                        ))}
                      </div>
                    </div>
                  ))}
                </div>
              </SortableContext>
            </DndContext>
          </ScrollArea>
        </div>

        {/* Canvas (Center) */}
        <div className="flex-1 overflow-hidden">
          <SlideCanvas
            slide={activeSlide || null}
            aspectRatio={aspectRatio}
            presentation={presentation}
            presentationPath={filePath}
            pendingMedia={pendingMedia}
          />
        </div>

        {/* Inspector (Right) */}
        <div className="w-80 shrink-0 border-l bg-muted/30 flex min-h-0 flex-col">
          <div className="flex-1 min-h-0">
            <SlideInspector slide={activeSlide || null} />
          </div>
        </div>
      </div>

      <Dialog
        open={themeMismatchDialogOpen}
        onOpenChange={(open) => {
          setThemeMismatchDialogOpen(open);
          if (!open) {
            setPendingThemeApply(null);
          }
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Aspect Ratio Mismatch</DialogTitle>
            <DialogDescription>
              This theme was created for {pendingThemeApply?.theme.aspectRatio} but this
              presentation is set to {presentation.manifest.aspectRatio ?? '16:9'}.
              Choose how to apply the theme.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter className="flex flex-col sm:flex-row sm:justify-end gap-2">
            <Button variant="outline" onClick={() => resolveThemeMismatch('cancel')}>
              Cancel
            </Button>
            <Button variant="outline" onClick={() => resolveThemeMismatch('fit')}>
              Scale theme to fit
            </Button>
            <Button onClick={() => resolveThemeMismatch('match')}>
              Match presentation to theme
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
      </div>
    </Profiler>
  );
}

interface SlideGroup {
  section: SongSection | null;
  label: string;
  slides: Slide[];
}

function groupSlidesBySection(slides: Slide[]): SlideGroup[] {
  const groups: SlideGroup[] = [];
  const sectionMap = new Map<string, Slide[]>();

  // Group slides
  for (const slide of slides) {
    const key = slide.section || '_unsorted';
    if (!sectionMap.has(key)) {
      sectionMap.set(key, []);
    }
    sectionMap.get(key)!.push(slide);
  }

  // Convert to array, keeping order
  const sectionOrder = ['_unsorted', ...SONG_SECTIONS];
  for (const section of sectionOrder) {
    const slides = sectionMap.get(section);
    if (slides && slides.length > 0) {
      groups.push({
        section: section === '_unsorted' ? null : (section as SongSection),
        label: section === '_unsorted' ? 'Slides' : formatSectionLabel(section as SongSection),
        slides,
      });
    }
  }

  return groups;
}

interface SlideListItemProps {
  slide: Slide;
  index: number;
  isSelected: boolean;
  isActive: boolean;
  isLive: boolean;
  slideNumber?: number;
  groupLabel?: string | null;
  onSelect: (event: MouseEvent) => void;
  onContextSelect: () => void;
  onTake: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
  onSetSection: (section: SongSection) => void;
  onApplyTheme: (theme: ThemeTemplate, themeSlide: ThemeTemplateSlide) => void;
  themes: ThemeTemplate[];
  presentation?: Presentation | null;
  presentationPath?: string | null;
  pendingMedia?: Map<string, string>;
}

function SlideListItem({
  slide,
  isSelected,
  isActive,
  isLive,
  slideNumber,
  groupLabel,
  onSelect,
  onContextSelect,
  onTake,
  onDuplicate,
  onDelete,
  onSetSection,
  onApplyTheme,
  themes,
  presentation,
  presentationPath,
  pendingMedia,
}: SlideListItemProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: slide.id,
    data: { section: slide.section ?? null },
  });
  const { onPointerEnter, onPointerLeave } = useCursorHover('pointer');
  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
  };

  return (
    <ContextMenu>
      <ContextMenuTrigger>
        <div
          ref={setNodeRef}
          style={style}
          className={cn(
            'group relative rounded-md p-1 cursor-pointer',
            isActive && 'bg-accent',
            isLive && 'ring-2 ring-green-500',
            isDragging && 'opacity-60'
          )}
          onContextMenu={onContextSelect}
          onPointerEnter={onPointerEnter}
          onPointerLeave={onPointerLeave}
        >
          <div className="flex items-center gap-1">
            <button
              type="button"
              className="h-6 w-6 rounded-sm text-muted-foreground opacity-0 group-hover:opacity-100 flex items-center justify-center"
              aria-label="Reorder slide"
              {...attributes}
              {...listeners}
            >
              <GripVertical className="h-4 w-4" />
            </button>
            <div className="flex-1" onClick={onSelect} onDoubleClick={onTake}>
              <SlideThumbnail
                slide={slide}
                isSelected={isSelected}
                isActive={isLive}
                showLabel={false}
                slideNumber={slideNumber}
                numberPlacement="overlay-left"
                showFooter={false}
                presentation={presentation}
                presentationPath={presentationPath}
                pendingMedia={pendingMedia}
              />
            </div>
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button
                  variant="ghost"
                  size="icon"
                  className="h-6 w-6 opacity-0 group-hover:opacity-100"
                >
                  <MoreHorizontal className="h-3 w-3" />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end">
                <DropdownMenuItem onClick={onTake}>
                  Take Live
                </DropdownMenuItem>
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={onDuplicate}>
                  <Copy className="mr-2 h-4 w-4" />
                  Duplicate
                </DropdownMenuItem>
                <DropdownMenuSub>
                  <DropdownMenuSubTrigger>Set Section</DropdownMenuSubTrigger>
                  <DropdownMenuSubContent>
                    {SONG_SECTIONS.map((section) => (
                      <DropdownMenuItem
                        key={section}
                        onClick={() => onSetSection(section)}
                      >
                        {formatSectionLabel(section)}
                      </DropdownMenuItem>
                    ))}
                  </DropdownMenuSubContent>
                </DropdownMenuSub>
                <DropdownMenuSeparator />
                <DropdownMenuItem onClick={onDelete} className="text-destructive">
                  <Trash2 className="mr-2 h-4 w-4" />
                  Delete
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </div>
        </div>
      </ContextMenuTrigger>
      <ContextMenuContent>
        <ContextMenuItem onClick={onTake}>Take Live</ContextMenuItem>
        <ContextMenuSeparator />
        <ContextMenuItem onClick={onDuplicate}>
          <Copy className="mr-2 h-4 w-4" />
          Duplicate
        </ContextMenuItem>
        <ContextMenuSub>
          <ContextMenuSubTrigger>Set Section</ContextMenuSubTrigger>
          <ContextMenuSubContent>
            {SONG_SECTIONS.map((section) => (
              <ContextMenuItem
                key={section}
                onClick={() => onSetSection(section)}
              >
                {formatSectionLabel(section)}
              </ContextMenuItem>
            ))}
          </ContextMenuSubContent>
        </ContextMenuSub>
        <ContextMenuSub>
          <ContextMenuSubTrigger disabled={themes.length === 0}>
            Apply Theme
          </ContextMenuSubTrigger>
          <ContextMenuSubContent>
            {themes.length === 0 ? (
              <ContextMenuItem disabled>No themes available</ContextMenuItem>
            ) : (
              themes.map((theme) => (
                <ContextMenuSub key={theme.id}>
                  <ContextMenuSubTrigger disabled={theme.slides.length === 0}>
                    {theme.name}
                  </ContextMenuSubTrigger>
                  <ContextMenuSubContent>
                    {theme.slides.length === 0 ? (
                      <ContextMenuItem disabled>No theme slides</ContextMenuItem>
                    ) : (
                      theme.slides.map((themeSlide, index) => (
                        <ContextMenuItem
                          key={themeSlide.id}
                          onClick={() => onApplyTheme(theme, themeSlide)}
                        >
                          {themeSlide.name ?? `Slide ${index + 1}`}
                        </ContextMenuItem>
                      ))
                    )}
                  </ContextMenuSubContent>
                </ContextMenuSub>
              ))
            )}
          </ContextMenuSubContent>
        </ContextMenuSub>
        <ContextMenuSeparator />
        <ContextMenuItem onClick={onDelete} className="text-destructive">
          <Trash2 className="mr-2 h-4 w-4" />
          Delete
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  );
}
