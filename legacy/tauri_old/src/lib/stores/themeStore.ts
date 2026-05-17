/**
 * Theme Store - manages global theme templates
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import { load } from '@tauri-apps/plugin-store';
import { v4 as uuid } from 'uuid';
import { ensureDocumentsDataSubDir, getDocumentsDataDir } from '../tauri-api';
import type {
  Slide,
  Layer,
  ThemeTemplate,
  ThemeTemplateSlide,
} from '../models';
import {
  createLayerFill,
  createLayerStroke,
  defaultLayerTransform,
  defaultPrimaryTextStyle,
  defaultShapeStyle,
  getThemeSlideLayers,
  normalizeLayoutType,
} from '../models';

let themesStoreCache: {
  path: string;
  promise: ReturnType<typeof load>;
} | null = null;

/** Resolves to `<content dir>/themes/themes.json` (same root as libraries, playlists, etc.). */
export async function getThemesJsonPath(): Promise<string> {
  await ensureDocumentsDataSubDir('themes');
  const base = await getDocumentsDataDir();
  return `${base.replace(/\\/g, '/')}/themes/themes.json`;
}

export async function getThemesStore() {
  const path = await getThemesJsonPath();
  if (!themesStoreCache || themesStoreCache.path !== path) {
    themesStoreCache = {
      path,
      promise: load(path, {
        defaults: { themes: [] },
        autoSave: false,
      }),
    };
  }
  return themesStoreCache.promise;
}

const getBaseSlideSize = (aspectRatio?: ThemeTemplate['aspectRatio']) => {
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

const cloneLayers = (layers: Layer[]) =>
  typeof structuredClone === 'function'
    ? (structuredClone(layers) as Layer[])
    : (JSON.parse(JSON.stringify(layers)) as Layer[]);

const normalizeThemeLayer = (layer: Layer): Layer => {
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
    default:
      return { ...layer, ...baseLayer };
  }
};

const normalizeThemeLayers = (layers: Layer[]) => layers.map(normalizeThemeLayer);

const ensureUniqueLayoutTypes = (slides: ThemeTemplateSlide[]): ThemeTemplateSlide[] => {
  const used = new Set<string>();
  const nameCounts = new Map<string, number>();

  slides.forEach((slide) => {
    const normalizedName = normalizeLayoutType(slide.name);
    if (!normalizedName) return;
    nameCounts.set(normalizedName, (nameCounts.get(normalizedName) ?? 0) + 1);
  });

  return slides.map((slide) => {
    let layoutType = slide.layoutType?.trim() || undefined;
    if (!layoutType && slide.name?.trim()) {
      const nameKey = normalizeLayoutType(slide.name);
      if (nameKey && nameCounts.get(nameKey) === 1) {
        layoutType = slide.name.trim();
      }
    }

    const layoutKey = normalizeLayoutType(layoutType);
    if (!layoutKey) {
      return { ...slide, layoutType: undefined };
    }
    if (used.has(layoutKey)) {
      return { ...slide, layoutType: undefined };
    }
    used.add(layoutKey);
    return { ...slide, layoutType };
  });
};

const getLatestThemeUpdatedAt = (themes: ThemeTemplate[]) =>
  themes.reduce<string | null>((latest, theme) => {
    if (!theme.updatedAt) return latest;
    if (!latest) return theme.updatedAt;
    return new Date(theme.updatedAt) > new Date(latest) ? theme.updatedAt : latest;
  }, null);

export const createThemeSlideFromSlide = (
  slide: Slide,
  name?: string
): ThemeTemplateSlide => ({
  id: uuid(),
  name: name?.trim() ? name : undefined,
  layoutType: slide.layoutType,
  background: slide.background ?? { type: 'solid', color: '#000000' },
  layers: cloneLayers(slide.layers ?? []),
  mediaCues: Array.isArray(slide.mediaCues) ? slide.mediaCues : [],
});

export const createThemeTemplateFromSlide = (
  name: string,
  slide: Slide,
  aspectRatio: ThemeTemplate['aspectRatio'] = '16:9'
): ThemeTemplate => {
  const now = new Date().toISOString();
  return {
    id: uuid(),
    name,
    createdAt: now,
    updatedAt: now,
    aspectRatio,
    baseSize: getBaseSlideSize(aspectRatio),
    slides: [createThemeSlideFromSlide(slide)],
  };
};

interface ThemeState {
  themes: ThemeTemplate[];
  isLoading: boolean;
  error: string | null;
  isDirty: boolean;
  autoSave: {
    status: 'idle' | 'pending' | 'saving' | 'saved' | 'error' | 'conflict';
    lastSaved: string | null;
    lastError: string | null;
  };

  loadThemes: () => Promise<void>;
  saveThemes: () => Promise<void>;
  addTheme: (theme: ThemeTemplate) => void;
  updateTheme: (themeId: string, updates: Partial<ThemeTemplate>) => void;
  deleteTheme: (themeId: string) => void;
  duplicateTheme: (themeId: string) => ThemeTemplate | null;
  createThemeFromSlide: (
    name: string,
    slide: Slide,
    aspectRatio?: ThemeTemplate['aspectRatio']
  ) => ThemeTemplate;
}

export const useThemeStore = create<ThemeState>()(
  immer((set, get) => ({
    themes: [],
    isLoading: false,
    error: null,
    isDirty: false,
    autoSave: {
      status: 'idle',
      lastSaved: null,
      lastError: null,
    },

    loadThemes: async () => {
      set((state) => {
        state.isLoading = true;
        state.error = null;
      });
      try {
        const store = await getThemesStore();
        const themes = await store.get<ThemeTemplate[]>('themes');
        set((state) => {
          state.themes = Array.isArray(themes)
            ? themes.map((theme) => {
              const aspectRatio = theme.aspectRatio ?? '16:9';
              const baseSize = theme.baseSize ?? getBaseSlideSize(aspectRatio);
              const slides = Array.isArray(theme.slides) && theme.slides.length > 0
                ? theme.slides.map((slide) => {
                  const normalizedLayers = normalizeThemeLayers(getThemeSlideLayers(slide));
                  return {
                    id: slide.id ?? uuid(),
                    name: slide.name?.trim() || undefined,
                    layoutType: slide.layoutType?.trim() || undefined,
                    background: slide.background ?? { type: 'solid', color: '#000000' },
                    layers: normalizedLayers,
                    mediaCues: Array.isArray(slide.mediaCues) ? slide.mediaCues : [],
                  };
                })
                : [
                  {
                    id: uuid(),
                    name: undefined,
                    layoutType: undefined,
                    background: (theme as ThemeTemplate & { background?: ThemeTemplateSlide['background'] }).background
                      ?? { type: 'solid', color: '#000000' },
                    layers: [],
                    mediaCues: [],
                  },
                ];

              return {
                ...theme,
                aspectRatio,
                baseSize,
                slides: ensureUniqueLayoutTypes(slides),
              };
            })
            : [];
          state.isLoading = false;
          state.isDirty = false;
          state.autoSave.status = 'saved';
          state.autoSave.lastSaved = getLatestThemeUpdatedAt(state.themes);
          state.autoSave.lastError = null;
        });
      } catch (error) {
        set((state) => {
          state.error = String(error);
          state.isLoading = false;
        });
      }
    },

    saveThemes: async () => {
      try {
        const store = await getThemesStore();
        await store.set('themes', get().themes);
        await store.save();
      } catch (error) {
        set((state) => {
          state.error = String(error);
        });
      }
    },

    addTheme: (theme) => {
      set((state) => {
        state.themes.push(theme);
        state.isDirty = true;
      });
    },

    updateTheme: (themeId, updates) => {
      set((state) => {
        const theme = state.themes.find((item) => item.id === themeId);
        if (theme) {
          const nextUpdates = { ...updates };
          if (Array.isArray(nextUpdates.slides)) {
            nextUpdates.slides = ensureUniqueLayoutTypes(nextUpdates.slides);
          }
          Object.assign(theme, nextUpdates, { updatedAt: new Date().toISOString() });
          state.isDirty = true;
        }
      });
    },

    deleteTheme: (themeId) => {
      set((state) => {
        state.themes = state.themes.filter((theme) => theme.id !== themeId);
        state.isDirty = true;
      });
    },

    duplicateTheme: (themeId) => {
      const theme = get().themes.find((item) => item.id === themeId);
      if (!theme) return null;
      const now = new Date().toISOString();
      const slides = theme.slides.map((slide) => ({
        ...slide,
        background: { ...slide.background },
        layers: cloneLayers(slide.layers ?? []),
        mediaCues: Array.isArray(slide.mediaCues) ? slide.mediaCues.map((cue) => ({ ...cue })) : [],
      }));
      const duplicate: ThemeTemplate = {
        ...theme,
        id: uuid(),
        name: `${theme.name} Copy`,
        createdAt: now,
        updatedAt: now,
        slides,
      };
      set((state) => {
        state.themes.push(duplicate);
        state.isDirty = true;
      });
      return duplicate;
    },

    createThemeFromSlide: (name, slide, aspectRatio = '16:9') => {
      const theme = createThemeTemplateFromSlide(name, slide, aspectRatio);
      set((state) => {
        state.themes.push(theme);
        state.isDirty = true;
      });
      return theme;
    },
  }))
);
