/**
 * Settings Store - manages app settings
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import type { AppSettings, PresentationRef } from '../models';
import { defaultAppSettings } from '../models';
import { load } from '@tauri-apps/plugin-store';

const SETTINGS_FILE = 'settings.json';
let systemThemeMql: MediaQueryList | null = null;
let systemThemeListener: ((event: MediaQueryListEvent) => void) | null = null;
let settingsStorePromise: ReturnType<typeof load> | null = null;

const getSettingsStore = () => {
  if (!settingsStorePromise) {
    settingsStorePromise = load(SETTINGS_FILE, {
      defaults: { settings: defaultAppSettings },
      autoSave: 300,
    });
  }
  return settingsStorePromise;
};

const mergeSettings = (stored?: AppSettings | null): AppSettings => {
  const output = { ...defaultAppSettings.output, ...stored?.output };
  const legacyMonitorId = (stored?.output as { monitorId?: string } | undefined)?.monitorId;
  if ((!output.monitorIds || output.monitorIds.length === 0) && legacyMonitorId) {
    output.monitorIds = [legacyMonitorId];
  }

  return {
    ...defaultAppSettings,
    ...stored,
    output,
    editor: { ...defaultAppSettings.editor, ...stored?.editor },
    show: { ...defaultAppSettings.show, ...stored?.show },
    reflow: { ...defaultAppSettings.reflow, ...stored?.reflow },
    integrations: {
      ...defaultAppSettings.integrations,
      ...stored?.integrations,
      musicManager: {
        ...defaultAppSettings.integrations.musicManager,
        ...stored?.integrations?.musicManager,
      },
    },
    updates: { ...defaultAppSettings.updates, ...stored?.updates },
  };
};

interface SettingsState {
  settings: AppSettings;
  isLoading: boolean;
  error: string | null;

  // Actions
  loadSettings: () => Promise<void>;
  saveSettings: () => Promise<void>;
  updateSettings: (updates: Partial<AppSettings>) => void;
  
  // Recent files
  addRecentFile: (ref: PresentationRef) => void;
  removeRecentFile: (path: string) => void;
  clearRecentFiles: () => void;
  remapRecentFilePaths: (oldBase: string, newBase: string) => void;
  
  // Theme
  setTheme: (theme: 'light' | 'dark' | 'system') => void;
  
  // Internal
  applyTheme: () => void;
}

export const useSettingsStore = create<SettingsState>()(
  immer((set, get) => ({
    settings: defaultAppSettings,
    isLoading: false,
    error: null,

    loadSettings: async () => {
      set((state) => {
        state.isLoading = true;
        state.error = null;
      });

      try {
        const store = await getSettingsStore();
        const settings = await store.get<AppSettings>('settings');
        const shouldForceSystemTheme = settings?.theme && settings.theme !== 'system';
        set((state) => {
          state.settings = mergeSettings(settings);
          if (shouldForceSystemTheme) {
            state.settings.theme = 'system';
          }
          state.isLoading = false;
        });
        
        // Apply theme
        get().applyTheme();
        if (shouldForceSystemTheme) {
          get().saveSettings();
        }
      } catch (error) {
        set((state) => {
          state.error = String(error);
          state.isLoading = false;
        });
      }
    },

    saveSettings: async () => {
      try {
        const store = await getSettingsStore();
        await store.set('settings', get().settings);
        await store.save();
      } catch (error) {
        set((state) => {
          state.error = String(error);
        });
      }
    },

    updateSettings: (updates: Partial<AppSettings>) => {
      set((state) => {
        Object.assign(state.settings, updates);
      });
      get().saveSettings();
    },

    addRecentFile: (ref: PresentationRef) => {
      set((state) => {
        // Remove existing entry with same path
        state.settings.recentFiles = state.settings.recentFiles.filter(
          (r) => r.path !== ref.path
        );
        
        // Add to front
        state.settings.recentFiles.unshift({
          ...ref,
          updatedAt: new Date().toISOString(),
        });
        
        // Trim to max
        if (state.settings.recentFiles.length > state.settings.maxRecentFiles) {
          state.settings.recentFiles = state.settings.recentFiles.slice(
            0,
            state.settings.maxRecentFiles
          );
        }
      });
      get().saveSettings();
    },

    removeRecentFile: (path: string) => {
      set((state) => {
        state.settings.recentFiles = state.settings.recentFiles.filter(
          (r) => r.path !== path
        );
      });
      get().saveSettings();
    },

    clearRecentFiles: () => {
      set((state) => {
        state.settings.recentFiles = [];
      });
      get().saveSettings();
    },

    remapRecentFilePaths: (oldBase, newBase) => {
      const normalizePath = (path: string) => path.replace(/\\/g, '/');
      const isAbsolutePath = (path: string) => /^[a-zA-Z]:[\\/]|^\//.test(path);
      const normalizedOld = normalizePath(oldBase);
      const normalizedNew = normalizePath(newBase);

      set((state) => {
        state.settings.recentFiles = state.settings.recentFiles.map((ref) => {
          if (!isAbsolutePath(ref.path)) {
            return ref;
          }
          const normalizedPath = normalizePath(ref.path);
          if (!normalizedPath.startsWith(`${normalizedOld}/`)) {
            return ref;
          }
          return {
            ...ref,
            path: `${normalizedNew}/${normalizedPath.slice(normalizedOld.length + 1)}`,
          };
        });
      });
      get().saveSettings();
    },

    setTheme: (theme: 'light' | 'dark' | 'system') => {
      set((state) => {
        state.settings.theme = theme;
      });
      get().saveSettings();
      get().applyTheme();
    },

    // Internal helper to apply theme to document
    applyTheme: () => {
      const { theme } = get().settings;
      const root = document.documentElement;

      const applySystemTheme = (prefersDark: boolean) => {
        root.classList.toggle('dark', prefersDark);
      };

      if (theme === 'system') {
        if (!systemThemeMql) {
          systemThemeMql = window.matchMedia('(prefers-color-scheme: dark)');
        }
        applySystemTheme(systemThemeMql.matches);

        if (!systemThemeListener) {
          systemThemeListener = (event) => applySystemTheme(event.matches);
          if (systemThemeMql.addEventListener) {
            systemThemeMql.addEventListener('change', systemThemeListener);
          } else {
            systemThemeMql.addListener(systemThemeListener);
          }
        }
        return;
      }

      if (systemThemeMql && systemThemeListener) {
        if (systemThemeMql.removeEventListener) {
          systemThemeMql.removeEventListener('change', systemThemeListener);
        } else {
          systemThemeMql.removeListener(systemThemeListener);
        }
        systemThemeListener = null;
      }

      root.classList.toggle('dark', theme === 'dark');
    },
  }))
);

