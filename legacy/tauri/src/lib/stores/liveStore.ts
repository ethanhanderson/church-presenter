/**
 * Live Store - manages the current live presentation state with build step support
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import { emit, listen, type UnlistenFn } from '@tauri-apps/api/event';
import type {
  Presentation,
  Slide,
  BuildStep,
  MediaLayerId,
  MediaLayersState,
  OutputLayerMedia,
  SuppressState,
  OutputControlGroup,
} from '../models';

// State saved when clearing for undo
export interface ClearedPresentationState {
  slideId: string;
  slideIndex: number;
  buildIndex: number;
}

export interface ClearedMediaState {
  mediaLayers: MediaLayersState;
}

// Event types for IPC
export interface LiveStateEvent {
  presentationId: string | null;
  currentSlideId: string | null;
  currentSlideIndex: number;
  currentBuildIndex: number;
  totalBuildSteps: number;
  visibleLayerIds: string[];
  isBlackout: boolean;
  isClear: boolean;
  mediaLayers: MediaLayersState;
  suppress: SuppressState;
  // Clearing animation state
  isClearing: { presentation: boolean; media: boolean };
}

export interface LivePresentationEvent {
  presentation: Presentation | null;
  slideId: string | null;
  presentationPath: string | null;
}

interface LiveState {
  // State
  isLive: boolean;
  presentation: Presentation | null;
  presentationPath: string | null;
  currentSlideId: string | null;
  currentSlideIndex: number;
  currentBuildIndex: number; // -1 means no builds triggered yet, 0+ means that build step is active
  isBlackout: boolean;
  isClear: boolean;
  mediaLayers: MediaLayersState;
  suppress: SuppressState;
  
  // Clearing animation state (true while transitioning out)
  isClearing: { presentation: boolean; media: boolean };
  
  // Undo state - stores the state before clearing so it can be restored
  clearedPresentationState: ClearedPresentationState | null;
  clearedMediaState: ClearedMediaState | null;

  // Cached computed state (updated via actions, not getters)
  _visibleLayerIds: string[];

  // Computed
  currentSlide: Slide | null;
  nextSlide: Slide | null;
  previousSlide: Slide | null;
  totalSlides: number;

  // Build-related computed
  buildSteps: BuildStep[];
  totalBuildSteps: number;
  visibleLayerIds: string[];
  hasMoreBuilds: boolean;
  
  // Undo state availability
  canUndoClearPresentation: boolean;
  canUndoClearMedia: boolean;

  // Actions
  goLive: (presentation: Presentation, path: string | null) => Promise<void>;
  endLive: () => Promise<void>;

  // Navigation
  goToSlide: (slideId: string) => void;
  goToSlideIndex: (index: number) => void;
  nextSlideAction: () => void;
  previousSlideAction: () => void;

  // Build controls
  advanceBuild: () => boolean; // Returns true if there was a build to advance
  resetBuild: () => void;
  goToBuildIndex: (index: number) => void;

  // Display controls
  setBlackout: (enabled: boolean) => void;
  setClear: (enabled: boolean) => void;
  toggleBlackout: () => void;
  toggleClear: () => void;
  clearPresentation: () => void;
  clearMedia: () => void;
  undoClearPresentation: () => string | null;  // Returns restored slideId or null
  undoClearMedia: () => void;
  // Reset suppress state (for preview mode when not live)
  resetSuppress: () => void;
  // Internal: called when clearing animation completes
  _finishClearPresentation: () => void;
  _finishClearMedia: () => void;

  // Event handling
  emitState: () => void;
  emitPresentation: () => void;
  emitSlide: () => void;
  setupListeners: () => Promise<UnlistenFn>;
  remapPresentationPath: (oldBase: string, newBase: string) => void;
}

// Helper to get onAdvance build steps for a slide
function getAdvanceBuildSteps(slide: Slide | null): BuildStep[] {
  if (!slide?.animations?.buildIn) return [];
  return slide.animations.buildIn.filter(step => step.trigger === 'onAdvance');
}

// Helper to get visible layer IDs based on current build state
function calculateVisibleLayerIds(slide: Slide | null, buildIndex: number): string[] {
  if (!slide) return [];

  const advanceSteps = getAdvanceBuildSteps(slide);
  const visibleIds = new Set<string>();

  // All layers that don't have an onAdvance animation are always visible
  const advanceLayerIds = new Set(advanceSteps.map(s => s.layerId));
  slide.layers.forEach(layer => {
    if (!advanceLayerIds.has(layer.id)) {
      visibleIds.add(layer.id);
    }
  });

  // Add layers that have been revealed by builds
  // buildIndex of -1 means nothing triggered yet
  // buildIndex of 0 means first onAdvance step was triggered
  for (let i = 0; i <= buildIndex; i++) {
    const step = advanceSteps[i];
    if (step) {
      visibleIds.add(step.layerId);
    }
  }

  return Array.from(visibleIds);
}

function applySlideCuesToState(state: LiveState, slide: Slide | null) {
  if (!slide?.mediaCues?.length) return;
  slide.mediaCues.forEach((cue) => {
    const layer = cue.target as MediaLayerId;
    const payload: OutputLayerMedia = {
      mediaId: cue.mediaId,
      mediaType: cue.mediaType,
      fit: cue.fit,
      loop: cue.loop,
      muted: cue.muted,
      autoplay: cue.autoplay,
    };
    state.mediaLayers[layer] = payload;
  });
  // Reset media suppression when new cues are applied
  state.suppress.media = false;
}

// Helper to update cached visible layer IDs in state
function updateVisibleLayerIds(state: LiveState) {
  const slide = state.presentation?.slides.find((s) => s.id === state.currentSlideId) || null;
  const newIds = calculateVisibleLayerIds(slide, state.currentBuildIndex);

  // Only update if the arrays are actually different (by content)
  const currentIds = state._visibleLayerIds;
  if (
    newIds.length !== currentIds.length ||
    newIds.some((id, i) => id !== currentIds[i])
  ) {
    state._visibleLayerIds = newIds;
  }
}

// Default initial state values
const DEFAULT_MEDIA_LAYERS: MediaLayersState = {
  mediaUnderlay: null,
  mediaOverlay: null,
  audio: null,
};

const DEFAULT_SUPPRESS: SuppressState = {
  presentation: false,
  media: false,
};

export const useLiveStore = create<LiveState>()(
  immer((set, get) => ({
    isLive: false,
    presentation: null,
    presentationPath: null,
    currentSlideId: null,
    currentSlideIndex: 0,
    currentBuildIndex: -1,
    isBlackout: false,
    isClear: false,
    mediaLayers: { ...DEFAULT_MEDIA_LAYERS },
    suppress: { ...DEFAULT_SUPPRESS },
    
    // Clearing animation state
    isClearing: { presentation: false, media: false },
    
    // Undo state
    clearedPresentationState: null,
    clearedMediaState: null,

    // Cached computed state
    _visibleLayerIds: [],

    // Computed getters
    get currentSlide() {
      const { presentation, currentSlideId } = get();
      if (!presentation || !currentSlideId) return null;
      return presentation.slides.find((s) => s.id === currentSlideId) || null;
    },

    get nextSlide() {
      const { presentation, currentSlideIndex } = get();
      if (!presentation) return null;
      return presentation.slides[currentSlideIndex + 1] || null;
    },

    get previousSlide() {
      const { presentation, currentSlideIndex } = get();
      if (!presentation) return null;
      return presentation.slides[currentSlideIndex - 1] || null;
    },

    get totalSlides() {
      const { presentation } = get();
      return presentation?.slides.length || 0;
    },

    // Build-related computed
    get buildSteps() {
      const currentSlide = get().currentSlide;
      return getAdvanceBuildSteps(currentSlide);
    },

    get totalBuildSteps() {
      return get().buildSteps.length;
    },

    get visibleLayerIds() {
      // Return the cached array to maintain stable reference
      return get()._visibleLayerIds;
    },

    get hasMoreBuilds() {
      const { currentBuildIndex, totalBuildSteps } = get();
      return currentBuildIndex < totalBuildSteps - 1;
    },
    
    get canUndoClearPresentation() {
      return get().clearedPresentationState !== null;
    },
    
    get canUndoClearMedia() {
      return get().clearedMediaState !== null;
    },

    goLive: async (presentation: Presentation, path: string | null) => {
      set((state) => {
        state.isLive = true;
        state.presentation = presentation;
        state.presentationPath = path;
        state.currentSlideIndex = 0;
        state.currentSlideId = presentation.slides[0]?.id || null;
        state.currentBuildIndex = -1; // Reset build index
        state.isBlackout = false;
        state.isClear = false;
        state.mediaLayers = { ...DEFAULT_MEDIA_LAYERS };
        state.suppress = { ...DEFAULT_SUPPRESS };
        state.isClearing = { presentation: false, media: false };
        state.clearedPresentationState = null;
        state.clearedMediaState = null;
        updateVisibleLayerIds(state);
        const nextSlide =
          presentation.slides.find((s) => s.id === state.currentSlideId) || null;
        applySlideCuesToState(state, nextSlide);
      });

      get().emitState();
      get().emitPresentation();
      get().emitSlide();
    },

    endLive: async () => {
      set((state) => {
        state.isLive = false;
        state.presentation = null;
        state.presentationPath = null;
        state.currentSlideId = null;
        state.currentSlideIndex = 0;
        state.currentBuildIndex = -1;
        state.isBlackout = false;
        state.isClear = false;
        state._visibleLayerIds = [];
        state.mediaLayers = { ...DEFAULT_MEDIA_LAYERS };
        state.suppress = { ...DEFAULT_SUPPRESS };
        state.isClearing = { presentation: false, media: false };
        state.clearedPresentationState = null;
        state.clearedMediaState = null;
      });

      get().emitState();
      get().emitPresentation();
      get().emitSlide();
    },

    goToSlide: (slideId: string) => {
      const { presentation } = get();
      if (!presentation) return;

      const index = presentation.slides.findIndex((s) => s.id === slideId);
      if (index === -1) return;

      set((state) => {
        state.currentSlideId = slideId;
        state.currentSlideIndex = index;
        state.currentBuildIndex = -1; // Reset build index when changing slides
        state.isBlackout = false;
        state.isClear = false;
        state.suppress = { ...DEFAULT_SUPPRESS };
        state.isClearing = { presentation: false, media: false };
        // Clear undo state when navigating to a new slide
        state.clearedPresentationState = null;
        updateVisibleLayerIds(state);
        const nextSlide = presentation.slides.find((s) => s.id === slideId) || null;
        applySlideCuesToState(state, nextSlide);
      });

      get().emitState();
      get().emitSlide();
    },

    goToSlideIndex: (index: number) => {
      const { presentation } = get();
      if (!presentation) return;

      const slide = presentation.slides[index];
      if (!slide) return;

      set((state) => {
        state.currentSlideIndex = index;
        state.currentSlideId = slide.id;
        state.currentBuildIndex = -1; // Reset build index when changing slides
        state.isBlackout = false;
        state.isClear = false;
        state.suppress = { ...DEFAULT_SUPPRESS };
        state.isClearing = { presentation: false, media: false };
        // Clear undo state when navigating to a new slide
        state.clearedPresentationState = null;
        updateVisibleLayerIds(state);
        applySlideCuesToState(state, slide);
      });

      get().emitState();
      get().emitSlide();
    },

    nextSlideAction: () => {
      const { presentation, currentSlideIndex, hasMoreBuilds } = get();
      if (!presentation) return;

      // If there are more build steps, advance build first
      if (hasMoreBuilds) {
        get().advanceBuild();
        return;
      }

      // Otherwise, go to next slide
      const nextIndex = currentSlideIndex + 1;
      if (nextIndex >= presentation.slides.length) return;

      get().goToSlideIndex(nextIndex);
    },

    previousSlideAction: () => {
      const { currentSlideIndex, currentBuildIndex } = get();

      // If we have builds triggered, reset them first
      if (currentBuildIndex >= 0) {
        get().resetBuild();
        return;
      }

      // Otherwise, go to previous slide
      if (currentSlideIndex <= 0) return;

      get().goToSlideIndex(currentSlideIndex - 1);
    },

    advanceBuild: () => {
      const { currentBuildIndex, totalBuildSteps } = get();

      if (currentBuildIndex >= totalBuildSteps - 1) {
        return false; // No more builds
      }

      set((state) => {
        state.currentBuildIndex = state.currentBuildIndex + 1;
        updateVisibleLayerIds(state);
      });

      get().emitState();
      return true;
    },

    resetBuild: () => {
      set((state) => {
        state.currentBuildIndex = -1;
        updateVisibleLayerIds(state);
      });
      get().emitState();
    },

    goToBuildIndex: (index: number) => {
      const { totalBuildSteps } = get();

      if (index < -1 || index >= totalBuildSteps) return;

      set((state) => {
        state.currentBuildIndex = index;
        updateVisibleLayerIds(state);
      });

      get().emitState();
    },

    setBlackout: (enabled: boolean) => {
      set((state) => {
        state.isBlackout = enabled;
        if (enabled) state.isClear = false;
      });
      get().emitState();
    },

    setClear: (enabled: boolean) => {
      set((state) => {
        state.isClear = enabled;
        if (enabled) state.isBlackout = false;
      });
      get().emitState();
    },

    toggleBlackout: () => {
      get().setBlackout(!get().isBlackout);
    },

    toggleClear: () => {
      get().setClear(!get().isClear);
    },

    clearPresentation: () => {
      const { currentSlideId, currentSlideIndex, currentBuildIndex, suppress, isClearing } = get();
      
      // Don't clear if already clearing or already suppressed
      if (isClearing.presentation || suppress.presentation) return;
      
      set((state) => {
        // Save state for undo (only if there's something to clear)
        if (state.currentSlideId) {
          state.clearedPresentationState = {
            slideId: state.currentSlideId,
            slideIndex: state.currentSlideIndex,
            buildIndex: state.currentBuildIndex,
          };
        }
        // Start clearing animation
        state.isClearing.presentation = true;
      });
      get().emitState();
    },
    
    _finishClearPresentation: () => {
      set((state) => {
        state.isClearing.presentation = false;
        state.suppress.presentation = true;
        // Clear the current slide - "no slide selected" is the cleared state
        state.currentSlideId = null;
        state.currentSlideIndex = -1;
        state.currentBuildIndex = -1;
        state._visibleLayerIds = [];
      });
      get().emitState();
      get().emitSlide();
    },

    clearMedia: () => {
      const { mediaLayers, suppress, isClearing } = get();
      
      // Don't clear if already clearing or already suppressed
      if (isClearing.media || suppress.media) return;
      
      // Check if there's any media to clear
      const hasMedia = mediaLayers.mediaUnderlay || mediaLayers.mediaOverlay || mediaLayers.audio;
      
      set((state) => {
        // Save state for undo (only if there's something to clear)
        if (hasMedia) {
          state.clearedMediaState = {
            mediaLayers: { ...state.mediaLayers },
          };
        }
        // Start clearing animation
        state.isClearing.media = true;
      });
      get().emitState();
    },
    
    _finishClearMedia: () => {
      set((state) => {
        state.isClearing.media = false;
        state.mediaLayers = { ...DEFAULT_MEDIA_LAYERS };
        state.suppress.media = true;
      });
      get().emitState();
    },
    
    undoClearPresentation: () => {
      const { clearedPresentationState, presentation } = get();
      if (!clearedPresentationState) return null;
      
      const slideId = clearedPresentationState.slideId;
      
      // If we have a live presentation, verify the slide exists and restore fully
      if (presentation) {
        const slide = presentation.slides.find(s => s.id === slideId);
        if (!slide) return null;
        
        set((state) => {
          state.currentSlideId = slideId;
          state.currentSlideIndex = clearedPresentationState.slideIndex;
          state.currentBuildIndex = clearedPresentationState.buildIndex;
          state.suppress.presentation = false;
          state.isClearing.presentation = false;
          state.clearedPresentationState = null;
          updateVisibleLayerIds(state);
          applySlideCuesToState(state, slide);
        });
      } else {
        // Preview mode - just reset suppress state
        set((state) => {
          state.suppress.presentation = false;
          state.isClearing.presentation = false;
          state.clearedPresentationState = null;
        });
      }
      
      get().emitState();
      get().emitSlide();
      
      // Return the slideId so the caller can restore selectedSlideId
      return slideId;
    },
    
    undoClearMedia: () => {
      const { clearedMediaState } = get();
      if (!clearedMediaState) return;
      
      set((state) => {
        state.mediaLayers = { ...clearedMediaState.mediaLayers };
        state.suppress.media = false;
        state.isClearing.media = false;
        state.clearedMediaState = null;
      });
      
      get().emitState();
    },
    
    resetSuppress: () => {
      set((state) => {
        state.suppress = { ...DEFAULT_SUPPRESS };
        state.isClearing = { presentation: false, media: false };
        state.clearedPresentationState = null;
        state.clearedMediaState = null;
      });
      get().emitState();
    },

    emitState: () => {
      const {
        presentation,
        currentSlideId,
        currentSlideIndex,
        currentBuildIndex,
        totalBuildSteps,
        visibleLayerIds,
        isBlackout,
        isClear,
        mediaLayers,
        suppress,
        isClearing,
      } = get();

      const event: LiveStateEvent = {
        presentationId: presentation?.manifest.presentationId || null,
        currentSlideId,
        currentSlideIndex,
        currentBuildIndex,
        totalBuildSteps,
        visibleLayerIds,
        isBlackout,
        isClear,
        mediaLayers,
        suppress,
        isClearing,
      };

      emit('live:state', event);
    },

    emitPresentation: () => {
      const { presentation, currentSlideId, presentationPath } = get();
      const event: LivePresentationEvent = {
        presentation: presentation ? JSON.parse(JSON.stringify(presentation)) : null,
        slideId: currentSlideId,
        presentationPath,
      };

      emit('live:presentation', event);
    },

    emitSlide: () => {
      const { currentSlideId } = get();
      emit('live:slide', currentSlideId);
    },

    setupListeners: async () => {
      const unlistenState = await listen('live:request-state', () => {
        get().emitState();
        get().emitPresentation();
        get().emitSlide();
      });
      const unlistenNext = await listen('live:next', () => {
        get().nextSlideAction();
      });
      const unlistenPrevious = await listen('live:previous', () => {
        get().previousSlideAction();
      });
      // Listen for clearing animation completion from output window
      const unlistenFinishClearPresentation = await listen('live:finish-clear-presentation', () => {
        get()._finishClearPresentation();
      });
      const unlistenFinishClearMedia = await listen('live:finish-clear-media', () => {
        get()._finishClearMedia();
      });

      return () => {
        unlistenState();
        unlistenNext();
        unlistenPrevious();
        unlistenFinishClearPresentation();
        unlistenFinishClearMedia();
      };
    },

    remapPresentationPath: (oldBase, newBase) => {
      const normalizePath = (path: string) => path.replace(/\\/g, '/');
      const normalizedOld = normalizePath(oldBase);
      const normalizedNew = normalizePath(newBase);

      set((state) => {
        const current = state.presentationPath;
        if (!current) return;
        const normalizedPath = normalizePath(current);
        if (!normalizedPath.startsWith(`${normalizedOld}/`)) return;
        state.presentationPath = `${normalizedNew}/${normalizedPath.slice(normalizedOld.length + 1)}`;
      });
    },
  }))
);

// Helper hook to get computed values
export function useCurrentSlide() {
  const { presentation, currentSlideId } = useLiveStore();
  if (!presentation || !currentSlideId) return null;
  return presentation.slides.find((s) => s.id === currentSlideId) || null;
}

export function useNextSlide() {
  const { presentation, currentSlideIndex } = useLiveStore();
  if (!presentation) return null;
  return presentation.slides[currentSlideIndex + 1] || null;
}

export function usePreviousSlide() {
  const { presentation, currentSlideIndex } = useLiveStore();
  if (!presentation) return null;
  return presentation.slides[currentSlideIndex - 1] || null;
}

export function useVisibleLayerIds() {
  const { visibleLayerIds } = useLiveStore();
  return visibleLayerIds;
}

export function useBuildState() {
  const { currentBuildIndex, totalBuildSteps, hasMoreBuilds } = useLiveStore();
  return { currentBuildIndex, totalBuildSteps, hasMoreBuilds };
}
