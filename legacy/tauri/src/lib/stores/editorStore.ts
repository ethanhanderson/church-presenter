/**
 * Editor Store - manages the current presentation being edited
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import { v4 as uuid } from 'uuid';
import type {
  Presentation,
  Slide,
  Theme,
  ThemeTemplate,
  ThemeTemplateSlide,
  Layer,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
  LayerTransform,
  TextStyle,
  ShapeType,
  SongSection,
  SlideType,
  Background,
  MediaEntry,
  SlideTransition,
  BuildStep,
  MediaCueTarget,
  LegacyMediaCueTarget,
  LayerStroke,
} from '../models';
import { LEGACY_CUE_TARGET_MAP } from '../models';
import {
  createPresentation,
  createSlide,
  createTextLayer,
  createShapeLayer,
  createMediaLayer,
  createWebLayer,
  createArrangement,
  createTheme,
  defaultLayerTransform,
  defaultShapeStyle,
  defaultPrimaryTextStyle,
  createLayerFill,
  createLayerStroke,
  defaultSlideTransition,
  deriveLayoutTypeFromSlide,
  normalizeLayoutType,
  applyThemeSlideToSlideInPlace,
} from '../models';
import {
  importFontFiles,
  openBundle,
  saveBundle,
  type FontFileRef,
  type MediaFileRef,
  type SystemFontInfo,
} from '../tauri-api';
import { getDocumentsDataDirPath } from '../services/appDataService';

// ============================================================================
// Auto-Save Status Types
// ============================================================================

export type AutoSaveStatus = 'idle' | 'pending' | 'saving' | 'saved' | 'error' | 'conflict';

export interface AutoSaveState {
  status: AutoSaveStatus;
  lastSaved: string | null;
  lastError: string | null;
}

const isAbsolutePath = (path: string) => /^[a-zA-Z]:[\\/]|^\//.test(path);

const resolvePresentationPath = async (path: string) => {
  if (isAbsolutePath(path)) return path;
  const baseDir = await getDocumentsDataDirPath();
  return `${baseDir}/${path}`.replace(/\\/g, '/');
};

const DEFAULT_COLOR_LIBRARY = ['#000000', '#111827', '#ffffff', '#ef4444'];
const DEFAULT_STROKE_LIBRARY = [
  createLayerStroke('#111827', 1, 1, 'inside', 'all'),
  createLayerStroke('#ffffff', 2, 1, 'inside', 'all'),
  createLayerStroke('#ef4444', 3, 1, 'inside', 'all'),
  createLayerStroke('#3b82f6', 4, 0.9, 'inside', 'all'),
];

const mergeTextStyle = (
  base?: TextStyle,
  overrides?: Partial<TextStyle>
): TextStyle | undefined => {
  if (!base && !overrides) return undefined;
  return {
    ...(base ?? {}),
    ...(overrides ?? {}),
    font: { ...(base?.font ?? {}), ...(overrides?.font ?? {}) },
    shadow: { ...(base?.shadow ?? {}), ...(overrides?.shadow ?? {}) },
    outline: { ...(base?.outline ?? {}), ...(overrides?.outline ?? {}) },
  } as TextStyle;
};

const scaleTransform = (
  transform: LayerTransform,
  scaleX: number,
  scaleY: number
): LayerTransform => {
  const radiusScale = Math.min(scaleX, scaleY);
  return {
    ...transform,
    x: transform.x * scaleX,
    y: transform.y * scaleY,
    width: transform.width * scaleX,
    height: transform.height * scaleY,
    cornerRadius: (transform.cornerRadius ?? 0) * radiusScale,
    cornerRadiusTopLeft: transform.cornerRadiusTopLeft
      ? transform.cornerRadiusTopLeft * radiusScale
      : undefined,
    cornerRadiusTopRight: transform.cornerRadiusTopRight
      ? transform.cornerRadiusTopRight * radiusScale
      : undefined,
    cornerRadiusBottomRight: transform.cornerRadiusBottomRight
      ? transform.cornerRadiusBottomRight * radiusScale
      : undefined,
    cornerRadiusBottomLeft: transform.cornerRadiusBottomLeft
      ? transform.cornerRadiusBottomLeft * radiusScale
      : undefined,
  };
};

const scaleTextStyle = (style: TextStyle, scale: number): TextStyle => ({
  ...style,
  font: {
    ...style.font,
    size: style.font.size * scale,
    letterSpacing: (style.font.letterSpacing ?? 0) * scale,
  },
  shadow: style.shadow
    ? {
      ...style.shadow,
      offsetX: style.shadow.offsetX * scale,
      offsetY: style.shadow.offsetY * scale,
      blur: style.shadow.blur * scale,
    }
    : style.shadow,
  outline: style.outline
    ? {
      ...style.outline,
      width: style.outline.width * scale,
    }
    : style.outline,
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

const getAutoLayoutType = (slide: Slide) => deriveLayoutTypeFromSlide(slide);

const shouldUpdateLayoutType = (slide: Slide, previousAuto?: string) => {
  const current = slide.layoutType?.trim();
  if (!current) return true;
  const currentKey = normalizeLayoutType(current);
  const previousKey = normalizeLayoutType(previousAuto);
  return !!previousKey && currentKey === previousKey;
};

const resolveAspectRatioFromSize = (
  width: number,
  height: number
): '16:9' | '4:3' | '16:10' => {
  const ratio = width / height;
  const candidates = [
    { value: '16:9' as const, ratio: 16 / 9 },
    { value: '4:3' as const, ratio: 4 / 3 },
    { value: '16:10' as const, ratio: 16 / 10 },
  ];
  let closest = candidates[0];
  let bestDiff = Math.abs(ratio - closest.ratio);
  for (const candidate of candidates.slice(1)) {
    const diff = Math.abs(ratio - candidate.ratio);
    if (diff < bestDiff) {
      bestDiff = diff;
      closest = candidate;
    }
  }
  return closest.value;
};

const ensureValidPresentation = (presentation: Presentation) => {
  // Normalize manifest for legacy/partial data
  if (!Array.isArray(presentation.manifest.media)) {
    // Some older bundles may omit the media manifest entirely
    presentation.manifest.media = [];
  }

  if (!Array.isArray(presentation.manifest.fonts)) {
    // Some older bundles may omit the font manifest entirely
    presentation.manifest.fonts = [];
  }

  if (!presentation.slides || presentation.slides.length === 0) {
    presentation.slides = [createSlide('blank')];
  }

  if (!presentation.themes || presentation.themes.length === 0) {
    const theme = createTheme('Default Theme');
    presentation.themes = [theme];
    presentation.manifest.themeId = theme.id;
  }

  if (
    (!presentation.manifest.themeId && presentation.themes.length > 0) ||
    !presentation.themes.some((theme) => theme.id === presentation.manifest.themeId)
  ) {
    presentation.manifest.themeId = presentation.themes[0]!.id;
  }

  const activeTheme =
    presentation.themes.find((theme) => theme.id === presentation.manifest.themeId) ||
    presentation.themes[0]!;
  if (!presentation.manifest.aspectRatio) {
    presentation.manifest.aspectRatio = activeTheme?.aspectRatio ?? '16:9';
  }
  const manifestSlideSize = presentation.manifest.slideSize;
  if (
    !manifestSlideSize ||
    !Number.isFinite(manifestSlideSize.width) ||
    !Number.isFinite(manifestSlideSize.height) ||
    manifestSlideSize.width <= 0 ||
    manifestSlideSize.height <= 0
  ) {
    presentation.manifest.slideSize = getBaseSlideSize(presentation.manifest.aspectRatio);
  }

  // Normalize slides/layers for legacy or partial data
  presentation.slides = presentation.slides.map((slide) => {
    const normalizedLayers = Array.isArray(slide.layers) ? slide.layers : [];
    slide.layers = (normalizedLayers as Layer[]).map((layer) => {
      const baseLayer = {
        locked: layer.locked ?? false,
        visible: layer.visible ?? true,
        name: layer.name || layer.type,
        transform: { ...defaultLayerTransform, ...layer.transform },
      };

      switch (layer.type) {
        case 'text':
          return {
            ...layer,
            ...baseLayer,
            content: layer.content ?? '',
            textFit: layer.textFit ?? 'auto',
            padding: layer.padding ?? 2,
            fills:
              layer.fills && layer.fills.length > 0
                ? layer.fills
                : [
                  createLayerFill(
                    layer.style?.color ?? defaultPrimaryTextStyle.color,
                    1
                  ),
                ],
          };
        case 'shape':
          return {
            ...layer,
            ...baseLayer,
            shapeType: layer.shapeType || 'rectangle',
            style: { ...defaultShapeStyle, ...layer.style },
            fills:
              layer.fills && layer.fills.length > 0
                ? layer.fills
                : [
                  createLayerFill(
                    layer.style?.fill ?? defaultShapeStyle.fill,
                    layer.style?.fillOpacity ?? defaultShapeStyle.fillOpacity
                  ),
                ],
            strokes:
              layer.strokes && layer.strokes.length > 0
                ? layer.strokes.map((stroke) => ({
                  id: stroke.id ?? uuid(),
                  color: stroke.color ?? defaultShapeStyle.stroke,
                  opacity: stroke.opacity ?? defaultShapeStyle.strokeOpacity,
                  width: stroke.width ?? defaultShapeStyle.strokeWidth,
                  position: stroke.position ?? 'inside',
                  sides: stroke.sides ?? 'all',
                  customSides: stroke.customSides,
                  enabled: stroke.enabled ?? true,
                }))
                : [
                  createLayerStroke(
                    layer.style?.stroke ?? defaultShapeStyle.stroke,
                    layer.style?.strokeWidth ?? defaultShapeStyle.strokeWidth,
                    layer.style?.strokeOpacity ?? defaultShapeStyle.strokeOpacity
                  ),
                ],
          };
        case 'media':
          return {
            ...layer,
            ...baseLayer,
            fit: layer.fit ?? 'contain',
            loop: layer.loop ?? (layer.mediaType === 'video'),
            muted: layer.muted ?? true,
            autoplay: layer.autoplay ?? true,
          };
        case 'web':
          return {
            ...layer,
            ...baseLayer,
            zoom: layer.zoom ?? 1,
            interactive: layer.interactive ?? false,
            refreshInterval: layer.refreshInterval ?? 0,
          };
        default: {
          const safeLayer = (typeof layer === 'object' && layer ? layer : {}) as Layer;
          return { ...safeLayer, ...baseLayer } as Layer;
        }
      }
    });

    const trimmedLayoutType = slide.layoutType?.trim();
    slide.layoutType = trimmedLayoutType || undefined;
    if (!slide.layoutType) {
      slide.layoutType = getAutoLayoutType(slide);
    }

    if (!slide.animations) {
      slide.animations = {
        transition: { ...defaultSlideTransition },
        buildIn: [],
        buildOut: [],
      };
    } else if (!slide.animations.transition) {
      slide.animations.transition = { ...defaultSlideTransition };
    }

    if (!Array.isArray(slide.mediaCues)) {
      slide.mediaCues = [];
    }

    // Migrate legacy media cue targets to new targets
    slide.mediaCues = slide.mediaCues.map((cue) => {
      const target = cue.target as MediaCueTarget | LegacyMediaCueTarget;
      // Check if this is a legacy target that needs migration
      if (target in LEGACY_CUE_TARGET_MAP && target !== 'audio') {
        const legacyTarget = target as LegacyMediaCueTarget;
        return {
          ...cue,
          target: LEGACY_CUE_TARGET_MAP[legacyTarget],
        };
      }
      return cue;
    });

    const mergedBackground =
      slide.overrides?.background ?? slide.background ?? activeTheme?.background;
    if (mergedBackground) {
      slide.background = mergedBackground;
    }

    slide.layers = slide.layers.map((layer) => {
      if (layer.type !== 'text') return layer;
      const baseStyle = mergeTextStyle(activeTheme?.primaryText, slide.overrides?.primaryText);
      const finalStyle = mergeTextStyle(baseStyle, layer.style) ?? baseStyle;
      if (!finalStyle) return layer;
      const fills = layer.fills && layer.fills.length > 0
        ? layer.fills
        : [createLayerFill(finalStyle.color ?? defaultPrimaryTextStyle.color, 1)];
      return {
        ...layer,
        style: finalStyle,
        fills,
      };
    });

    if (slide.overrides) {
      slide.overrides = undefined;
    }

    return slide;
  });

  if (!presentation.arrangement?.order?.length) {
    presentation.arrangement = createArrangement(presentation.slides);
  }

  return presentation;
};

interface EditorSelection {
  slideIds: string[];
  layerIds: string[];
}

interface UndoEntry {
  presentation: Presentation;
  selection: EditorSelection;
}

interface EditorState {
  // Current state
  presentation: Presentation | null;
  filePath: string | null;
  isDirty: boolean;
  selection: EditorSelection;
  activeSlideId: string | null;

  // Transform editing state (for continuous drag without multiple undos)
  isTransforming: boolean;

  // Undo/redo
  undoStack: UndoEntry[];
  redoStack: UndoEntry[];
  maxUndoLevels: number;

  // Pending media (not yet saved to bundle)
  pendingMedia: Map<string, string>; // mediaId -> sourcePath

  // Pending fonts (not yet saved to bundle)
  pendingFonts: Map<string, string>; // fontId -> sourcePath

  // Recent fonts (most recently used families)
  recentFonts: string[];

  // Auto-save state
  autoSave: AutoSaveState;

  // Color library (shared in editor)
  colorLibrary: string[];

  // Stroke library (shared in editor)
  strokeLibrary: LayerStroke[];

  // Actions
  newPresentation: (title: string) => void;
  openPresentation: (path: string) => Promise<void>;
  savePresentation: (path?: string) => Promise<void>;
  closePresentation: () => void;

  // Auto-save actions
  setAutoSaveStatus: (status: AutoSaveStatus, error?: string) => void;
  markSaved: (filePath: string) => void;
  remapFilePath: (oldBase: string, newBase: string) => void;

  // Color library actions
  setColorLibrary: (colors: string[]) => void;
  addColorToLibrary: (color: string) => void;
  removeColorFromLibrary: (color: string) => void;

  // Stroke library actions
  setStrokeLibrary: (strokes: LayerStroke[]) => void;
  addStrokeToLibrary: (stroke: LayerStroke) => void;
  removeStrokeFromLibrary: (strokeId: string) => void;

  // Selection
  selectSlide: (slideId: string | null) => void;
  selectSlides: (slideIds: string[], activeSlideId?: string | null) => void;
  selectLayer: (layerId: string | null) => void;
  selectLayers: (layerIds: string[]) => void;
  clearSelection: () => void;

  // Slides
  addSlide: (type: SlideType, options?: { section?: SongSection; afterSlideId?: string }) => Slide;
  duplicateSlide: (slideId: string) => Slide | null;
  duplicateSlides: (slideIds: string[]) => Slide[];
  deleteSlide: (slideId: string) => void;
  deleteSlides: (slideIds: string[]) => void;
  updateSlide: (slideId: string, updates: Partial<Slide>) => void;
  updateSlides: (slideIds: string[], updates: Partial<Slide>) => void;
  moveSlide: (slideId: string, toIndex: number) => void;
  setSlideSection: (slideId: string, section?: SongSection, label?: string) => void;
  setSlidesSection: (slideIds: string[], section?: SongSection, label?: string) => void;
  setSlideTransition: (slideId: string, transition: Partial<SlideTransition>) => void;
  setSlidesTransition: (slideIds: string[], transition: Partial<SlideTransition>) => void;

  // Layers (replacing text blocks)
  addTextLayer: (slideId: string, content?: string) => TextLayer | null;
  addShapeLayer: (slideId: string, shapeType?: ShapeType) => ShapeLayer | null;
  addMediaLayer: (slideId: string, mediaId: string, mediaType: 'image' | 'video') => MediaLayer | null;
  addWebLayer: (slideId: string, url: string) => WebLayer | null;
  updateLayer: (slideId: string, layerId: string, updates: Partial<Layer>) => void;
  updateLayers: (slideId: string, updates: { layerId: string; updates: Partial<Layer> }[]) => void;
  deleteLayer: (slideId: string, layerId: string) => void;

  // Layer ordering
  reorderLayer: (slideId: string, layerId: string, toIndex: number) => void;
  bringLayerForward: (slideId: string, layerId: string) => void;
  sendLayerBackward: (slideId: string, layerId: string) => void;
  bringLayerToFront: (slideId: string, layerId: string) => void;
  sendLayerToBack: (slideId: string, layerId: string) => void;

  // Layer transform helpers (for drag operations)
  beginLayerTransform: () => void;
  updateLayerTransform: (slideId: string, layerId: string, transform: Partial<LayerTransform>) => void;
  updateLayerTransforms: (
    slideId: string,
    updates: { layerId: string; transform: Partial<LayerTransform> }[]
  ) => void;
  commitLayerTransform: () => void;

  // Build animations
  addBuildStep: (slideId: string, step: Omit<BuildStep, 'id'>) => void;
  updateBuildStep: (slideId: string, stepId: string, updates: Partial<BuildStep>) => void;
  deleteBuildStep: (slideId: string, stepId: string) => void;
  reorderBuildStep: (slideId: string, stepId: string, toIndex: number) => void;

  // Backgrounds
  setSlideBackground: (slideId: string, background: Background) => void;
  setSlidesBackground: (slideIds: string[], background: Background) => void;
  setPresentationAspectRatio: (aspectRatio: '16:9' | '4:3' | '16:10') => void;
  setPresentationResolution: (size: { width: number; height: number }) => void;

  // Themes
  applyTheme: (themeId: string) => void;
  applyThemeTemplateToSlides: (
    slideIds: string[],
    theme: ThemeTemplate,
    options?: { scaleMode?: 'none' | 'fit' }
  ) => void;
  applyThemeSlideToSlides: (
    slideIds: string[],
    theme: ThemeTemplate,
    themeSlide: ThemeTemplateSlide,
    options?: { scaleMode?: 'none' | 'fit' }
  ) => void;
  applyThemeSlideMappings: (
    slideThemeMap: Record<string, string>,
    theme: ThemeTemplate,
    options?: { scaleMode?: 'none' | 'fit' }
  ) => void;
  saveThemeFromSlide: (slideId: string, themeName: string) => Theme | null;
  addTheme: (theme: Theme) => void;
  updateTheme: (themeId: string, updates: Partial<Theme>) => void;
  deleteTheme: (themeId: string) => void;

  // Media
  addMedia: (entry: MediaEntry, sourcePath: string) => void;
  removeMedia: (mediaId: string) => void;

  // Fonts
  ensureFontFamilyBundled: (family: string, systemFonts: SystemFontInfo[]) => Promise<void>;
  trackRecentFont: (family: string) => void;

  // Undo/Redo
  undo: () => void;
  redo: () => void;
  pushUndo: () => void;

  // Metadata
  updateTitle: (title: string) => void;
  linkExternalSong: (songId: string, groupId: string) => void;
  unlinkExternalSong: () => void;
}

export const useEditorStore = create<EditorState>()(
  immer((set, get) => ({
    presentation: null,
    filePath: null,
    isDirty: false,
    selection: { slideIds: [], layerIds: [] },
    activeSlideId: null,
    isTransforming: false,
    undoStack: [],
    redoStack: [],
    maxUndoLevels: 50,
    pendingMedia: new Map(),
    pendingFonts: new Map(),
    recentFonts: [],
    autoSave: {
      status: 'idle',
      lastSaved: null,
      lastError: null,
    },
    colorLibrary: DEFAULT_COLOR_LIBRARY,
    strokeLibrary: DEFAULT_STROKE_LIBRARY,

    newPresentation: (title: string) => {
      const presentation = createPresentation(title);
      set((state) => {
        state.presentation = presentation;
        state.filePath = null;
        state.isDirty = true;
        state.selection = { slideIds: [], layerIds: [] };
        state.activeSlideId = presentation.slides[0]?.id || null;
        state.undoStack = [];
        state.redoStack = [];
        state.pendingMedia = new Map();
        state.pendingFonts = new Map();
        state.autoSave = { status: 'pending', lastSaved: null, lastError: null };
      });
    },

    openPresentation: async (path: string) => {
      let resolvedPath = path;
      let presentation: Presentation;

      try {
        presentation = await openBundle(path);
      } catch (error) {
        const candidate = await resolvePresentationPath(path);
        if (candidate === path) {
          throw error;
        }
        presentation = await openBundle(candidate);
        resolvedPath = candidate;
      }

      presentation = ensureValidPresentation(presentation);

      set((state) => {
        state.presentation = presentation;
        state.filePath = resolvedPath;
        state.isDirty = false;
        state.selection = { slideIds: [], layerIds: [] };
        state.activeSlideId = presentation.slides[0]?.id || null;
        state.undoStack = [];
        state.redoStack = [];
        state.pendingMedia = new Map();
        state.pendingFonts = new Map();
        state.autoSave = {
          status: 'idle',
          lastSaved: presentation.manifest.updatedAt,
          lastError: null,
        };
      });
    },

    savePresentation: async (path?: string) => {
      const { presentation, filePath, pendingMedia, pendingFonts } = get();
      if (!presentation) return;

      const savePath = path || filePath;
      if (!savePath) {
        throw new Error('No file path specified');
      }

      set((state) => {
        state.autoSave.status = 'saving';
      });

      try {
        // Update timestamps (avoid mutating potentially frozen state)
        const updatedAt = new Date().toISOString();
        const updatedPresentation: Presentation = {
          ...presentation,
          manifest: {
            ...presentation.manifest,
            updatedAt,
            media: Array.isArray(presentation.manifest.media) ? presentation.manifest.media : [],
            fonts: Array.isArray(presentation.manifest.fonts) ? presentation.manifest.fonts : [],
          },
        };

        // Build media refs for pending media
        const mediaRefs: MediaFileRef[] = [];
        for (const media of updatedPresentation.manifest.media) {
          const sourcePath = pendingMedia.get(media.id);
          if (sourcePath) {
            mediaRefs.push({
              id: media.id,
              source_path: sourcePath,
              bundle_path: media.path,
            });
          } else if (filePath && savePath === filePath) {
            mediaRefs.push({
              id: media.id,
              source_path: `bundle:${media.path}`,
              bundle_path: media.path,
            });
          }
        }

        // Build font refs for pending fonts
        const fontRefs: FontFileRef[] = [];
        for (const font of updatedPresentation.manifest.fonts) {
          const sourcePath = pendingFonts.get(font.id);
          if (sourcePath) {
            fontRefs.push({
              id: font.id,
              source_path: sourcePath,
              bundle_path: font.path,
            });
          } else if (filePath && savePath === filePath) {
            fontRefs.push({
              id: font.id,
              source_path: `bundle:${font.path}`,
              bundle_path: font.path,
            });
          }
        }

        await saveBundle(savePath, updatedPresentation, mediaRefs, fontRefs);

        set((state) => {
          state.presentation = updatedPresentation;
          state.filePath = savePath;
          state.isDirty = false;
          state.pendingMedia = new Map(); // Clear pending after save
          state.pendingFonts = new Map();
          state.autoSave = {
            status: 'saved',
            lastSaved: new Date().toISOString(),
            lastError: null,
          };
        });
      } catch (error) {
        const errorMessage = error instanceof Error ? error.message : String(error);
        set((state) => {
          state.autoSave.status = 'error';
          state.autoSave.lastError = errorMessage;
        });
        throw error;
      }
    },

    closePresentation: () => {
      set((state) => {
        state.presentation = null;
        state.filePath = null;
        state.isDirty = false;
        state.selection = { slideIds: [], layerIds: [] };
        state.activeSlideId = null;
        state.undoStack = [];
        state.redoStack = [];
        state.pendingMedia = new Map();
        state.pendingFonts = new Map();
        state.autoSave = {
          status: 'idle',
          lastSaved: null,
          lastError: null,
        };
      });
    },

    selectSlide: (slideId: string | null) => {
      set((state) => {
        state.activeSlideId = slideId;
        state.selection.slideIds = slideId ? [slideId] : [];
        state.selection.layerIds = [];
      });
    },

    selectSlides: (slideIds: string[], activeSlideId?: string | null) => {
      set((state) => {
        state.selection.slideIds = slideIds;
        state.activeSlideId = activeSlideId ?? slideIds[0] ?? null;
        state.selection.layerIds = [];
      });
    },

    selectLayer: (layerId: string | null) => {
      set((state) => {
        state.selection.layerIds = layerId ? [layerId] : [];
      });
    },

    selectLayers: (layerIds: string[]) => {
      set((state) => {
        state.selection.layerIds = layerIds;
      });
    },

    clearSelection: () => {
      set((state) => {
        state.selection = { slideIds: [], layerIds: [] };
      });
    },

    addSlide: (type: SlideType, options?: { section?: SongSection; afterSlideId?: string }) => {
      get().pushUndo();

      const slide = createSlide(type, '', {
        section: options?.section,
        sectionLabel: options?.section,
      });

      set((state) => {
        if (!state.presentation) return;

        const afterIndex = options?.afterSlideId
          ? state.presentation.slides.findIndex((s) => s.id === options.afterSlideId)
          : state.presentation.slides.length - 1;

        state.presentation.slides.splice(afterIndex + 1, 0, slide);
        state.presentation.arrangement = createArrangement(state.presentation.slides);
        state.activeSlideId = slide.id;
        state.selection.slideIds = [slide.id];
        state.isDirty = true;
      });

      return slide;
    },

    duplicateSlide: (slideId: string) => {
      get().pushUndo();

      const { presentation } = get();
      if (!presentation) return null;

      const original = presentation.slides.find((s) => s.id === slideId);
      if (!original) return null;

      const duplicate: Slide = {
        ...JSON.parse(JSON.stringify(original)),
        id: uuid(),
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      };

      set((state) => {
        if (!state.presentation) return;

        const index = state.presentation.slides.findIndex((s) => s.id === slideId);
        state.presentation.slides.splice(index + 1, 0, duplicate);
        state.presentation.arrangement = createArrangement(state.presentation.slides);
        state.activeSlideId = duplicate.id;
        state.selection.slideIds = [duplicate.id];
        state.isDirty = true;
      });

      return duplicate;
    },

    duplicateSlides: (slideIds: string[]) => {
      const { presentation } = get();
      if (!presentation || slideIds.length === 0) return [];

      const uniqueIds = Array.from(new Set(slideIds));
      const slidesToDuplicate = presentation.slides.filter((slide) => uniqueIds.includes(slide.id));
      if (slidesToDuplicate.length === 0) return [];

      get().pushUndo();

      const duplicates: Slide[] = [];
      set((state) => {
        if (!state.presentation) return;

        slidesToDuplicate.forEach((slide) => {
          const duplicate: Slide = {
            ...JSON.parse(JSON.stringify(slide)),
            id: uuid(),
            createdAt: new Date().toISOString(),
            updatedAt: new Date().toISOString(),
          };
          const index = state.presentation.slides.findIndex((s) => s.id === slide.id);
          if (index !== -1) {
            state.presentation.slides.splice(index + 1, 0, duplicate);
            duplicates.push(duplicate);
          }
        });

        if (duplicates.length > 0) {
          state.presentation.arrangement = createArrangement(state.presentation.slides);
          state.activeSlideId = duplicates[0]!.id;
          state.selection.slideIds = duplicates.map((slide) => slide.id);
          state.isDirty = true;
        }
      });

      return duplicates;
    },

    deleteSlide: (slideId: string) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const index = state.presentation.slides.findIndex((s) => s.id === slideId);
        if (index === -1) return;

        state.presentation.slides.splice(index, 1);
        state.presentation.arrangement = createArrangement(state.presentation.slides);

        // Select adjacent slide
        if (state.activeSlideId === slideId) {
          const newIndex = Math.min(index, state.presentation.slides.length - 1);
          state.activeSlideId = state.presentation.slides[newIndex]?.id || null;
          state.selection.slideIds = state.activeSlideId ? [state.activeSlideId] : [];
        }

        state.isDirty = true;
      });
    },

    deleteSlides: (slideIds: string[]) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        state.presentation.slides = state.presentation.slides.filter(
          (s) => !slideIds.includes(s.id)
        );
        state.presentation.arrangement = createArrangement(state.presentation.slides);

        if (slideIds.includes(state.activeSlideId || '')) {
          state.activeSlideId = state.presentation.slides[0]?.id || null;
          state.selection.slideIds = state.activeSlideId ? [state.activeSlideId] : [];
        } else {
          state.selection.slideIds = state.selection.slideIds.filter(
            (id) => !slideIds.includes(id)
          );
        }

        state.isDirty = true;
      });
    },

    updateSlide: (slideId: string, updates: Partial<Slide>) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          Object.assign(slide, updates, { updatedAt: new Date().toISOString() });
          state.isDirty = true;
        }
      });
    },

    updateSlides: (slideIds: string[], updates: Partial<Slide>) => {
      if (slideIds.length === 0) return;
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const updateIdSet = new Set(slideIds);
        state.presentation.slides.forEach((slide) => {
          if (!updateIdSet.has(slide.id)) return;
          Object.assign(slide, updates, { updatedAt: new Date().toISOString() });
        });
        state.isDirty = true;
      });
    },

    moveSlide: (slideId: string, toIndex: number) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const fromIndex = state.presentation.slides.findIndex((s) => s.id === slideId);
        if (fromIndex === -1) return;

        const [slide] = state.presentation.slides.splice(fromIndex, 1);
        state.presentation.slides.splice(toIndex, 0, slide);
        state.presentation.arrangement = createArrangement(state.presentation.slides);
        state.isDirty = true;
      });
    },

    setSlideSection: (slideId: string, section?: SongSection, label?: string) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          const previousAuto = getAutoLayoutType(slide);
          slide.section = section;
          slide.sectionLabel = section ? label || section : undefined;
          if (shouldUpdateLayoutType(slide, previousAuto)) {
            slide.layoutType = getAutoLayoutType(slide);
          }
          slide.updatedAt = new Date().toISOString();
          state.presentation.arrangement = createArrangement(state.presentation.slides);
          state.isDirty = true;
        }
      });
    },

    setSlidesSection: (slideIds: string[], section?: SongSection, label?: string) => {
      if (slideIds.length === 0) return;
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const updateIdSet = new Set(slideIds);
        state.presentation.slides.forEach((slide) => {
          if (!updateIdSet.has(slide.id)) return;
          const previousAuto = getAutoLayoutType(slide);
          slide.section = section;
          slide.sectionLabel = section ? label || section : undefined;
          if (shouldUpdateLayoutType(slide, previousAuto)) {
            slide.layoutType = getAutoLayoutType(slide);
          }
          slide.updatedAt = new Date().toISOString();
        });
        state.presentation.arrangement = createArrangement(state.presentation.slides);
        state.isDirty = true;
      });
    },

    setSlideTransition: (slideId: string, transition: Partial<SlideTransition>) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          if (!slide.animations) {
            slide.animations = {
              transition: { ...defaultSlideTransition },
              buildIn: [],
              buildOut: [],
            };
          }
          slide.animations.transition = { ...slide.animations.transition, ...transition };
          slide.updatedAt = new Date().toISOString();
          state.isDirty = true;
        }
      });
    },

    setSlidesTransition: (slideIds: string[], transition: Partial<SlideTransition>) => {
      if (slideIds.length === 0) return;
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const updateIdSet = new Set(slideIds);
        state.presentation.slides.forEach((slide) => {
          if (!updateIdSet.has(slide.id)) return;
          if (!slide.animations) {
            slide.animations = {
              transition: { ...defaultSlideTransition },
              buildIn: [],
              buildOut: [],
            };
          }
          slide.animations.transition = { ...slide.animations.transition, ...transition };
          slide.updatedAt = new Date().toISOString();
        });
        state.isDirty = true;
      });
    },

    // Layer methods
    addTextLayer: (slideId: string, content?: string) => {
      get().pushUndo();

      const layer = createTextLayer(content || 'Enter text here...');

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          slide.layers.push(layer);
          slide.updatedAt = new Date().toISOString();
          state.selection.layerIds = [layer.id];
          state.isDirty = true;
        }
      });

      return layer;
    },

    addShapeLayer: (slideId: string, shapeType: ShapeType = 'rectangle') => {
      get().pushUndo();

      const layer = createShapeLayer(shapeType);

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          slide.layers.push(layer);
          slide.updatedAt = new Date().toISOString();
          state.selection.layerIds = [layer.id];
          state.isDirty = true;
        }
      });

      return layer;
    },

    addMediaLayer: (slideId: string, mediaId: string, mediaType: 'image' | 'video') => {
      get().pushUndo();

      const layer = createMediaLayer(mediaId, mediaType);

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          slide.layers.push(layer);
          slide.updatedAt = new Date().toISOString();
          state.selection.layerIds = [layer.id];
          state.isDirty = true;
        }
      });

      return layer;
    },

    addWebLayer: (slideId: string, url: string) => {
      get().pushUndo();

      const layer = createWebLayer(url);

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          slide.layers.push(layer);
          slide.updatedAt = new Date().toISOString();
          state.selection.layerIds = [layer.id];
          state.isDirty = true;
        }
      });

      return layer;
    },

    updateLayer: (slideId: string, layerId: string, updates: Partial<Layer>) => {
      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          const layer = slide.layers.find((l) => l.id === layerId);
          if (layer) {
            Object.assign(layer, updates);
            slide.updatedAt = new Date().toISOString();
            state.isDirty = true;
          }
        }
      });
    },

    updateLayers: (slideId: string, updates: { layerId: string; updates: Partial<Layer> }[]) => {
      if (updates.length === 0) return;
      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (!slide) return;

        const updatesById = new Map(updates.map((update) => [update.layerId, update.updates]));
        slide.layers.forEach((layer) => {
          const layerUpdates = updatesById.get(layer.id);
          if (!layerUpdates) return;
          Object.assign(layer, layerUpdates);
        });
        slide.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });
    },

    deleteLayer: (slideId: string, layerId: string) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          slide.layers = slide.layers.filter((l) => l.id !== layerId);
          slide.updatedAt = new Date().toISOString();
          state.selection.layerIds = state.selection.layerIds.filter((id) => id !== layerId);
          state.isDirty = true;
        }
      });
    },

    // Layer ordering
    reorderLayer: (slideId: string, layerId: string, toIndex: number) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (!slide) return;

        const fromIndex = slide.layers.findIndex((l) => l.id === layerId);
        if (fromIndex === -1) return;

        const [layer] = slide.layers.splice(fromIndex, 1);
        slide.layers.splice(toIndex, 0, layer);
        slide.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });
    },

    bringLayerForward: (slideId: string, layerId: string) => {
      const { presentation } = get();
      if (!presentation) return;

      const slide = presentation.slides.find((s) => s.id === slideId);
      if (!slide) return;

      const index = slide.layers.findIndex((l) => l.id === layerId);
      if (index === -1 || index >= slide.layers.length - 1) return;

      get().reorderLayer(slideId, layerId, index + 1);
    },

    sendLayerBackward: (slideId: string, layerId: string) => {
      const { presentation } = get();
      if (!presentation) return;

      const slide = presentation.slides.find((s) => s.id === slideId);
      if (!slide) return;

      const index = slide.layers.findIndex((l) => l.id === layerId);
      if (index <= 0) return;

      get().reorderLayer(slideId, layerId, index - 1);
    },

    bringLayerToFront: (slideId: string, layerId: string) => {
      const { presentation } = get();
      if (!presentation) return;

      const slide = presentation.slides.find((s) => s.id === slideId);
      if (!slide) return;

      get().reorderLayer(slideId, layerId, slide.layers.length - 1);
    },

    sendLayerToBack: (slideId: string, layerId: string) => {
      get().reorderLayer(slideId, layerId, 0);
    },

    // Layer transform helpers for drag operations
    beginLayerTransform: () => {
      if (!get().isTransforming) {
        get().pushUndo();
        set((state) => {
          state.isTransforming = true;
        });
      }
    },

    updateLayerTransform: (slideId: string, layerId: string, transform: Partial<LayerTransform>) => {
      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          const layer = slide.layers.find((l) => l.id === layerId);
          if (layer) {
            layer.transform = { ...layer.transform, ...transform };
            slide.updatedAt = new Date().toISOString();
            state.isDirty = true;
          }
        }
      });
    },

    updateLayerTransforms: (
      slideId: string,
      updates: { layerId: string; transform: Partial<LayerTransform> }[]
    ) => {
      if (updates.length === 0) return;
      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (!slide) return;

        const updatesById = new Map(
          updates.map((update) => [update.layerId, update.transform])
        );
        slide.layers.forEach((layer) => {
          const layerUpdates = updatesById.get(layer.id);
          if (!layerUpdates) return;
          layer.transform = { ...layer.transform, ...layerUpdates };
        });
        slide.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });
    },

    commitLayerTransform: () => {
      set((state) => {
        state.isTransforming = false;
      });
    },

    // Build animations
    addBuildStep: (slideId: string, step: Omit<BuildStep, 'id'>) => {
      get().pushUndo();

      const buildStep: BuildStep = {
        ...step,
        id: uuid(),
      };

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          if (!slide.animations) {
            slide.animations = {
              transition: { ...defaultSlideTransition },
              buildIn: [],
              buildOut: [],
            };
          }
          slide.animations.buildIn.push(buildStep);
          slide.updatedAt = new Date().toISOString();
          state.isDirty = true;
        }
      });
    },

    updateBuildStep: (slideId: string, stepId: string, updates: Partial<BuildStep>) => {
      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide && slide.animations) {
          const step = slide.animations.buildIn.find((s) => s.id === stepId) ||
            slide.animations.buildOut.find((s) => s.id === stepId);
          if (step) {
            Object.assign(step, updates);
            slide.updatedAt = new Date().toISOString();
            state.isDirty = true;
          }
        }
      });
    },

    deleteBuildStep: (slideId: string, stepId: string) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide && slide.animations) {
          slide.animations.buildIn = slide.animations.buildIn.filter((s) => s.id !== stepId);
          slide.animations.buildOut = slide.animations.buildOut.filter((s) => s.id !== stepId);
          slide.updatedAt = new Date().toISOString();
          state.isDirty = true;
        }
      });
    },

    reorderBuildStep: (slideId: string, stepId: string, toIndex: number) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (!slide || !slide.animations) return;

        // Check buildIn first
        let fromIndex = slide.animations.buildIn.findIndex((s) => s.id === stepId);
        if (fromIndex !== -1) {
          const [step] = slide.animations.buildIn.splice(fromIndex, 1);
          slide.animations.buildIn.splice(toIndex, 0, step);
        } else {
          // Check buildOut
          fromIndex = slide.animations.buildOut.findIndex((s) => s.id === stepId);
          if (fromIndex !== -1) {
            const [step] = slide.animations.buildOut.splice(fromIndex, 1);
            slide.animations.buildOut.splice(toIndex, 0, step);
          }
        }

        slide.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });
    },

    setSlideBackground: (slideId: string, background: Background) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          slide.background = background;
          slide.updatedAt = new Date().toISOString();
          state.isDirty = true;
        }
      });
    },

    setSlidesBackground: (slideIds: string[], background: Background) => {
      if (slideIds.length === 0) return;
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const updateIdSet = new Set(slideIds);
        state.presentation.slides.forEach((slide) => {
          if (!updateIdSet.has(slide.id)) return;
          slide.background = background;
          slide.updatedAt = new Date().toISOString();
        });
        state.isDirty = true;
      });
    },

    setPresentationAspectRatio: (aspectRatio) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;
        state.presentation.manifest.aspectRatio = aspectRatio;
        state.presentation.manifest.slideSize = getBaseSlideSize(aspectRatio);
        state.presentation.manifest.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });
    },

    setPresentationResolution: (size) => {
      get().pushUndo();

      set((state) => {
        const presentation = state.presentation;
        if (!presentation) return;

        const nextWidth = Math.round(size.width);
        const nextHeight = Math.round(size.height);
        if (!Number.isFinite(nextWidth) || !Number.isFinite(nextHeight) || nextWidth <= 0 || nextHeight <= 0) {
          return;
        }

        const currentSize = getBaseSlideSize(
          presentation.manifest.aspectRatio,
          presentation.manifest.slideSize
        );
        const scaleX = nextWidth / currentSize.width;
        const scaleY = nextHeight / currentSize.height;
        const uniformScale = Math.min(scaleX, scaleY);

        presentation.slides = presentation.slides.map((slide) => ({
          ...slide,
          layers: slide.layers.map((layer) => {
            const scaledTransform = scaleTransform(layer.transform, scaleX, scaleY);
            if (layer.type === 'text') {
              return {
                ...layer,
                transform: scaledTransform,
                style:
                  layer.style && layer.style.font
                    ? scaleTextStyle(layer.style as TextStyle, uniformScale)
                    : layer.style,
              };
            }
            if (layer.type === 'shape') {
              return {
                ...layer,
                transform: scaledTransform,
                style: {
                  ...layer.style,
                  strokeWidth: layer.style.strokeWidth * uniformScale,
                  cornerRadius: layer.style.cornerRadius * uniformScale,
                },
                strokes: layer.strokes?.map((stroke) => ({
                  ...stroke,
                  width: stroke.width * uniformScale,
                })),
              };
            }
            return {
              ...layer,
              transform: scaledTransform,
              strokes: layer.strokes?.map((stroke) => ({
                ...stroke,
                width: stroke.width * uniformScale,
              })),
            };
          }),
          updatedAt: new Date().toISOString(),
        }));

        presentation.manifest.slideSize = { width: nextWidth, height: nextHeight };
        presentation.manifest.aspectRatio = resolveAspectRatioFromSize(nextWidth, nextHeight);
        presentation.manifest.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });
    },

    applyTheme: (themeId: string) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        state.presentation.manifest.themeId = themeId;
        state.isDirty = true;
      });
    },

    applyThemeTemplateToSlides: (slideIds, theme, options) => {
      const themeSlide = theme.slides[0];
      if (!themeSlide) return;
      get().applyThemeSlideToSlides(slideIds, theme, themeSlide, options);
    },

    applyThemeSlideToSlides: (slideIds, theme, themeSlide, options) => {
      if (slideIds.length === 0) return;
      const startTimestamp = Date.now();
      // #region agent log
      fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sessionId: 'debug-session', runId: 'pre-fix', hypothesisId: 'C', location: 'editorStore.ts:applyThemeSlideToSlides:start', message: 'apply_theme_slide_start', data: { slideCount: slideIds.length, themeSlideId: themeSlide.id, scaleMode: options?.scaleMode ?? 'none', themeAspect: theme.aspectRatio }, timestamp: Date.now() }) }).catch(() => { });
      // #endregion
      get().pushUndo();

      set((state) => {
        const presentation = state.presentation;
        if (!presentation) return;

        const targetAspect = presentation.manifest.aspectRatio ?? '16:9';
        const targetSize = getBaseSlideSize(targetAspect, presentation.manifest.slideSize);
        const sourceSize = theme.baseSize ?? getBaseSlideSize(theme.aspectRatio);
        const scaleMode = options?.scaleMode ?? 'none';
        const applyOptions = { scaleMode, sourceSize, targetSize };

        const updateIdSet = new Set(slideIds);
        presentation.slides.forEach((slide) => {
          if (!updateIdSet.has(slide.id)) return;
          applyThemeSlideToSlideInPlace(slide, themeSlide, applyOptions);
        });

        presentation.manifest.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });

      // #region agent log
      fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sessionId: 'debug-session', runId: 'pre-fix', hypothesisId: 'C', location: 'editorStore.ts:applyThemeSlideToSlides:end', message: 'apply_theme_slide_end', data: { durationMs: Date.now() - startTimestamp, slideCount: slideIds.length }, timestamp: Date.now() }) }).catch(() => { });
      // #endregion
    },

    applyThemeSlideMappings: (slideThemeMap, theme, options) => {
      const slideIds = Object.keys(slideThemeMap);
      if (slideIds.length === 0) return;
      get().pushUndo();

      set((state) => {
        const presentation = state.presentation;
        if (!presentation) return;

        const targetAspect = presentation.manifest.aspectRatio ?? '16:9';
        const targetSize = getBaseSlideSize(targetAspect, presentation.manifest.slideSize);
        const sourceSize = theme.baseSize ?? getBaseSlideSize(theme.aspectRatio);
        const scaleMode = options?.scaleMode ?? 'none';
        const applyOptions = { scaleMode, sourceSize, targetSize };

        const themeSlideById = new Map(theme.slides.map((slide) => [slide.id, slide]));
        const updateIdSet = new Set(slideIds);
        presentation.slides.forEach((slide) => {
          if (!updateIdSet.has(slide.id)) return;
          const themeSlideId = slideThemeMap[slide.id];
          const themeSlide = themeSlideById.get(themeSlideId);
          if (!themeSlide) return;
          applyThemeSlideToSlideInPlace(slide, themeSlide, applyOptions);
        });

        presentation.manifest.updatedAt = new Date().toISOString();
        state.isDirty = true;
      });
    },

    saveThemeFromSlide: (slideId: string, themeName: string) => {
      const { presentation } = get();
      if (!presentation) return null;

      const slide = presentation.slides.find((s) => s.id === slideId);
      const currentTheme = presentation.themes.find(
        (t) => t.id === presentation.manifest.themeId
      );

      if (!currentTheme) return null;

      // Create new theme based on current theme + slide overrides
      const newTheme = createTheme(themeName, {
        background: slide?.overrides?.background || currentTheme.background,
        primaryText: { ...currentTheme.primaryText, ...slide?.overrides?.primaryText },
        secondaryText: { ...currentTheme.secondaryText, ...slide?.overrides?.secondaryText },
        padding: { ...currentTheme.padding, ...slide?.overrides?.padding },
        aspectRatio: currentTheme.aspectRatio,
      });

      set((state) => {
        if (!state.presentation) return;

        state.presentation.themes.push(newTheme);
        state.isDirty = true;
      });

      return newTheme;
    },

    addTheme: (theme: Theme) => {
      set((state) => {
        if (!state.presentation) return;

        state.presentation.themes.push(theme);
        state.isDirty = true;
      });
    },

    updateTheme: (themeId: string, updates: Partial<Theme>) => {
      get().pushUndo();

      set((state) => {
        if (!state.presentation) return;

        const theme = state.presentation.themes.find((t) => t.id === themeId);
        if (theme) {
          Object.assign(theme, updates, { updatedAt: new Date().toISOString() });
          state.isDirty = true;
        }
      });
    },

    deleteTheme: (themeId: string) => {
      set((state) => {
        if (!state.presentation) return;

        // Don't delete if it's the current theme
        if (state.presentation.manifest.themeId === themeId) return;

        state.presentation.themes = state.presentation.themes.filter((t) => t.id !== themeId);
        state.isDirty = true;
      });
    },

    addMedia: (entry: MediaEntry, sourcePath: string) => {
      set((state) => {
        if (!state.presentation) return;

        state.presentation.manifest.media.push(entry);
        state.pendingMedia.set(entry.id, sourcePath);
        state.isDirty = true;
      });
    },

    removeMedia: (mediaId: string) => {
      set((state) => {
        if (!state.presentation) return;

        state.presentation.manifest.media = state.presentation.manifest.media.filter(
          (m) => m.id !== mediaId
        );
        state.pendingMedia.delete(mediaId);
        state.isDirty = true;
      });
    },

    ensureFontFamilyBundled: async (family: string, systemFonts: SystemFontInfo[]) => {
      const { presentation, pendingFonts, filePath } = get();
      if (!presentation) return;

      const existingFonts = Array.isArray(presentation.manifest.fonts)
        ? presentation.manifest.fonts
        : [];
      if (existingFonts.some((font) => font.family === family)) return;

      const matchingFonts = systemFonts.filter((font) => font.family === family);
      if (matchingFonts.length === 0) return;

      const uniquePaths = Array.from(new Set(matchingFonts.map((font) => font.path)));
      const entries = await importFontFiles(uniquePaths);

      const existingHashes = new Set(existingFonts.map((font) => font.sha256));
      const nextEntries = entries.filter((entry) => !existingHashes.has(entry.sha256));
      if (nextEntries.length === 0) return;

      const sourceById = new Map<string, string>();
      entries.forEach((entry, index) => {
        const sourcePath = uniquePaths[index];
        if (sourcePath) sourceById.set(entry.id, sourcePath);
      });

      set((state) => {
        if (!state.presentation) return;

        state.presentation.manifest.fonts = [
          ...(state.presentation.manifest.fonts ?? []),
          ...nextEntries,
        ];

        for (const entry of nextEntries) {
          const sourcePath = sourceById.get(entry.id);
          if (sourcePath) {
            state.pendingFonts.set(entry.id, sourcePath);
          } else if (filePath) {
            state.pendingFonts.set(entry.id, `bundle:${entry.path}`);
          }
        }

        state.isDirty = true;
      });
    },

    trackRecentFont: (family: string) => {
      const nextFamily = family.trim();
      if (!nextFamily) return;
      set((state) => {
        const next = [nextFamily, ...state.recentFonts.filter((item) => item !== nextFamily)];
        state.recentFonts = next.slice(0, 8);
      });
    },

    undo: () => {
      const { undoStack, presentation } = get();
      if (undoStack.length === 0 || !presentation) return;

      const entry = undoStack[undoStack.length - 1];

      set((state) => {
        // Push current state to redo
        state.redoStack.push({
          presentation: JSON.parse(JSON.stringify(state.presentation)),
          selection: { ...state.selection },
        });

        // Restore from undo
        state.presentation = JSON.parse(JSON.stringify(entry.presentation));
        state.selection = { ...entry.selection };
        state.activeSlideId = entry.selection.slideIds[0] || null;
        state.undoStack.pop();
        state.isDirty = true;
      });
    },

    redo: () => {
      const { redoStack } = get();
      if (redoStack.length === 0) return;

      const entry = redoStack[redoStack.length - 1];

      set((state) => {
        // Push current state to undo
        state.undoStack.push({
          presentation: JSON.parse(JSON.stringify(state.presentation)),
          selection: { ...state.selection },
        });

        // Restore from redo
        state.presentation = JSON.parse(JSON.stringify(entry.presentation));
        state.selection = { ...entry.selection };
        state.activeSlideId = entry.selection.slideIds[0] || null;
        state.redoStack.pop();
        state.isDirty = true;
      });
    },

    pushUndo: () => {
      const { presentation, selection, maxUndoLevels } = get();
      if (!presentation) return;

      set((state) => {
        state.undoStack.push({
          presentation: JSON.parse(JSON.stringify(presentation)),
          selection: { ...selection },
        });

        // Trim to max
        if (state.undoStack.length > maxUndoLevels) {
          state.undoStack.shift();
        }

        // Clear redo on new action
        state.redoStack = [];
      });
    },

    updateTitle: (title: string) => {
      set((state) => {
        if (!state.presentation) return;

        state.presentation.manifest.title = title;
        state.isDirty = true;
      });
    },
    linkExternalSong: (songId: string, groupId: string) => {
      const now = new Date().toISOString();
      set((state) => {
        if (!state.presentation) return;

        state.presentation.manifest.externalSong = {
          songId,
          groupId,
          syncedAt: now,
        };
        state.presentation.manifest.sync = {
          status: 'linked',
          lastSyncAttempt: now,
        };
        state.isDirty = true;
      });
    },
    unlinkExternalSong: () => {
      set((state) => {
        if (!state.presentation) return;

        state.presentation.manifest.externalSong = undefined;
        state.presentation.manifest.sync = undefined;
        state.isDirty = true;
      });
    },

    // Auto-save actions
    setAutoSaveStatus: (status: AutoSaveStatus, error?: string) => {
      set((state) => {
        state.autoSave.status = status;
        if (error !== undefined) {
          state.autoSave.lastError = error;
        }
        if (status === 'saved') {
          state.autoSave.lastSaved = new Date().toISOString();
          state.autoSave.lastError = null;
        }
      });
    },

    markSaved: (filePath: string) => {
      set((state) => {
        state.filePath = filePath;
        state.isDirty = false;
        state.pendingMedia = new Map();
        state.pendingFonts = new Map();
        state.autoSave.status = 'saved';
        state.autoSave.lastSaved = new Date().toISOString();
        state.autoSave.lastError = null;
      });
    },

    remapFilePath: (oldBase, newBase) => {
      const normalizePath = (path: string) => path.replace(/\\/g, '/');
      const normalizedOld = normalizePath(oldBase);
      const normalizedNew = normalizePath(newBase);

      set((state) => {
        const current = state.filePath;
        if (!current) return;
        const normalizedPath = normalizePath(current);
        if (!normalizedPath.startsWith(`${normalizedOld}/`)) return;
        state.filePath = `${normalizedNew}/${normalizedPath.slice(normalizedOld.length + 1)}`;
      });
    },

    setColorLibrary: (colors: string[]) => {
      set((state) => {
        state.colorLibrary = colors.slice(0, 8);
      });
    },
    addColorToLibrary: (color: string) => {
      const next = color.trim();
      if (!next) return;
      set((state) => {
        const deduped = [next, ...state.colorLibrary.filter((item) => item !== next)];
        state.colorLibrary = deduped.slice(0, 8);
      });
    },
    removeColorFromLibrary: (color: string) => {
      set((state) => {
        state.colorLibrary = state.colorLibrary.filter((item) => item !== color);
      });
    },
    setStrokeLibrary: (strokes: LayerStroke[]) => {
      set((state) => {
        state.strokeLibrary = strokes.slice(0, 8);
      });
    },
    addStrokeToLibrary: (stroke: LayerStroke) => {
      set((state) => {
        const deduped = [
          stroke,
          ...state.strokeLibrary.filter((item) => item.id !== stroke.id),
        ];
        state.strokeLibrary = deduped.slice(0, 8);
      });
    },
    removeStrokeFromLibrary: (strokeId: string) => {
      set((state) => {
        state.strokeLibrary = state.strokeLibrary.filter((item) => item.id !== strokeId);
      });
    },
  }))
);
