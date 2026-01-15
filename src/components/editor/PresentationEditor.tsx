/**
 * PresentationEditor - Main editor for creating/editing presentations
 */

import { useEffect, useMemo, useState } from 'react';
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
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
  ContextMenuSub,
  ContextMenuSubContent,
  ContextMenuSubTrigger,
} from '@/components/ui/context-menu';
import { useCatalogStore, useEditorStore, useLiveStore, useMusicManagerStore } from '@/lib/stores';
import { SlideThumbnail } from '@/components/preview/SlideRenderer';
import { SlideCanvas } from './SlideCanvas';
import { SlideInspector } from './SlideInspector';
import { SONG_SECTIONS, formatSectionLabel, getSlideGroupLabel } from '@/lib/models';
import type { SongSection, Slide, Theme } from '@/lib/models';
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
    addSlide,
    duplicateSlide,
    deleteSlide,
    setSlideSection,
    updateTitle,
    filePath,
    unlinkExternalSong,
  } = useEditorStore();

  const { isLive, currentSlideId, goToSlide } = useLiveStore();
  const { catalog } = useCatalogStore();
  const { groups, loadGroups, isLoadingGroups } = useMusicManagerStore();

  const [isEditingTitle, setIsEditingTitle] = useState(false);

  if (!presentation) return null;

  const activeSlide = presentation.slides.find((s) => s.id === activeSlideId);
  const currentTheme = presentation.themes.find(
    (t) => t.id === presentation.manifest.themeId
  );
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
    void loadBundledFonts(presentation, filePath);
  }, [presentation, filePath]);

  // Group slides by section
  const slidesBySection = groupSlidesBySection(presentation.slides);
  const slideNumberById = useMemo(
    () => new Map(presentation.slides.map((slide, index) => [slide.id, index + 1])),
    [presentation.slides]
  );

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

  const externalSong = presentation.manifest.externalSong;
  const isLinked = !!externalSong;

  const handleLinkToggle = () => {
    if (isLinked) {
      unlinkExternalSong();
      return;
    }
    onRequestLink?.();
  };

  return (
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
      <div className="flex flex-1 overflow-hidden">
        {/* Slide List (Left) */}
        <div className="w-56 shrink-0 border-r bg-muted/30">
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
          <ScrollArea className="h-full">
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
                        theme={currentTheme || null}
                        isSelected={selection.slideIds.includes(slide.id)}
                        isActive={activeSlideId === slide.id}
                        isLive={isLive && currentSlideId === slide.id}
                        slideNumber={slideNumberById.get(slide.id)}
                        groupLabel={index === 0 ? getSlideGroupLabel(slide) : null}
                        onSelect={() => selectSlide(slide.id)}
                        onTake={() => handleTakeSlide(slide.id)}
                        onDuplicate={() => duplicateSlide(slide.id)}
                        onDelete={() => deleteSlide(slide.id)}
                        onSetSection={(section) => setSlideSection(slide.id, section)}
                      />
                    ))}
                  </div>
                </div>
              ))}
            </div>
          </ScrollArea>
        </div>

        {/* Canvas (Center) */}
        <div className="flex-1 overflow-hidden">
          <SlideCanvas slide={activeSlide || null} theme={currentTheme || null} />
        </div>

        {/* Inspector (Right) */}
        <div className="w-80 shrink-0 border-l bg-muted/30">
          <SlideInspector slide={activeSlide || null} theme={currentTheme || null} />
        </div>
      </div>
    </div>
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
  theme: Theme | null;
  isSelected: boolean;
  isActive: boolean;
  isLive: boolean;
  slideNumber?: number;
  groupLabel?: string | null;
  onSelect: () => void;
  onTake: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
  onSetSection: (section: SongSection) => void;
}

function SlideListItem({
  slide,
  theme,
  isSelected,
  isActive,
  isLive,
  slideNumber,
  groupLabel,
  onSelect,
  onTake,
  onDuplicate,
  onDelete,
  onSetSection,
}: SlideListItemProps) {
  return (
    <ContextMenu>
      <ContextMenuTrigger>
        <div
          className={cn(
            'group relative rounded-md p-1 cursor-pointer',
            isActive && 'bg-accent',
            isLive && 'ring-2 ring-green-500'
          )}
        >
          <div className="flex items-center gap-1">
            <GripVertical className="h-4 w-4 text-muted-foreground opacity-0 group-hover:opacity-100 drag-handle" />
            <div className="flex-1" onClick={onSelect} onDoubleClick={onTake}>
              <SlideThumbnail
                slide={slide}
                theme={theme}
                isSelected={isSelected}
                isActive={isLive}
                slideNumber={slideNumber}
                groupLabel={groupLabel}
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
        <ContextMenuSeparator />
        <ContextMenuItem onClick={onDelete} className="text-destructive">
          <Trash2 className="mr-2 h-4 w-4" />
          Delete
        </ContextMenuItem>
      </ContextMenuContent>
    </ContextMenu>
  );
}
