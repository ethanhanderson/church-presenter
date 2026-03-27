/**
 * useAutoSave - Hook for automatic saving of presentations with debounce
 */

import { useEffect, useRef, useCallback } from 'react';
import { useEditorStore, useSettingsStore, useThemeStore } from '@/lib/stores';
import { saveBundle, openBundle, type FontFileRef, type MediaFileRef } from '@/lib/tauri-api';
import { generatePresentationPath, generateSongPresentationPath } from '@/lib/services/appDataService';
import type { Presentation, ThemeTemplate } from '@/lib/models';
import { getThemesStore, THEMES_FILE } from '@/lib/stores/themeStore';

export type AutoSaveStatus = 'idle' | 'pending' | 'saving' | 'saved' | 'error' | 'conflict';

interface ConflictData {
  localVersion: Presentation;
  remoteVersion: Presentation;
  filePath: string;
}

interface UseAutoSaveOptions {
  /** Debounce delay in milliseconds for presentation content changes (default: 1000) */
  debounceMs?: number;
  /** Whether auto-save is enabled (respects settings) */
  enabled?: boolean;
  /** Callback when a conflict is detected */
  onConflict?: (conflict: ConflictData) => void;
  /** Callback when auto-save completes */
  onSaved?: () => void;
  /** Callback when auto-save fails */
  onError?: (error: string) => void;
}

export interface ThemeConflictData {
  localThemes: ThemeTemplate[];
  remoteThemes: ThemeTemplate[];
  filePath: string;
}

interface UseThemeAutoSaveOptions {
  /** Debounce delay in milliseconds for theme changes (default: 1000) */
  debounceMs?: number;
  /** Whether auto-save is enabled (respects settings) */
  enabled?: boolean;
  /** Callback when a conflict is detected */
  onConflict?: (conflict: ThemeConflictData) => void;
  /** Callback when auto-save completes */
  onSaved?: () => void;
  /** Callback when auto-save fails */
  onError?: (error: string) => void;
}

const getLatestThemeUpdatedAt = (themes: ThemeTemplate[]) =>
  themes.reduce<string | null>((latest, theme) => {
    if (!theme.updatedAt) return latest;
    if (!latest) return theme.updatedAt;
    return new Date(theme.updatedAt) > new Date(latest) ? theme.updatedAt : latest;
  }, null);

/**
 * Generate a file path for a presentation in the app data directory
 */
export function useAutoSave(options: UseAutoSaveOptions = {}) {
  const {
    debounceMs = 1000, // Longer debounce keeps editor responsive during edits
    enabled = true,
    onConflict,
    onSaved,
    onError,
  } = options;

  const { settings } = useSettingsStore();
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const intervalTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const lastSavedVersionRef = useRef<string | null>(null);
  const isSavingRef = useRef(false);
  const skipInitialSaveRef = useRef(false);
  const pendingSaveAfterTransformRef = useRef(false);

  // Store callbacks in refs to avoid recreating dependent callbacks
  const onConflictRef = useRef(onConflict);
  const onSavedRef = useRef(onSaved);
  const onErrorRef = useRef(onError);

  // Keep refs updated
  useEffect(() => {
    onConflictRef.current = onConflict;
    onSavedRef.current = onSaved;
    onErrorRef.current = onError;
  }, [onConflict, onSaved, onError]);

  // Get the effective enabled state from settings
  const isEnabled = enabled && settings.editor.autoSaveEnabled;

  /**
   * Perform the actual save operation
   */
  const performSave = useCallback(async () => {
    const { presentation, filePath, pendingMedia, pendingFonts, isDirty } = useEditorStore.getState();

    if (!presentation || !isDirty || isSavingRef.current) {
      return;
    }

    const startTimestamp = Date.now();
    // #region agent log
    fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sessionId: 'debug-session', runId: 'pre-fix', hypothesisId: 'A', location: 'use-auto-save.ts:performSave:start', message: 'autosave_start', data: { filePath, slideCount: presentation.slides.length, pendingMediaCount: pendingMedia.size, pendingFontsCount: pendingFonts.size }, timestamp: Date.now() }) }).catch(() => { });
    // #endregion

    // Generate a path if none exists
    let savePath = filePath;
    if (!savePath) {
      // Check if this is a song presentation (has externalSong link)
      if (presentation.manifest.externalSong?.songId) {
        savePath = await generateSongPresentationPath(presentation.manifest.externalSong.songId);
      } else {
        savePath = await generatePresentationPath(
          presentation.manifest.title,
          presentation.manifest.presentationId
        );
      }
    }

    isSavingRef.current = true;

    // Update status to saving
    useEditorStore.setState((state) => {
      state.autoSave.status = 'saving';
    });

    try {
      // Check for conflicts if file already exists and was previously saved
      if (lastSavedVersionRef.current && filePath) {
        try {
          const remotePresentation = await openBundle(filePath);
          const remoteUpdatedAt = remotePresentation.manifest.updatedAt;

          // If remote version is newer than our last saved version, we have a conflict
          if (remoteUpdatedAt && lastSavedVersionRef.current &&
            new Date(remoteUpdatedAt) > new Date(lastSavedVersionRef.current)) {
            useEditorStore.setState((state) => {
              state.autoSave.status = 'conflict';
            });
            onConflictRef.current?.({
              localVersion: presentation,
              remoteVersion: remotePresentation,
              filePath,
            });
            isSavingRef.current = false;
            return;
          }
        } catch {
          // File doesn't exist or can't be read - that's fine, we'll create it
        }
      }

      // Update timestamps
      const safeMedia = Array.isArray(presentation.manifest.media) ? presentation.manifest.media : [];
      const updatedPresentation = {
        ...presentation,
        manifest: {
          ...presentation.manifest,
          updatedAt: new Date().toISOString(),
          media: safeMedia,
          fonts: Array.isArray(presentation.manifest.fonts) ? presentation.manifest.fonts : [],
        },
      };
      const latestSlideUpdatedAt =
        updatedPresentation.slides.reduce((latest, slide) => {
          if (!slide.updatedAt) return latest;
          if (!latest) return slide.updatedAt;
          return new Date(slide.updatedAt) > new Date(latest) ? slide.updatedAt : latest;
        }, '' as string) || null;

      // Build media refs for pending media
      const mediaRefs: MediaFileRef[] = [];
      for (const media of safeMedia) {
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

      // Update store state - set isDirty to false and update autoSave status
      const now = new Date().toISOString();
      const currentState = useEditorStore.getState();
      const latestSlideUpdatedAtAtCommit =
        currentState.presentation?.slides.reduce((latest, slide) => {
          if (!slide.updatedAt) return latest;
          if (!latest) return slide.updatedAt;
          return new Date(slide.updatedAt) > new Date(latest) ? slide.updatedAt : latest;
        }, '' as string) || null;
      const hasNewerEdits =
        currentState.presentation !== presentation ||
        (latestSlideUpdatedAtAtCommit &&
          latestSlideUpdatedAt &&
          new Date(latestSlideUpdatedAtAtCommit) > new Date(latestSlideUpdatedAt));

      if (!hasNewerEdits) {
        useEditorStore.setState({
          presentation: updatedPresentation,
          filePath: savePath,
          isDirty: false,
          pendingMedia: new Map(),
          pendingFonts: new Map(),
          autoSave: {
            status: 'saved',
            lastSaved: now,
            lastError: null,
          },
        });
      } else {
        useEditorStore.setState((state) => {
          state.autoSave.status = 'saved';
          state.autoSave.lastSaved = now;
          state.autoSave.lastError = null;
          if (!state.filePath) {
            state.filePath = savePath;
          }
        });
      }

      lastSavedVersionRef.current = updatedPresentation.manifest.updatedAt;
      onSavedRef.current?.();

    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : String(error);
      useEditorStore.setState((state) => {
        state.autoSave.status = 'error';
        state.autoSave.lastError = errorMessage;
      });
      onErrorRef.current?.(errorMessage);
      console.error('Auto-save failed:', errorMessage);
    } finally {
      isSavingRef.current = false;
      // #region agent log
      fetch('http://127.0.0.1:7247/ingest/b87235aa-ab93-4b87-9657-fe5221616d91', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ sessionId: 'debug-session', runId: 'pre-fix', hypothesisId: 'A', location: 'use-auto-save.ts:performSave:end', message: 'autosave_end', data: { durationMs: Date.now() - startTimestamp, status: useEditorStore.getState().autoSave.status }, timestamp: Date.now() }) }).catch(() => { });
      // #endregion
    }
  }, []); // No dependencies - uses refs for callbacks

  /**
   * Schedule a debounced save
   */
  const queueSave = useCallback(() => {
    if (typeof window !== 'undefined' && 'requestIdleCallback' in window) {
      (window as typeof window & { requestIdleCallback?: (callback: () => void, options?: { timeout?: number }) => void })
        .requestIdleCallback?.(() => {
          performSave();
        }, { timeout: 1000 });
      return;
    }
    performSave();
  }, [performSave]);

  const scheduleSave = useCallback(() => {
    if (!isEnabled) return;

    // Clear existing timer
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }

    // Update status to pending
    useEditorStore.setState((state) => {
      state.autoSave.status = 'pending';
    });

    // Schedule new save with short debounce - saves quickly after edits stop
    debounceTimerRef.current = setTimeout(() => {
      queueSave();
    }, debounceMs);
  }, [isEnabled, debounceMs, queueSave]);

  /**
   * Immediate save (bypasses debounce)
   */
  const saveNow = useCallback(async () => {
    // Clear any pending debounced save
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = null;
    }

    await performSave();
  }, [performSave]);

  /**
   * Cancel any pending save
   */
  const cancelPendingSave = useCallback(() => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = null;
    }
    // Only reset status if it was pending
    const currentStatus = useEditorStore.getState().autoSave.status;
    if (currentStatus === 'pending') {
      useEditorStore.setState((state) => {
        state.autoSave.status = 'idle';
      });
    }
  }, []);

  // Subscribe to store changes for auto-save trigger
  useEffect(() => {
    if (!isEnabled) return;

    // Subscribe to editor store changes
    const unsubscribe = useEditorStore.subscribe(
      (state, prevState) => {
        const currentPresentationId = state.presentation?.manifest.presentationId;
        const previousPresentationId = prevState.presentation?.manifest.presentationId;
        const presentationChanged = currentPresentationId !== previousPresentationId;

        // Track new/loaded presentations to honor autoSaveOnCreate
        if (presentationChanged) {
          skipInitialSaveRef.current = !state.filePath && !settings.editor.autoSaveOnCreate;
        }

        const transformEnded = prevState.isTransforming && !state.isTransforming;
        if (transformEnded && pendingSaveAfterTransformRef.current && state.isDirty) {
          pendingSaveAfterTransformRef.current = false;
          scheduleSave();
          return;
        }

        if (state.isTransforming) {
          pendingSaveAfterTransformRef.current = true;
          return;
        }

        // Only trigger on isDirty changes (from false to true or continued edits)
        if (state.isDirty && state.presentation) {
          // Check if the presentation content actually changed
          // (not just selection or other non-content state)
          const contentChanged =
            state.presentation !== prevState.presentation ||
            (state.isDirty !== prevState.isDirty);

          if (!contentChanged) return;

          // Skip the initial auto-save for new/unsaved presentations when disabled
          if (presentationChanged && skipInitialSaveRef.current) {
            return;
          }

          if (skipInitialSaveRef.current) {
            // First real edit after creation; allow future auto-saves
            skipInitialSaveRef.current = false;
          }

          scheduleSave();
        }
      }
    );

    return () => {
      unsubscribe();
      cancelPendingSave();
    };
  }, [isEnabled, scheduleSave, cancelPendingSave, settings.editor.autoSaveOnCreate]);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      cancelPendingSave();
    };
  }, [cancelPendingSave]);

  // Interval-based auto-save while actively editing
  useEffect(() => {
    if (!isEnabled) return;

    const intervalSeconds = settings.editor.autosaveInterval;
    if (!intervalSeconds || intervalSeconds <= 0) return;

    intervalTimerRef.current = setInterval(() => {
      const { presentation, isDirty, isTransforming } = useEditorStore.getState();
      if (!presentation || !isDirty || isTransforming) return;
      if (skipInitialSaveRef.current) return;
      saveNow();
    }, intervalSeconds * 1000);

    return () => {
      if (intervalTimerRef.current) {
        clearInterval(intervalTimerRef.current);
        intervalTimerRef.current = null;
      }
    };
  }, [isEnabled, saveNow, settings.editor.autosaveInterval]);

  // Update last saved version when opening a presentation
  useEffect(() => {
    const unsubscribe = useEditorStore.subscribe(
      (state, prevState) => {
        // When a presentation is opened (filePath changes and isDirty is false)
        if (state.filePath !== prevState.filePath && !state.isDirty && state.presentation) {
          lastSavedVersionRef.current = state.presentation.manifest.updatedAt;
        }
        // When a new presentation is created (filePath is null and presentation changes)
        if (!state.filePath && state.presentation !== prevState.presentation) {
          lastSavedVersionRef.current = null;
        }
      }
    );

    return () => {
      unsubscribe();
    };
  }, []);

  return {
    /** Trigger an immediate save */
    saveNow,
    /** Schedule a debounced save */
    scheduleSave,
    /** Cancel any pending save */
    cancelPendingSave,
    /** Whether auto-save is currently enabled */
    isEnabled,
  };
}

export function useThemeAutoSave(options: UseThemeAutoSaveOptions = {}) {
  const {
    debounceMs = 1000,
    enabled = true,
    onConflict,
    onSaved,
    onError,
  } = options;

  const { settings } = useSettingsStore();
  const debounceTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const intervalTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const lastSavedVersionRef = useRef<string | null>(null);
  const isSavingRef = useRef(false);

  const onConflictRef = useRef(onConflict);
  const onSavedRef = useRef(onSaved);
  const onErrorRef = useRef(onError);

  useEffect(() => {
    onConflictRef.current = onConflict;
    onSavedRef.current = onSaved;
    onErrorRef.current = onError;
  }, [onConflict, onSaved, onError]);

  const isEnabled = enabled && settings.editor.autoSaveEnabled;

  const performSave = useCallback(async () => {
    const { themes, isDirty } = useThemeStore.getState();
    if (!isDirty || isSavingRef.current) return;

    isSavingRef.current = true;
    useThemeStore.setState((state) => {
      state.autoSave.status = 'saving';
      state.autoSave.lastError = null;
    });

    try {
      if (lastSavedVersionRef.current) {
        const store = await getThemesStore();
        await store.load();
        const remoteThemes = (await store.get<ThemeTemplate[]>('themes')) ?? [];
        const remoteUpdatedAt = getLatestThemeUpdatedAt(remoteThemes);
        if (
          remoteUpdatedAt &&
          lastSavedVersionRef.current &&
          new Date(remoteUpdatedAt) > new Date(lastSavedVersionRef.current)
        ) {
          useThemeStore.setState((state) => {
            state.autoSave.status = 'conflict';
          });
          onConflictRef.current?.({
            localThemes: themes,
            remoteThemes,
            filePath: THEMES_FILE,
          });
          isSavingRef.current = false;
          return;
        }
      }

      await useThemeStore.getState().saveThemes();

      const now = new Date().toISOString();
      useThemeStore.setState((state) => {
        state.isDirty = false;
        state.autoSave.status = 'saved';
        state.autoSave.lastSaved = now;
        state.autoSave.lastError = null;
      });
      lastSavedVersionRef.current = now;
      onSavedRef.current?.();
    } catch (error) {
      const errorMessage = String(error);
      useThemeStore.setState((state) => {
        state.autoSave.status = 'error';
        state.autoSave.lastError = errorMessage;
      });
      onErrorRef.current?.(errorMessage);
    } finally {
      isSavingRef.current = false;
    }
  }, []);

  const scheduleSave = useCallback(() => {
    if (!isEnabled) return;
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
    }
    useThemeStore.setState((state) => {
      state.autoSave.status = 'pending';
    });
    debounceTimerRef.current = setTimeout(() => {
      performSave();
    }, debounceMs);
  }, [debounceMs, isEnabled, performSave]);

  const cancelPendingSave = useCallback(() => {
    if (debounceTimerRef.current) {
      clearTimeout(debounceTimerRef.current);
      debounceTimerRef.current = null;
    }
    const currentStatus = useThemeStore.getState().autoSave.status;
    if (currentStatus === 'pending') {
      useThemeStore.setState((state) => {
        state.autoSave.status = 'idle';
      });
    }
  }, []);

  useEffect(() => {
    if (!isEnabled) return;

    const unsubscribe = useThemeStore.subscribe((state, prevState) => {
      if (!state.isDirty) return;
      if (state.themes !== prevState.themes || state.isDirty !== prevState.isDirty) {
        scheduleSave();
      }
    });

    return () => {
      unsubscribe();
      cancelPendingSave();
    };
  }, [cancelPendingSave, isEnabled, scheduleSave]);

  useEffect(() => {
    return () => {
      cancelPendingSave();
    };
  }, [cancelPendingSave]);

  useEffect(() => {
    if (!isEnabled) return;

    const intervalSeconds = settings.editor.autosaveInterval;
    if (!intervalSeconds || intervalSeconds <= 0) return;

    intervalTimerRef.current = setInterval(() => {
      const { themes, isDirty } = useThemeStore.getState();
      if (!themes || !isDirty) return;
      performSave();
    }, intervalSeconds * 1000);

    return () => {
      if (intervalTimerRef.current) {
        clearInterval(intervalTimerRef.current);
        intervalTimerRef.current = null;
      }
    };
  }, [isEnabled, performSave, settings.editor.autosaveInterval]);

  useEffect(() => {
    const unsubscribe = useThemeStore.subscribe((state, prevState) => {
      if (!state.isDirty && (state.themes !== prevState.themes || state.isDirty !== prevState.isDirty)) {
        lastSavedVersionRef.current = getLatestThemeUpdatedAt(state.themes);
      }
    });

    return () => {
      unsubscribe();
    };
  }, []);

  return {
    saveNow: performSave,
    scheduleSave,
    cancelPendingSave,
    isEnabled,
  };
}

/**
 * Force resolve a conflict by choosing a version
 */
export async function resolveConflict(
  choice: 'local' | 'remote',
  conflict: ConflictData
): Promise<void> {
  const { localVersion, remoteVersion, filePath } = conflict;
  const now = new Date().toISOString();

  if (choice === 'local') {
    // Overwrite remote with local version
    const mediaRefs: MediaFileRef[] = (localVersion.manifest.media ?? []).map((media) => ({
      id: media.id,
      source_path: `bundle:${media.path}`,
      bundle_path: media.path,
    }));
    const fontRefs: FontFileRef[] = (localVersion.manifest.fonts ?? []).map((font) => ({
      id: font.id,
      source_path: `bundle:${font.path}`,
      bundle_path: font.path,
    }));

    await saveBundle(
      filePath,
      {
        ...localVersion,
        manifest: {
          ...localVersion.manifest,
          updatedAt: now,
        },
      },
      mediaRefs,
      fontRefs
    );

    useEditorStore.setState({
      presentation: localVersion,
      filePath,
      isDirty: false,
      pendingMedia: new Map(),
      pendingFonts: new Map(),
      autoSave: {
        status: 'saved',
        lastSaved: now,
        lastError: null,
      },
    });
  } else {
    // Use remote version, discard local changes
    useEditorStore.setState({
      presentation: remoteVersion,
      filePath,
      isDirty: false,
      pendingMedia: new Map(),
      pendingFonts: new Map(),
      autoSave: {
        status: 'saved',
        lastSaved: remoteVersion.manifest.updatedAt,
        lastError: null,
      },
    });
  }
}

export async function resolveThemeConflict(
  choice: 'local' | 'remote',
  conflict: ThemeConflictData
): Promise<void> {
  const { localThemes, remoteThemes } = conflict;
  const store = await getThemesStore();

  if (choice === 'local') {
    await store.set('themes', localThemes);
    await store.save();
  } else {
    await store.set('themes', remoteThemes);
    await store.save();
    await useThemeStore.getState().loadThemes();
  }

  const now = new Date().toISOString();
  useThemeStore.setState((state) => {
    state.isDirty = false;
    state.autoSave.status = 'saved';
    state.autoSave.lastSaved = now;
    state.autoSave.lastError = null;
  });
}
