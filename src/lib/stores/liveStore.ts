/**
 * Live Store - manages the current live presentation state with build step support
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';
import { emit, listen, type UnlistenFn } from '@tauri-apps/api/event';
import type { Presentation, Slide, BuildStep } from '../models';

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
}

export interface LivePresentationEvent {
  presentation: Presentation | null;
  slideId: string | null;
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
      const { currentSlide, currentBuildIndex } = get();
      return calculateVisibleLayerIds(currentSlide, currentBuildIndex);
    },

    get hasMoreBuilds() {
      const { currentBuildIndex, totalBuildSteps } = get();
      return currentBuildIndex < totalBuildSteps - 1;
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
      });

      get().emitState();
      return true;
    },

    resetBuild: () => {
      set((state) => {
        state.currentBuildIndex = -1;
      });
      get().emitState();
    },

    goToBuildIndex: (index: number) => {
      const { totalBuildSteps } = get();
      
      if (index < -1 || index >= totalBuildSteps) return;

      set((state) => {
        state.currentBuildIndex = index;
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
      };

      emit('live:state', event);
    },

    emitPresentation: () => {
      const { presentation, currentSlideId } = get();
      const event: LivePresentationEvent = {
        presentation: presentation ? JSON.parse(JSON.stringify(presentation)) : null,
        slideId: currentSlideId,
      };

      emit('live:presentation', event);
    },

    emitSlide: () => {
      const { currentSlideId } = get();
      emit('live:slide', currentSlideId);
    },

    setupListeners: async () => {
      // Listen for state requests from output window
      const unlisten = await listen('live:request-state', () => {
        get().emitState();
        get().emitPresentation();
        get().emitSlide();
      });

      return unlisten;
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
