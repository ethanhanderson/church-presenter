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
  Layer,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
  LayerTransform,
  ShapeType,
  SongSection,
  SlideType,
  Background,
  MediaEntry,
  SlideTransition,
  BuildStep,
} from '../models';
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
  defaultSlideTransition,
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
          };
        case 'shape':
          return {
            ...layer,
            ...baseLayer,
            shapeType: layer.shapeType || 'rectangle',
            style: { ...defaultShapeStyle, ...layer.style },
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

    if (!slide.animations) {
      slide.animations = {
        transition: { ...defaultSlideTransition },
        buildIn: [],
        buildOut: [],
      };
    } else if (!slide.animations.transition) {
      slide.animations.transition = { ...defaultSlideTransition };
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
  
  // Auto-save state
  autoSave: AutoSaveState;
  
  // Actions
  newPresentation: (title: string) => void;
  openPresentation: (path: string) => Promise<void>;
  savePresentation: (path?: string) => Promise<void>;
  closePresentation: () => void;
  
  // Auto-save actions
  setAutoSaveStatus: (status: AutoSaveStatus, error?: string) => void;
  markSaved: (filePath: string) => void;
  remapFilePath: (oldBase: string, newBase: string) => void;
  
  // Selection
  selectSlide: (slideId: string | null) => void;
  selectSlides: (slideIds: string[]) => void;
  selectLayer: (layerId: string | null) => void;
  selectLayers: (layerIds: string[]) => void;
  clearSelection: () => void;
  
  // Slides
  addSlide: (type: SlideType, options?: { section?: SongSection; afterSlideId?: string }) => Slide;
  duplicateSlide: (slideId: string) => Slide | null;
  deleteSlide: (slideId: string) => void;
  deleteSlides: (slideIds: string[]) => void;
  updateSlide: (slideId: string, updates: Partial<Slide>) => void;
  moveSlide: (slideId: string, toIndex: number) => void;
  setSlideSection: (slideId: string, section: SongSection, label?: string) => void;
  setSlideTransition: (slideId: string, transition: Partial<SlideTransition>) => void;
  
  // Layers (replacing text blocks)
  addTextLayer: (slideId: string, content?: string) => TextLayer | null;
  addShapeLayer: (slideId: string, shapeType?: ShapeType) => ShapeLayer | null;
  addMediaLayer: (slideId: string, mediaId: string, mediaType: 'image' | 'video') => MediaLayer | null;
  addWebLayer: (slideId: string, url: string) => WebLayer | null;
  updateLayer: (slideId: string, layerId: string, updates: Partial<Layer>) => void;
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
  commitLayerTransform: () => void;
  
  // Build animations
  addBuildStep: (slideId: string, step: Omit<BuildStep, 'id'>) => void;
  updateBuildStep: (slideId: string, stepId: string, updates: Partial<BuildStep>) => void;
  deleteBuildStep: (slideId: string, stepId: string) => void;
  reorderBuildStep: (slideId: string, stepId: string, toIndex: number) => void;
  
  // Backgrounds
  setSlideBackground: (slideId: string, background: Background) => void;
  
  // Themes
  applyTheme: (themeId: string) => void;
  saveThemeFromSlide: (slideId: string, themeName: string) => Theme | null;
  addTheme: (theme: Theme) => void;
  updateTheme: (themeId: string, updates: Partial<Theme>) => void;
  deleteTheme: (themeId: string) => void;
  
  // Media
  addMedia: (entry: MediaEntry, sourcePath: string) => void;
  removeMedia: (mediaId: string) => void;

  // Fonts
  ensureFontFamilyBundled: (family: string, systemFonts: SystemFontInfo[]) => Promise<void>;
  
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
    autoSave: {
      status: 'idle',
      lastSaved: null,
      lastError: null,
    },

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

    selectSlides: (slideIds: string[]) => {
      set((state) => {
        state.selection.slideIds = slideIds;
        state.activeSlideId = slideIds[0] || null;
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

    setSlideSection: (slideId: string, section: SongSection, label?: string) => {
      get().pushUndo();
      
      set((state) => {
        if (!state.presentation) return;

        const slide = state.presentation.slides.find((s) => s.id === slideId);
        if (slide) {
          slide.section = section;
          slide.sectionLabel = label || section;
          slide.updatedAt = new Date().toISOString();
          state.presentation.arrangement = createArrangement(state.presentation.slides);
          state.isDirty = true;
        }
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
          if (!slide.overrides) {
            slide.overrides = {};
          }
          slide.overrides.background = background;
          slide.updatedAt = new Date().toISOString();
          state.isDirty = true;
        }
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
  }))
);
