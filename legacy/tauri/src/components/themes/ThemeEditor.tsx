/**
 * ThemeEditor - edit a theme template with preview + inspector
 */

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { SlideInspector } from '@/components/editor/SlideInspector';
import { SlideCanvas } from '@/components/editor/SlideCanvas';
import type {
  AssetToolbarStore,
  SlideCanvasStore,
  SlideInspectorStore,
} from '@/lib/editor/slide-surface-store';
import type {
  Presentation,
  Slide,
  Layer,
  TextLayer,
  ThemeTemplate,
  ThemeTemplateSlide,
  LayerTransform,
  MediaEntry,
} from '@/lib/models';
import {
  createArrangement,
  createShapeLayer,
  createTextLayer,
  createWebLayer,
  defaultPrimaryTextStyle,
  defaultSlideTransition,
  getThemeSlideLayers,
  normalizeLayoutType,
} from '@/lib/models';

const THEME_TEXT_PLACEHOLDER = 'Sample Text';
const THEME_HEADING_PLACEHOLDER = 'Heading';

const cloneLayers = (layers: Layer[]) =>
  typeof structuredClone === 'function'
    ? (structuredClone(layers) as Layer[])
    : (JSON.parse(JSON.stringify(layers)) as Layer[]);

const toThemeSlide = (themeSlide: ThemeTemplateSlide): Slide => {
  const layers = cloneLayers(getThemeSlideLayers(themeSlide));
  return {
    id: themeSlide.id,
    type: 'blank',
    layoutType: themeSlide.layoutType,
    layers,
    mediaCues: Array.isArray(themeSlide.mediaCues) ? themeSlide.mediaCues : [],
    background: themeSlide.background,
    animations: {
      transition: { ...defaultSlideTransition },
      buildIn: [],
      buildOut: [],
    },
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  };
};

const buildThemePresentation = (
  slide: Slide,
  theme: ThemeTemplate,
  mediaLibrary?: MediaEntry[]
): Presentation => ({
  manifest: {
    formatVersion: '1.0.0',
    presentationId: theme.id,
    title: theme.name,
    createdAt: theme.createdAt,
    updatedAt: theme.updatedAt,
    aspectRatio: theme.aspectRatio,
    media: mediaLibrary ?? [],
    fonts: [],
  },
  slides: [slide],
  arrangement: createArrangement([slide]),
  themes: [],
});

interface ThemeEditorProps {
  theme: ThemeTemplate;
  slideId: string;
  onUpdateTheme: (themeId: string, updates: Partial<ThemeTemplate>) => void;
  mediaLibrary?: MediaEntry[];
}

interface ThemeSlideSurfaceOptions {
  selection: { slideIds: string[]; layerIds: string[] };
  presentation: Presentation | null;
  applyDraft: (updater: (slide: Slide) => Slide) => void;
  setLayerSelection: (layerIds: string[]) => void;
  updateLayers: (updates: { layerId: string; updates: Partial<Layer> }[]) => void;
  updateLayerTransform: (layerId: string, transform: Partial<LayerTransform>) => void;
  updateLayerTransforms: (updates: { layerId: string; transform: Partial<LayerTransform> }[]) => void;
  reorderLayer: (layerId: string, toIndex: number) => void;
  moveLayerBy: (layerId: string, delta: number) => void;
  moveLayerToEdge: (layerId: string, toFront: boolean) => void;
  addThemeTextLayer: (content?: string, style?: TextLayer['style']) => TextLayer;
  addThemeShapeLayer: (shapeType?: Parameters<typeof createShapeLayer>[0]) => Layer;
  addThemeWebLayer: (url: string) => Layer;
}

function useThemeSlideSurfaceStores({
  selection,
  presentation,
  applyDraft,
  setLayerSelection,
  updateLayers,
  updateLayerTransform,
  updateLayerTransforms,
  reorderLayer,
  moveLayerBy,
  moveLayerToEdge,
  addThemeTextLayer,
  addThemeShapeLayer,
  addThemeWebLayer,
}: ThemeSlideSurfaceOptions) {
  const inspectorStore = useMemo<SlideInspectorStore>(
    () => ({
      updateSlides: (_slideIds: string[], updates: Partial<Slide>) =>
        applyDraft((slide) => ({ ...slide, ...updates })),
      setSlidesBackground: (_slideIds: string[], background: Slide['background']) =>
        applyDraft((slide) => ({ ...slide, background })),
      setSlidesSection: () => {},
      setSlidesTransition: () => {},
      updateLayer: (_slideId: string, layerId: string, updates: Partial<Layer>) =>
        applyDraft((slide) => ({
          ...slide,
          layers: slide.layers.map((layer) =>
            layer.id === layerId ? ({ ...layer, ...updates } as Layer) : layer
          ),
        })),
      deleteLayer: (_slideId: string, layerId: string) =>
        applyDraft((slide) => ({
          ...slide,
          layers: slide.layers.filter((layer) => layer.id !== layerId),
        })),
      bringLayerForward: (_slideId: string, layerId: string) => moveLayerBy(layerId, 1),
      sendLayerBackward: (_slideId: string, layerId: string) => moveLayerBy(layerId, -1),
      bringLayerToFront: (_slideId: string, layerId: string) => moveLayerToEdge(layerId, true),
      sendLayerToBack: (_slideId: string, layerId: string) => moveLayerToEdge(layerId, false),
      selectLayer: (layerId: string | null) => setLayerSelection(layerId ? [layerId] : []),
      selectLayers: (layerIds: string[]) => setLayerSelection(layerIds),
      selection,
      presentation,
      filePath: null,
      pendingMedia: new Map<string, string>(),
      reorderLayer: (_slideId: string, layerId: string, toIndex: number) =>
        reorderLayer(layerId, toIndex),
      addBuildStep: () => {},
    }),
    [
      applyDraft,
      moveLayerBy,
      moveLayerToEdge,
      presentation,
      reorderLayer,
      selection,
      setLayerSelection,
    ]
  );

  const canvasStore = useMemo<SlideCanvasStore>(
    () => ({
      addTextLayer: (_slideId: string, content?: string) => addThemeTextLayer(content),
      updateLayer: (_slideId: string, layerId: string, updates: Partial<Layer>) =>
        applyDraft((slide) => ({
          ...slide,
          layers: slide.layers.map((layer) =>
            layer.id === layerId ? ({ ...layer, ...updates } as Layer) : layer
          ),
        })),
      updateLayers: (_slideId: string, updates) => updateLayers(updates),
      deleteLayer: (_slideId: string, layerId: string) =>
        applyDraft((slide) => ({
          ...slide,
          layers: slide.layers.filter((layer) => layer.id !== layerId),
        })),
      selection,
      selectLayer: (layerId) => setLayerSelection(layerId ? [layerId] : []),
      selectLayers: (layerIds) => setLayerSelection(layerIds),
      beginLayerTransform: () => {},
      updateLayerTransform: (_slideId: string, layerId: string, transform) =>
        updateLayerTransform(layerId, transform),
      updateLayerTransforms: (_slideId: string, updates) => updateLayerTransforms(updates),
      commitLayerTransform: () => {},
      bringLayerForward: (_slideId: string, layerId: string) => moveLayerBy(layerId, 1),
      sendLayerBackward: (_slideId: string, layerId: string) => moveLayerBy(layerId, -1),
      bringLayerToFront: (_slideId: string, layerId: string) => moveLayerToEdge(layerId, true),
      sendLayerToBack: (_slideId: string, layerId: string) => moveLayerToEdge(layerId, false),
    }),
    [
      addThemeTextLayer,
      applyDraft,
      moveLayerBy,
      moveLayerToEdge,
      selection,
      setLayerSelection,
      updateLayerTransform,
      updateLayerTransforms,
      updateLayers,
    ]
  );

  const toolbarStore = useMemo<AssetToolbarStore>(
    () => ({
      presentation,
      addTextLayer: (_slideId, content) => {
        addThemeTextLayer(content);
      },
      addShapeLayer: (_slideId, shapeType) => {
        addThemeShapeLayer(shapeType);
      },
      addWebLayer: (_slideId, url) => {
        addThemeWebLayer(url);
      },
      updateSlide: (_slideId, updates) => {
        if (updates.mediaCues) {
          applyDraft((slide) => ({ ...slide, mediaCues: updates.mediaCues }));
        }
      },
    }),
    [addThemeShapeLayer, addThemeTextLayer, addThemeWebLayer, applyDraft, presentation]
  );

  return { inspectorStore, canvasStore, toolbarStore };
}

export function ThemeEditor({ theme, slideId, onUpdateTheme, mediaLibrary }: ThemeEditorProps) {
  const themeSlide = useMemo(
    () => theme.slides.find((slide) => slide.id === slideId) ?? null,
    [theme.slides, slideId]
  );
  const [draftSlide, setDraftSlide] = useState<Slide | null>(() =>
    themeSlide ? toThemeSlide(themeSlide) : null
  );
  const [selectedLayerIds, setSelectedLayerIds] = useState<string[]>([]);
  const lastThemeKeyRef = useRef(`${theme.id}:${slideId}`);

  useEffect(() => {
    const nextKey = `${theme.id}:${slideId}`;
    if (lastThemeKeyRef.current === nextKey) return;
    lastThemeKeyRef.current = nextKey;
    setDraftSlide(themeSlide ? toThemeSlide(themeSlide) : null);
    setSelectedLayerIds([]);
  }, [theme, themeSlide, slideId]);

  const commitThemeFromSlide = useCallback(
    (nextSlide: Slide) => {
      const nextSlides = theme.slides.map((slide) =>
        slide.id === slideId
          ? {
              ...slide,
              background: nextSlide.background ?? slide.background,
              layers: cloneLayers(nextSlide.layers),
              mediaCues: Array.isArray(nextSlide.mediaCues) ? nextSlide.mediaCues : [],
            }
          : slide
      );
      onUpdateTheme(theme.id, { slides: nextSlides });
    },
    [theme, slideId, onUpdateTheme]
  );

  const handleSlideNameChange = useCallback(
    (name: string) => {
      const nextSlides = theme.slides.map((slide) =>
        slide.id === slideId ? { ...slide, name } : slide
      );
      onUpdateTheme(theme.id, { slides: nextSlides });
    },
    [theme, slideId, onUpdateTheme]
  );

  const handleLayoutTypeChange = useCallback(
    (value: string) => {
      if (!themeSlide) return;
      const resolvedValue = value === '_none' ? '' : value;
      const nextValue = resolvedValue.trim() ? resolvedValue : undefined;
      const nextKey = normalizeLayoutType(nextValue);
      const nextSlides = theme.slides.map((slide) => {
        if (slide.id === slideId) {
          return { ...slide, layoutType: nextValue };
        }
        if (!nextKey) return slide;
        const existingKey = normalizeLayoutType(slide.layoutType);
        if (existingKey && existingKey === nextKey) {
          return { ...slide, layoutType: undefined };
        }
        return slide;
      });
      onUpdateTheme(theme.id, { slides: nextSlides });
    },
    [onUpdateTheme, slideId, theme, themeSlide]
  );

  const applyDraft = useCallback(
    (updater: (slide: Slide) => Slide) => {
      setDraftSlide((prev) => {
        if (!prev) return prev;
        const next = updater(prev);
        commitThemeFromSlide(next);
        return next;
      });
    },
    [commitThemeFromSlide]
  );

  const themePresentation = useMemo(
    () => (draftSlide ? buildThemePresentation(draftSlide, theme, mediaLibrary) : null),
    [draftSlide, theme, mediaLibrary]
  );

  const selection = useMemo(
    () => (draftSlide ? { slideIds: [draftSlide.id], layerIds: selectedLayerIds } : {
      slideIds: [],
      layerIds: [],
    }),
    [draftSlide, selectedLayerIds]
  );

  const setLayerSelection = useCallback((layerIds: string[]) => {
    setSelectedLayerIds(layerIds);
  }, []);

  const updateLayers = useCallback(
    (updates: { layerId: string; updates: Partial<Layer> }[]) => {
      if (updates.length === 0) return;
      applyDraft((slide) => {
        const updatesById = new Map(updates.map((item) => [item.layerId, item.updates]));
        return {
          ...slide,
          layers: slide.layers.map((layer) => {
            const layerUpdates = updatesById.get(layer.id);
            return layerUpdates ? ({ ...layer, ...layerUpdates } as Layer) : layer;
          }),
        };
      });
    },
    [applyDraft]
  );

  const updateLayerTransform = useCallback(
    (layerId: string, transform: Partial<LayerTransform>) => {
      applyDraft((slide) => ({
        ...slide,
        layers: slide.layers.map((layer) =>
          layer.id === layerId
            ? ({ ...layer, transform: { ...layer.transform, ...transform } } as Layer)
            : layer
        ),
      }));
    },
    [applyDraft]
  );

  const updateLayerTransforms = useCallback(
    (updates: { layerId: string; transform: Partial<LayerTransform> }[]) => {
      if (updates.length === 0) return;
      applyDraft((slide) => {
        const updatesById = new Map(updates.map((item) => [item.layerId, item.transform]));
        return {
          ...slide,
          layers: slide.layers.map((layer) => {
            const layerUpdates = updatesById.get(layer.id);
            return layerUpdates
              ? ({ ...layer, transform: { ...layer.transform, ...layerUpdates } } as Layer)
              : layer;
          }),
        };
      });
    },
    [applyDraft]
  );

  const reorderLayer = useCallback(
    (layerId: string, toIndex: number) =>
      applyDraft((slide) => {
        const fromIndex = slide.layers.findIndex((layer) => layer.id === layerId);
        if (fromIndex === -1 || fromIndex === toIndex) return slide;
        const nextLayers = [...slide.layers];
        const [moved] = nextLayers.splice(fromIndex, 1);
        nextLayers.splice(toIndex, 0, moved);
        return { ...slide, layers: nextLayers };
      }),
    [applyDraft]
  );

  const moveLayerBy = useCallback(
    (layerId: string, delta: number) => {
      applyDraft((slide) => {
        const fromIndex = slide.layers.findIndex((layer) => layer.id === layerId);
        if (fromIndex === -1) return slide;
        const toIndex = Math.max(
          0,
          Math.min(slide.layers.length - 1, fromIndex + delta)
        );
        if (fromIndex === toIndex) return slide;
        const nextLayers = [...slide.layers];
        const [moved] = nextLayers.splice(fromIndex, 1);
        nextLayers.splice(toIndex, 0, moved);
        return { ...slide, layers: nextLayers };
      });
    },
    [applyDraft]
  );

  const moveLayerToEdge = useCallback(
    (layerId: string, toFront: boolean) => {
      applyDraft((slide) => {
        const fromIndex = slide.layers.findIndex((layer) => layer.id === layerId);
        if (fromIndex === -1) return slide;
        const nextLayers = [...slide.layers];
        const [moved] = nextLayers.splice(fromIndex, 1);
        const toIndex = toFront ? nextLayers.length : 0;
        nextLayers.splice(toIndex, 0, moved);
        return { ...slide, layers: nextLayers };
      });
    },
    [applyDraft]
  );

  const addThemeTextLayer = useCallback(
    (content?: string, style?: TextLayer['style']) => {
      const nextLayer = createTextLayer(content ?? THEME_TEXT_PLACEHOLDER, style ? { style } : {});
      applyDraft((slide) => ({ ...slide, layers: [...slide.layers, nextLayer] }));
      setLayerSelection([nextLayer.id]);
      return nextLayer;
    },
    [applyDraft, setLayerSelection]
  );

  const addThemeShapeLayer = useCallback(
    (shapeType?: Parameters<typeof createShapeLayer>[0]) => {
      const nextLayer = createShapeLayer(shapeType);
      applyDraft((slide) => ({ ...slide, layers: [...slide.layers, nextLayer] }));
      setLayerSelection([nextLayer.id]);
      return nextLayer;
    },
    [applyDraft, setLayerSelection]
  );

  const addThemeWebLayer = useCallback(
    (url: string) => {
      const nextLayer = createWebLayer(url);
      applyDraft((slide) => ({ ...slide, layers: [...slide.layers, nextLayer] }));
      setLayerSelection([nextLayer.id]);
      return nextLayer;
    },
    [applyDraft, setLayerSelection]
  );

  const { inspectorStore, canvasStore, toolbarStore } = useThemeSlideSurfaceStores({
    selection,
    presentation: themePresentation,
    applyDraft,
    setLayerSelection,
    updateLayers,
    updateLayerTransform,
    updateLayerTransforms,
    reorderLayer,
    moveLayerBy,
    moveLayerToEdge,
    addThemeTextLayer,
    addThemeShapeLayer,
    addThemeWebLayer,
  });

  const handleAddTextLayer = useCallback(() => {
    addThemeTextLayer(THEME_TEXT_PLACEHOLDER);
  }, [addThemeTextLayer]);

  const handleAddHeadingLayer = useCallback(() => {
    const headingStyle = {
      ...defaultPrimaryTextStyle,
      font: {
        ...defaultPrimaryTextStyle.font,
        size: 96,
        weight: 800,
        italic: false,
        lineHeight: 1.1,
        letterSpacing: -2,
      },
    };
    addThemeTextLayer(THEME_HEADING_PLACEHOLDER, headingStyle);
  }, [addThemeTextLayer]);

  if (!draftSlide || !themePresentation) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        Select a theme slide to begin editing.
      </div>
    );
  }

  return (
    <div className="flex h-full min-h-0">
      <div className="flex-1 overflow-hidden">
        <SlideCanvas
          slide={draftSlide}
          aspectRatio={theme.aspectRatio}
          presentation={themePresentation}
          presentationPath={null}
          pendingMedia={new Map<string, string>()}
          toolbarStore={toolbarStore}
          allowMediaImport={false}
          store={canvasStore}
          toolbarMode="theme"
          onAddTextLayer={handleAddTextLayer}
          onAddHeadingLayer={handleAddHeadingLayer}
        />
      </div>

      <div className="w-80 shrink-0 border-l bg-muted/30 flex min-h-0 flex-col">
        <div className="flex-1 min-h-0">
          <SlideInspector
            slide={draftSlide}
            mode="theme"
            store={inspectorStore}
            themeSlideName={themeSlide?.name ?? ''}
            onThemeSlideNameChange={handleSlideNameChange}
            themeSlideLayoutType={themeSlide?.layoutType ?? '_none'}
            onThemeSlideLayoutTypeChange={handleLayoutTypeChange}
          />
        </div>
      </div>
    </div>
  );
}
