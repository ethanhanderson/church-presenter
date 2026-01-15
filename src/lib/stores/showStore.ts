/**
 * Show Store - manages show page selection state
 */

import { create } from 'zustand';
import { immer } from 'zustand/middleware/immer';

interface ShowState {
  selectedSlideId: string | null;
  setSelectedSlideId: (slideId: string | null) => void;
  clearSelectedSlide: () => void;
}

export const useShowStore = create<ShowState>()(
  immer((set) => ({
    selectedSlideId: null,
    setSelectedSlideId: (slideId) => {
      set((state) => {
        state.selectedSlideId = slideId;
      });
    },
    clearSelectedSlide: () => {
      set((state) => {
        state.selectedSlideId = null;
      });
    },
  }))
);
