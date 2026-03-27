/**
 * Show Store - manages show page selection state
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';

interface ShowState {
  selectedSlideId: string | null;
  selectedSlideSource: 'user' | 'live' | null;
  setSelectedSlideId: (slideId: string | null, source?: 'user' | 'live') => void;
  clearSelectedSlide: (source?: 'user' | 'live') => void;
}

export const useShowStore = create<ShowState>()(
  immer((set) => ({
    selectedSlideId: null,
    selectedSlideSource: null,
    setSelectedSlideId: (slideId, source = 'user') => {
      set((state) => {
        const nextSource = slideId ? source : null;
        if (
          state.selectedSlideId === slideId &&
          state.selectedSlideSource === nextSource
        ) {
          return;
        }
        state.selectedSlideId = slideId;
        state.selectedSlideSource = nextSource;
      });
    },
    clearSelectedSlide: (source = 'user') => {
      set((state) => {
        if (state.selectedSlideId === null && state.selectedSlideSource === null) {
          return;
        }
        state.selectedSlideId = null;
        state.selectedSlideSource = null;
      });
    },
  }))
);
