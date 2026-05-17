/**
 * ThemesPage - global theme library and editor
 */

import { useEffect, useMemo, useState, useCallback, useRef, type MouseEvent } from 'react';
import { v4 as uuid } from 'uuid';
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
  arrayMove,
  SortableContext,
  sortableKeyboardCoordinates,
  useSortable,
  verticalListSortingStrategy,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';
import { restrictToVerticalAxis } from '@dnd-kit/modifiers';
import {
  Palette,
  Plus,
  Copy,
  Trash2,
  Pencil,
  ChevronDown,
  MoreHorizontal,
  GripVertical,
  Wand2,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Input } from '@/components/ui/input';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuGroup,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuGroup,
  ContextMenuItem,
  ContextMenuLabel,
  ContextMenuSeparator,
  ContextMenuSub,
  ContextMenuSubContent,
  ContextMenuSubTrigger,
  ContextMenuTrigger,
} from '@/components/ui/context-menu';
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { useCatalogStore, useEditorStore, useThemeStore } from '@/lib/stores';
import { useCursorHover } from '@/components/cursor';
import {
  createArrangement,
  createSlide,
  defaultSlideTransition,
  applyThemeSlideToSlideInPlace,
  getThemeSlideLayers,
  type Presentation,
  type PresentationRef,
  type Slide,
  type ThemeScaleMode,
  type ThemeTemplate,
  type ThemeTemplateSlide,
} from '@/lib/models';
import { openBundle, saveBundle, type FontFileRef, type MediaFileRef } from '@/lib/tauri-api';
import { getDocumentsDataDirPath } from '@/lib/services/appDataService';
import { createThemeSlideFromSlide, createThemeTemplateFromSlide } from '@/lib/stores/themeStore';
import { ThemeEditor } from '@/components/themes/ThemeEditor';
import { SlideThumbnail } from '@/components/preview';
import { ApplyThemeDialog } from '@/components/dialogs';
import { cn } from '@/lib/utils';

const cloneThemeSlide = (slide: ThemeTemplateSlide, name: string): ThemeTemplateSlide => ({
  ...slide,
  id: uuid(),
  name,
  background: { ...slide.background },
  layers:
    typeof structuredClone === 'function'
      ? (structuredClone(getThemeSlideLayers(slide)) as Slide['layers'])
      : (JSON.parse(JSON.stringify(getThemeSlideLayers(slide))) as Slide['layers']),
  mediaCues: Array.isArray(slide.mediaCues) ? slide.mediaCues.map((cue) => ({ ...cue })) : [],
});

const toThemeSlide = (themeSlide: ThemeTemplateSlide): Slide => ({
  id: themeSlide.id,
  type: 'blank',
  layoutType: themeSlide.layoutType,
  layers:
    typeof structuredClone === 'function'
      ? (structuredClone(getThemeSlideLayers(themeSlide)) as Slide['layers'])
      : (JSON.parse(JSON.stringify(getThemeSlideLayers(themeSlide))) as Slide['layers']),
  mediaCues: Array.isArray(themeSlide.mediaCues) ? themeSlide.mediaCues : [],
  background: themeSlide.background,
  animations: {
    transition: { ...defaultSlideTransition },
    buildIn: [],
    buildOut: [],
  },
  createdAt: new Date().toISOString(),
  updatedAt: new Date().toISOString(),
});

const buildThemePresentation = (
  theme: ThemeTemplate,
  slides: Slide[],
  mediaLibrary?: Presentation['manifest']['media']
): Presentation => ({
  manifest: {
    formatVersion: '1.0.0',
    presentationId: theme.id,
    title: theme.name,
    createdAt: theme.createdAt,
    updatedAt: theme.updatedAt,
    aspectRatio: theme.aspectRatio,
    slideSize: theme.baseSize,
    media: mediaLibrary ?? [],
    fonts: [],
  },
  slides,
  arrangement: createArrangement(slides),
  themes: [],
});

const getBaseSlideSize = (
  aspectRatio?: '16:9' | '4:3' | '16:10',
  slideSize?: { width: number; height: number }
) => {
  if (
    slideSize &&
    Number.isFinite(slideSize.width) &&
    Number.isFinite(slideSize.height) &&
    slideSize.width > 0 &&
    slideSize.height > 0
  ) {
    return {
      width: Math.round(slideSize.width),
      height: Math.round(slideSize.height),
    };
  }
  switch (aspectRatio) {
    case '4:3':
      return { width: 1440, height: 1080 };
    case '16:10':
      return { width: 1920, height: 1200 };
    case '16:9':
    default:
      return { width: 1920, height: 1080 };
  }
};

const clonePresentation = (presentation: Presentation): Presentation =>
  typeof structuredClone === 'function'
    ? (structuredClone(presentation) as Presentation)
    : (JSON.parse(JSON.stringify(presentation)) as Presentation);

const buildBundleMediaRefs = (presentation: Presentation): MediaFileRef[] => {
  const safeMedia = Array.isArray(presentation.manifest.media) ? presentation.manifest.media : [];
  return safeMedia.map((entry) => ({
    id: entry.id,
    source_path: `bundle:${entry.path}`,
    bundle_path: entry.path,
  }));
};

const buildBundleFontRefs = (presentation: Presentation): FontFileRef[] => {
  const safeFonts = Array.isArray(presentation.manifest.fonts) ? presentation.manifest.fonts : [];
  return safeFonts.map((entry) => ({
    id: entry.id,
    source_path: `bundle:${entry.path}`,
    bundle_path: entry.path,
  }));
};

const isAbsolutePath = (path: string) => /^[a-zA-Z]:[\\/]|^\//.test(path);

const resolvePresentationPath = async (path: string) => {
  if (isAbsolutePath(path)) return path;
  const baseDir = await getDocumentsDataDirPath();
  return `${baseDir}/${path}`.replace(/\\/g, '/');
};

export function ThemesPage() {
  const {
    themes,
    loadThemes,
    addTheme,
    updateTheme,
    deleteTheme,
    duplicateTheme,
  } = useThemeStore();
  const { catalog } = useCatalogStore();
  const {
    presentation: editorPresentation,
    activeSlideId,
    filePath: editorFilePath,
    openPresentation,
  } = useEditorStore();
  const mediaLibrary = editorPresentation?.manifest.media ?? [];
  const [selectedThemeId, setSelectedThemeId] = useState<string | null>(null);
  const [selectedThemeSlideId, setSelectedThemeSlideId] = useState<string | null>(null);
  const [selectedThemeSlideIds, setSelectedThemeSlideIds] = useState<string[]>([]);
  const [lastSelectedSlideId, setLastSelectedSlideId] = useState<string | null>(null);
  const [rangeAnchorSlideId, setRangeAnchorSlideId] = useState<string | null>(null);
  const [pendingDeleteThemeId, setPendingDeleteThemeId] = useState<string | null>(null);
  const [editingThemeId, setEditingThemeId] = useState<string | null>(null);
  const [editingThemeName, setEditingThemeName] = useState('');
  const [applyThemeId, setApplyThemeId] = useState<string | null>(null);
  const [applyThemeDialogOpen, setApplyThemeDialogOpen] = useState(false);
  const [applyMenuOpen, setApplyMenuOpen] = useState(false);
  const [applyPresentation, setApplyPresentation] = useState<Presentation | null>(null);
  const [applyPresentationPath, setApplyPresentationPath] = useState<string | null>(null);
  const [applyPresentationSearch, setApplyPresentationSearch] = useState('');
  const [applyPresentationLoading, setApplyPresentationLoading] = useState(false);
  const [applyPresentationError, setApplyPresentationError] = useState<string | null>(null);
  const [applyPresentationSaving, setApplyPresentationSaving] = useState(false);
  const renameInputRef = useRef<HTMLInputElement | null>(null);

  useEffect(() => {
    void loadThemes();
  }, [loadThemes]);

  useEffect(() => {
    if (selectedThemeId && themes.some((theme) => theme.id === selectedThemeId)) return;
    setSelectedThemeId(themes[0]?.id ?? null);
  }, [themes, selectedThemeId]);

  const selectedTheme = useMemo(
    () => themes.find((theme) => theme.id === selectedThemeId) ?? null,
    [themes, selectedThemeId]
  );
  const applyThemeTarget = useMemo(
    () => themes.find((theme) => theme.id === applyThemeId) ?? null,
    [themes, applyThemeId]
  );
  const applyThemeResolved = useMemo(
    () => applyThemeTarget ?? selectedTheme ?? null,
    [applyThemeTarget, selectedTheme]
  );
  const selectedThemeSlide = useMemo(
    () => selectedTheme?.slides.find((slide) => slide.id === selectedThemeSlideId) ?? null,
    [selectedTheme, selectedThemeSlideId]
  );
  const themeSlidesForPreview = useMemo(
    () => selectedTheme?.slides.map((slide) => toThemeSlide(slide)) ?? [],
    [selectedTheme]
  );
  const themePresentation = useMemo(
    () =>
      selectedTheme
        ? buildThemePresentation(selectedTheme, themeSlidesForPreview, mediaLibrary)
        : null,
    [selectedTheme, themeSlidesForPreview, mediaLibrary]
  );
  const themeSlideIds = useMemo(
    () => selectedTheme?.slides.map((slide) => slide.id) ?? [],
    [selectedTheme]
  );
  const libraries = catalog.libraries;
  const hasLibraryPresentations = useMemo(
    () => libraries.some((library) => library.presentations.length > 0),
    [libraries]
  );
  const filteredLibraries = useMemo(() => {
    const query = applyPresentationSearch.trim().toLowerCase();
    if (!query) {
      return libraries.map((library) => ({
        ...library,
        presentations: library.presentations,
      }));
    }
    const matchesPresentation = (presentationRef: PresentationRef) =>
      presentationRef.title.toLowerCase().includes(query) ||
      presentationRef.path.toLowerCase().includes(query);
    return libraries.map((library) => ({
      ...library,
      presentations: library.presentations.filter(matchesPresentation),
    }));
  }, [applyPresentationSearch, libraries]);

  const sensors = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(KeyboardSensor, {
      coordinateGetter: sortableKeyboardCoordinates,
    })
  );

  useEffect(() => {
    if (!selectedTheme) {
      setSelectedThemeSlideId(null);
      setSelectedThemeSlideIds([]);
      setLastSelectedSlideId(null);
      setRangeAnchorSlideId(null);
      return;
    }
    if (selectedThemeSlideId && selectedTheme.slides.some((slide) => slide.id === selectedThemeSlideId)) {
      return;
    }
    setSelectedThemeSlideId(selectedTheme.slides[0]?.id ?? null);
    setSelectedThemeSlideIds(selectedTheme.slides[0]?.id ? [selectedTheme.slides[0]!.id] : []);
    setLastSelectedSlideId(selectedTheme.slides[0]?.id ?? null);
    setRangeAnchorSlideId(selectedTheme.slides[0]?.id ?? null);
  }, [selectedTheme, selectedThemeSlideId]);

  const getSlideRangeSelection = useCallback(
    (startId: string, endId: string) => {
      const startIndex = themeSlideIds.indexOf(startId);
      const endIndex = themeSlideIds.indexOf(endId);
      if (startIndex === -1 || endIndex === -1) return [endId];
      const [from, to] = startIndex <= endIndex ? [startIndex, endIndex] : [endIndex, startIndex];
      return themeSlideIds.slice(from, to + 1);
    },
    [themeSlideIds]
  );

  const handleSelectThemeSlide = useCallback(
    (slideId: string, event: MouseEvent) => {
      const isToggle = event.metaKey || event.ctrlKey;
      const isRange = event.shiftKey;
      const rangeAnchor = rangeAnchorSlideId ?? lastSelectedSlideId ?? selectedThemeSlideId ?? null;

      if (isRange && rangeAnchor) {
        const range = getSlideRangeSelection(rangeAnchor, slideId);
        const nextSelection = Array.from(new Set([...selectedThemeSlideIds, ...range]));
        setSelectedThemeSlideIds(nextSelection);
        setSelectedThemeSlideId(slideId);
      } else if (isToggle) {
        const isAlreadySelected = selectedThemeSlideIds.includes(slideId);
        if (isAlreadySelected) {
          const nextSelection = selectedThemeSlideIds.filter((id) => id !== slideId);
          setSelectedThemeSlideIds(nextSelection);
          setSelectedThemeSlideId(nextSelection[0] ?? null);
        } else {
          const nextSelection = [slideId, ...selectedThemeSlideIds];
          setSelectedThemeSlideIds(nextSelection);
          setSelectedThemeSlideId(slideId);
          setRangeAnchorSlideId(rangeAnchorSlideId ?? slideId);
        }
      } else {
        setSelectedThemeSlideIds([slideId]);
        setSelectedThemeSlideId(slideId);
        setRangeAnchorSlideId(slideId);
      }

      setLastSelectedSlideId(slideId);
    },
    [
      getSlideRangeSelection,
      lastSelectedSlideId,
      rangeAnchorSlideId,
      selectedThemeSlideId,
      selectedThemeSlideIds,
    ]
  );

  const handleContextSelectSlide = useCallback(
    (slideId: string) => {
      if (!selectedThemeSlideIds.includes(slideId)) {
        setSelectedThemeSlideIds([slideId]);
        setSelectedThemeSlideId(slideId);
      }
      setLastSelectedSlideId(slideId);
    },
    [selectedThemeSlideIds]
  );

  const getContextSlideSelection = useCallback(
    (slideId: string) =>
      selectedThemeSlideIds.includes(slideId) ? selectedThemeSlideIds : [slideId],
    [selectedThemeSlideIds]
  );
  const activeSlide = useMemo(
    () => editorPresentation?.slides.find((slide) => slide.id === activeSlideId) ?? null,
    [editorPresentation, activeSlideId]
  );
  const aspectRatio = editorPresentation?.manifest.aspectRatio ?? '16:9';

  const handleAddTheme = () => {
    const blankSlide = createSlide('blank');
    const theme = createThemeTemplateFromSlide('', blankSlide, aspectRatio);
    theme.slides = [
      {
        id: uuid(),
        name: undefined,
        background: { type: 'transparent' },
        layers: [],
        mediaCues: [],
      },
    ];
    addTheme(theme);
    setSelectedThemeId(theme.id);
    startInlineRename(theme.id, '');
  };

  const handleDuplicateTheme = (themeId: string) => {
    const duplicate = duplicateTheme(themeId);
    if (duplicate) {
      setSelectedThemeId(duplicate.id);
      startInlineRename(duplicate.id, duplicate.name);
    }
  };

  const handleDeleteTheme = (themeId: string) => {
    deleteTheme(themeId);
    setPendingDeleteThemeId(null);
  };

  const handleApplyMenuOpenChange = useCallback((themeId: string, open: boolean) => {
    setApplyMenuOpen(open);
    if (open) {
      setSelectedThemeId(themeId);
      setApplyThemeId(themeId);
      setApplyPresentationError(null);
    } else {
      setApplyPresentationSearch('');
    }
  }, []);

  const handleSelectApplyPresentation = useCallback(
    async (themeId: string, presentationRef: PresentationRef) => {
      setApplyThemeId(themeId);
      setApplyPresentationLoading(true);
      setApplyPresentationError(null);
      try {
        const resolvedPath = await resolvePresentationPath(presentationRef.path);
        const loaded = await openBundle(resolvedPath);
        setApplyPresentation(loaded);
        setApplyPresentationPath(resolvedPath);
        setApplyThemeDialogOpen(true);
        setApplyMenuOpen(false);
      } catch (error) {
        setApplyPresentationError(String(error));
      } finally {
        setApplyPresentationLoading(false);
      }
    },
    []
  );

  const handleApplyThemeToPresentation = useCallback(
    async (mapping: Record<string, string>, options: { scaleMode: ThemeScaleMode }) => {
      if (!applyThemeResolved || !applyPresentation || !applyPresentationPath) return;
      if (applyPresentationSaving) return;
      if (Object.keys(mapping).length === 0) return;
      setApplyPresentationSaving(true);
      try {
        const nextPresentation = clonePresentation(applyPresentation);
        const targetAspect = nextPresentation.manifest.aspectRatio ?? '16:9';
        const targetSize = getBaseSlideSize(targetAspect, nextPresentation.manifest.slideSize);
        const sourceSize = applyThemeResolved.baseSize ?? getBaseSlideSize(applyThemeResolved.aspectRatio);
        const scaleMode = options?.scaleMode ?? 'none';
        const applyOptions = { scaleMode, sourceSize, targetSize };

        const themeSlideById = new Map(applyThemeResolved.slides.map((slide) => [slide.id, slide]));
        const updateIdSet = new Set(Object.keys(mapping));
        nextPresentation.slides.forEach((slide) => {
          if (!updateIdSet.has(slide.id)) return;
          const themeSlideId = mapping[slide.id];
          const themeSlide = themeSlideById.get(themeSlideId);
          if (!themeSlide) return;
          applyThemeSlideToSlideInPlace(slide, themeSlide, applyOptions);
        });

        nextPresentation.manifest.updatedAt = new Date().toISOString();
        const mediaRefs = buildBundleMediaRefs(nextPresentation);
        const fontRefs = buildBundleFontRefs(nextPresentation);
        await saveBundle(applyPresentationPath, nextPresentation, mediaRefs, fontRefs);
        if (editorFilePath === applyPresentationPath) {
          await openPresentation(applyPresentationPath);
        }
      } catch (error) {
        console.error('Failed to apply theme to presentation', error);
      } finally {
        setApplyPresentationSaving(false);
      }
    },
    [applyPresentation, applyPresentationPath, applyPresentationSaving, applyThemeResolved]
  );

  const startInlineRename = (themeId: string, name: string) => {
    setEditingThemeId(themeId);
    setEditingThemeName(name);
  };

  const commitInlineRename = (themeId: string) => {
    const trimmed = editingThemeName.trim();
    if (trimmed.length > 0) {
      updateTheme(themeId, { name: trimmed });
    }
    setEditingThemeId(null);
    setEditingThemeName('');
  };

  const cancelInlineRename = () => {
    setEditingThemeId(null);
    setEditingThemeName('');
  };

  useEffect(() => {
    if (!editingThemeId) return;
    const id = window.setTimeout(() => {
      renameInputRef.current?.focus();
      renameInputRef.current?.select();
    }, 0);
    return () => window.clearTimeout(id);
  }, [editingThemeId]);

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      const isTypingTarget =
        target &&
        (target.isContentEditable ||
          target.tagName === 'INPUT' ||
          target.tagName === 'TEXTAREA' ||
          target.tagName === 'SELECT');
      if (isTypingTarget) return;
      if (!selectedTheme) return;

      if (event.key === 'F2') {
        event.preventDefault();
        startInlineRename(selectedTheme.id, selectedTheme.name);
        return;
      }

      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'd') {
        event.preventDefault();
        handleDuplicateTheme(selectedTheme.id);
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [selectedTheme, handleDuplicateTheme]);

  useEffect(() => {
    if (!applyThemeDialogOpen) {
      setApplyPresentation(null);
      setApplyPresentationPath(null);
    }
  }, [applyThemeDialogOpen]);

  useEffect(() => {
    if (!applyThemeDialogOpen && !applyMenuOpen) {
      setApplyThemeId(null);
      setApplyPresentationError(null);
    }
  }, [applyThemeDialogOpen, applyMenuOpen]);

  const handleAddThemeSlide = (mode: 'blank' | 'current') => {
    if (!selectedTheme) return;
    const baseSlide =
      mode === 'current' && activeSlideId
        ? editorPresentation?.slides.find((slide) => slide.id === activeSlideId) ?? null
        : null;
    const sourceSlide = baseSlide ?? createSlide('blank');
    const newSlide = createThemeSlideFromSlide(sourceSlide);
    updateTheme(selectedTheme.id, { slides: [...selectedTheme.slides, newSlide] });
    setSelectedThemeSlideId(newSlide.id);
    setSelectedThemeSlideIds([newSlide.id]);
    setLastSelectedSlideId(newSlide.id);
    setRangeAnchorSlideId(newSlide.id);
  };

  const handleDuplicateThemeSlide = (slideId: string) => {
    if (!selectedTheme) return;
    const targetIds = getContextSlideSelection(slideId);
    const nextSlides = [...selectedTheme.slides];
    const duplicatedIds: string[] = [];

    targetIds.forEach((targetId) => {
    const target = selectedTheme.slides.find((slide) => slide.id === targetId);
      if (!target) return;
      const duplicateSlide = cloneThemeSlide(
      target,
      target.name ? `${target.name} Copy` : ''
      );
      nextSlides.push(duplicateSlide);
      duplicatedIds.push(duplicateSlide.id);
    });

    updateTheme(selectedTheme.id, { slides: nextSlides });
    if (duplicatedIds.length > 0) {
      setSelectedThemeSlideIds(duplicatedIds);
      setSelectedThemeSlideId(duplicatedIds[0]);
      setLastSelectedSlideId(duplicatedIds[0]);
      setRangeAnchorSlideId(duplicatedIds[0]);
    }
  };

  const handleDeleteThemeSlide = (slideId: string) => {
    if (!selectedTheme) return;
    const targetIds = new Set(getContextSlideSelection(slideId));
    const nextSlides = selectedTheme.slides.filter((slide) => !targetIds.has(slide.id));
    updateTheme(selectedTheme.id, { slides: nextSlides });
    setSelectedThemeSlideId(nextSlides[0]?.id ?? null);
    setSelectedThemeSlideIds(nextSlides[0]?.id ? [nextSlides[0]!.id] : []);
    setLastSelectedSlideId(nextSlides[0]?.id ?? null);
    setRangeAnchorSlideId(nextSlides[0]?.id ?? null);
  };

  const handleDragEnd = useCallback(
    ({ active, over }: DragEndEvent) => {
      if (!selectedTheme || !over || active.id === over.id) return;
      const fromIndex = themeSlideIds.indexOf(String(active.id));
      const toIndex = themeSlideIds.indexOf(String(over.id));
      if (fromIndex === -1 || toIndex === -1) return;
      const nextSlides = arrayMove(selectedTheme.slides, fromIndex, toIndex);
      updateTheme(selectedTheme.id, { slides: nextSlides });
    },
    [selectedTheme, themeSlideIds, updateTheme]
  );

  return (
    <div className="flex h-full min-h-0">
      <div className="w-72 shrink-0 border-r bg-sidebar text-sidebar-foreground flex flex-col">
        <div className="flex items-center justify-between border-b px-4 py-3">
          <div className="flex items-center gap-2 text-sm font-semibold">
            <Palette className="h-4 w-4" />
            Themes
          </div>
          <Button
            variant="ghost"
            size="icon"
            className="h-7 w-7"
            onClick={() => {
              handleAddTheme();
            }}
          >
            <Plus className="h-4 w-4" />
          </Button>
        </div>
        <ScrollArea className="flex-1">
          <div className="space-y-1 p-2">
            {themes.length === 0 && (
              <div className="rounded-md border border-dashed border-sidebar-border p-3 text-xs text-muted-foreground">
                No themes yet. Create one to get started.
              </div>
            )}
            {themes.map((theme) => (
              <ContextMenu
                key={theme.id}
                onOpenChange={(open) => handleApplyMenuOpenChange(theme.id, open)}
              >
                <ContextMenuTrigger asChild>
                  <div
                    role="button"
                    tabIndex={0}
                    onClick={() => {
                      setSelectedThemeId(theme.id);
                    }}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault();
                        setSelectedThemeId(theme.id);
                      }
                    }}
                    onDoubleClick={(event) => {
                      event.stopPropagation();
                      setSelectedThemeId(theme.id);
                      startInlineRename(theme.id, theme.name);
                    }}
                    className={cn(
                      'group w-full rounded-md px-3 py-2 text-left text-sm transition-colors',
                      theme.id === selectedThemeId
                        ? 'bg-sidebar-accent text-sidebar-accent-foreground'
                        : 'hover:bg-sidebar-accent/70'
                    )}
                  >
                    <div className="flex items-center justify-between gap-2">
                      <div className="min-w-0">
                        {editingThemeId === theme.id ? (
                          <Input
                            ref={renameInputRef}
                            value={editingThemeName}
                            onChange={(event) => setEditingThemeName(event.target.value)}
                            onClick={(event) => event.stopPropagation()}
                            onKeyDown={(event) => {
                              if (event.key === 'Enter') {
                                event.preventDefault();
                                commitInlineRename(theme.id);
                              } else if (event.key === 'Escape') {
                                event.preventDefault();
                                cancelInlineRename();
                              }
                            }}
                            onBlur={() => {
                              commitInlineRename(theme.id);
                            }}
                            className="h-7"
                            autoFocus
                          />
                        ) : (
                          <div className="font-medium truncate">{theme.name}</div>
                        )}
                        <div className="text-[10px] text-muted-foreground">
                          {theme.slides.length} slide
                          {theme.slides.length === 1 ? '' : 's'}
                        </div>
                      </div>
                      <DropdownMenu onOpenChange={(open) => handleApplyMenuOpenChange(theme.id, open)}>
                        <DropdownMenuTrigger asChild>
                          <Button
                            type="button"
                            variant="ghost"
                            size="icon"
                            className="h-6 w-6 opacity-0 transition-opacity group-hover:opacity-100"
                            onClick={(event) => event.stopPropagation()}
                            aria-label="Theme actions"
                          >
                            <MoreHorizontal className="h-3 w-3" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuSub>
                            <DropdownMenuSubTrigger disabled={!hasLibraryPresentations}>
                              <Wand2 />
                              Apply to Presentation
                            </DropdownMenuSubTrigger>
                            <DropdownMenuSubContent>
                              <DropdownMenuItem asChild onSelect={(event) => event.preventDefault()}>
                                <Input
                                  value={applyPresentationSearch}
                                  onChange={(event) => setApplyPresentationSearch(event.target.value)}
                                  placeholder="Search presentations..."
                                />
                              </DropdownMenuItem>
                              <DropdownMenuSeparator />
                              {libraries.length === 0 ? (
                                <DropdownMenuItem disabled>
                                  No libraries yet. Create a library to store presentations.
                                </DropdownMenuItem>
                              ) : (
                                filteredLibraries.map((library, index) => (
                                  <DropdownMenuGroup key={library.id}>
                                    <DropdownMenuLabel>{library.name}</DropdownMenuLabel>
                                    {library.presentations.length === 0 ? (
                                      <DropdownMenuItem disabled>
                                        {applyPresentationSearch
                                          ? 'No matches in this library.'
                                          : 'No presentations.'}
                                      </DropdownMenuItem>
                                    ) : (
                                      library.presentations.map((presentationRef) => (
                                        <DropdownMenuItem
                                          key={presentationRef.path}
                                          onSelect={() =>
                                            handleSelectApplyPresentation(theme.id, presentationRef)
                                          }
                                          disabled={applyPresentationLoading}
                                        >
                                          {presentationRef.title || 'Untitled presentation'}
                                        </DropdownMenuItem>
                                      ))
                                    )}
                                    {index < filteredLibraries.length - 1 ? (
                                      <DropdownMenuSeparator />
                                    ) : null}
                                  </DropdownMenuGroup>
                                ))
                              )}
                              {applyPresentationError ? (
                                <DropdownMenuItem disabled>{applyPresentationError}</DropdownMenuItem>
                              ) : null}
                            </DropdownMenuSubContent>
                          </DropdownMenuSub>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            onClick={() => {
                              setSelectedThemeId(theme.id);
                              startInlineRename(theme.id, theme.name);
                            }}
                          >
                            <Pencil />
                            Rename
                          </DropdownMenuItem>
                          <DropdownMenuItem onClick={() => handleDuplicateTheme(theme.id)}>
                            <Copy />
                            Duplicate
                          </DropdownMenuItem>
                          <DropdownMenuSeparator />
                          <DropdownMenuItem
                            variant="destructive"
                            onClick={() => setPendingDeleteThemeId(theme.id)}
                          >
                            <Trash2 />
                            Delete
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </div>
                  </div>
                </ContextMenuTrigger>
                <AlertDialog
                  open={pendingDeleteThemeId === theme.id}
                  onOpenChange={(open) => setPendingDeleteThemeId(open ? theme.id : null)}
                >
                  <AlertDialogContent>
                    <AlertDialogHeader>
                      <AlertDialogTitle>Delete theme</AlertDialogTitle>
                      <AlertDialogDescription>
                        This action cannot be undone. This will permanently remove the theme.
                      </AlertDialogDescription>
                    </AlertDialogHeader>
                    <AlertDialogFooter>
                      <AlertDialogCancel>Cancel</AlertDialogCancel>
                      <AlertDialogAction
                        className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
                        onClick={() => handleDeleteTheme(theme.id)}
                      >
                        Delete
                      </AlertDialogAction>
                    </AlertDialogFooter>
                  </AlertDialogContent>
                </AlertDialog>
                <ContextMenuContent>
                  <ContextMenuSub>
                    <ContextMenuSubTrigger disabled={!hasLibraryPresentations}>
                      <Wand2 />
                      Apply to Presentation
                    </ContextMenuSubTrigger>
                    <ContextMenuSubContent>
                      <ContextMenuItem asChild onSelect={(event) => event.preventDefault()}>
                        <Input
                          value={applyPresentationSearch}
                          onChange={(event) => setApplyPresentationSearch(event.target.value)}
                          placeholder="Search presentations..."
                        />
                      </ContextMenuItem>
                      <ContextMenuSeparator />
                      {libraries.length === 0 ? (
                        <ContextMenuItem disabled>
                          No libraries yet. Create a library to store presentations.
                        </ContextMenuItem>
                      ) : (
                      filteredLibraries.map((library, index) => (
                        <ContextMenuGroup key={library.id}>
                          <ContextMenuLabel>{library.name}</ContextMenuLabel>
                            {library.presentations.length === 0 ? (
                              <ContextMenuItem disabled>
                                {applyPresentationSearch
                                  ? 'No matches in this library.'
                                  : 'No presentations.'}
                              </ContextMenuItem>
                            ) : (
                              library.presentations.map((presentationRef) => (
                                <ContextMenuItem
                                  key={presentationRef.path}
                                  onSelect={() =>
                                    handleSelectApplyPresentation(theme.id, presentationRef)
                                  }
                                  disabled={applyPresentationLoading}
                                >
                                  {presentationRef.title || 'Untitled presentation'}
                                </ContextMenuItem>
                              ))
                            )}
                          {index < filteredLibraries.length - 1 ? <ContextMenuSeparator /> : null}
                        </ContextMenuGroup>
                        ))
                      )}
                      {applyPresentationError ? (
                        <ContextMenuItem disabled>{applyPresentationError}</ContextMenuItem>
                      ) : null}
                    </ContextMenuSubContent>
                  </ContextMenuSub>
                  <ContextMenuSeparator />
                  <ContextMenuItem
                    onClick={() => {
                      setSelectedThemeId(theme.id);
                      startInlineRename(theme.id, theme.name);
                    }}
                  >
                    <Pencil />
                    Rename
                  </ContextMenuItem>
                  <ContextMenuItem onClick={() => handleDuplicateTheme(theme.id)}>
                    <Copy />
                    Duplicate
                  </ContextMenuItem>
                  <ContextMenuSeparator />
                  <ContextMenuItem
                    variant="destructive"
                    onClick={() => setPendingDeleteThemeId(theme.id)}
                  >
                    <Trash2 />
                    Delete
                  </ContextMenuItem>
                </ContextMenuContent>
              </ContextMenu>
            ))}
          </div>
        </ScrollArea>
      </div>

      <div className="flex-1 min-w-0">
        {selectedTheme ? (
          <div className="flex h-full min-h-0">
            <div className="w-64 shrink-0 border-r bg-muted/30 flex min-h-0 flex-col">
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
                    <DropdownMenuItem onClick={() => handleAddThemeSlide('blank')}>
                      Blank Slide
                    </DropdownMenuItem>
                    <DropdownMenuSeparator />
                    <DropdownMenuItem
                      disabled={!editorPresentation || !activeSlideId}
                      onClick={() => handleAddThemeSlide('current')}
                    >
                      From Current Slide
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </div>
              <ScrollArea className="flex-1">
                <DndContext
                  sensors={sensors}
                  collisionDetection={closestCenter}
                  onDragEnd={handleDragEnd}
                  modifiers={[restrictToVerticalAxis]}
                >
                  <SortableContext items={themeSlideIds} strategy={verticalListSortingStrategy}>
                    <div className="p-2 space-y-4">
                      {selectedTheme.slides.length === 0 && (
                        <div className="rounded-md border border-dashed border-border p-3 text-xs text-muted-foreground">
                          No theme slides yet. Add one to get started.
                        </div>
                      )}
                      {themeSlidesForPreview.map((slide, index) => {
                        const themeSlide = selectedTheme.slides[index];
                        const label =
                          themeSlide?.layoutType?.trim() ||
                          themeSlide?.name?.trim() ||
                          undefined;
                        return (
                          <ThemeSlideListItem
                            key={slide.id}
                            slide={slide}
                            label={label}
                            slideNumber={index + 1}
                            isSelected={selectedThemeSlideIds.includes(slide.id)}
                            isActive={slide.id === selectedThemeSlideId}
                            presentation={themePresentation}
                            onSelect={(event) => handleSelectThemeSlide(slide.id, event)}
                            onContextSelect={() => handleContextSelectSlide(slide.id)}
                            onDuplicate={() => handleDuplicateThemeSlide(slide.id)}
                            onDelete={() => handleDeleteThemeSlide(slide.id)}
                          />
                        );
                      })}
                    </div>
                  </SortableContext>
                </DndContext>
              </ScrollArea>
            </div>
            <div className="flex-1 min-w-0">
              {selectedThemeSlide ? (
                <ThemeEditor
                  theme={selectedTheme}
                  slideId={selectedThemeSlide.id}
                  onUpdateTheme={updateTheme}
                  mediaLibrary={mediaLibrary}
                />
              ) : (
                <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                  Select a theme slide to begin editing.
                </div>
              )}
            </div>
          </div>
        ) : (
          <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
            Select a theme to begin editing.
          </div>
        )}
      </div>
      <ApplyThemeDialog
        open={applyThemeDialogOpen}
        onOpenChange={(open) => {
          setApplyThemeDialogOpen(open);
          if (!open) {
            setApplyThemeId(null);
          }
        }}
        presentation={applyPresentation}
        theme={applyThemeResolved}
        onApply={(mapping, options) => {
          if (!applyThemeResolved || !applyPresentation) return;
          void handleApplyThemeToPresentation(mapping, options);
        }}
      />
    </div>
  );
}

interface ThemeSlideListItemProps {
  slide: Slide;
  label: string;
  slideNumber: number;
  isSelected: boolean;
  isActive: boolean;
  presentation: Presentation | null;
  onSelect: (event: MouseEvent) => void;
  onContextSelect: () => void;
  onDuplicate: () => void;
  onDelete: () => void;
}

function ThemeSlideListItem({
  slide,
  label,
  slideNumber,
  isSelected,
  isActive,
  presentation,
  onSelect,
  onContextSelect,
  onDuplicate,
  onDelete,
}: ThemeSlideListItemProps) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({
    id: slide.id,
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
            <div className="flex-1" onClick={onSelect}>
              <SlideThumbnail
                slide={slide}
                isSelected={isSelected}
                showLabel={false}
                slideNumber={slideNumber}
                numberPlacement="overlay-left"
                showFooter={false}
                presentation={presentation ?? undefined}
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
                <DropdownMenuItem onClick={onDuplicate}>
                  <Copy className="mr-2 h-4 w-4" />
                  Duplicate
                </DropdownMenuItem>
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
        <ContextMenuItem onClick={onDuplicate}>
          <Copy className="mr-2 h-4 w-4" />
          Duplicate
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
