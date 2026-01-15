/**
 * useAutoSave - Hook for automatic saving of presentations with debounce
 */

import { useEffect, useRef, useCallback } from 'react';
import { useEditorStore, useSettingsStore } from '@/lib/stores';
import { saveBundle, openBundle, type FontFileRef, type MediaFileRef } from '@/lib/tauri-api';
import { generatePresentationPath, generateSongPresentationPath } from '@/lib/services/appDataService';
import type { Presentation } from '@/lib/models';

export type AutoSaveStatus = 'idle' | 'pending' | 'saving' | 'saved' | 'error' | 'conflict';

interface ConflictData {
  localVersion: Presentation;
  remoteVersion: Presentation;
  filePath: string;
}

interface UseAutoSaveOptions {
  /** Debounce delay in milliseconds for presentation content changes (default: 300) */
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

/**
 * Generate a file path for a presentation in the app data directory
 */
export function useAutoSave(options: UseAutoSaveOptions = {}) {
  const {
    debounceMs = 300, // Short debounce for quick saves after edits stop
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
    }
  }, []); // No dependencies - uses refs for callbacks

  /**
   * Schedule a debounced save
   */
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
      performSave();
    }, debounceMs);
  }, [isEnabled, debounceMs, performSave]);

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
      const { presentation, isDirty } = useEditorStore.getState();
      if (!presentation || !isDirty) return;
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
