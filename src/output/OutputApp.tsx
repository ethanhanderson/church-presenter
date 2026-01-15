/**
 * OutputApp - Presentation output window
 * This is rendered in a separate Tauri window
 */

import { useEffect, useMemo, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { emit } from '@tauri-apps/api/event';
import { AnimatedSlideStage } from '@/components/preview/AnimatedSlideStage';
import type { Presentation, Slide, Theme } from '@/lib/models';
import type { LivePresentationEvent, LiveStateEvent } from '@/lib/stores/liveStore';
import { loadBundledFonts } from '@/lib/services/fontService';

export function OutputApp() {
  const [presentation, setPresentation] = useState<Presentation | null>(null);
  const [presentationPath, setPresentationPath] = useState<string | null>(null);
  const [currentSlide, setCurrentSlide] = useState<Slide | null>(null);
  const [theme, setTheme] = useState<Theme | null>(null);
  const [isBlackout, setIsBlackout] = useState(false);
  const [isClear, setIsClear] = useState(false);
  const [visibleLayerIds, setVisibleLayerIds] = useState<string[] | null>(null);
  const isTauriApp =
    typeof window !== 'undefined' &&
    ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);

  useEffect(() => {
    if (!isTauriApp) return;
    // Request initial state
    emit('live:request-state');

    // Listen for live state updates
    const unlisten = listen<LiveStateEvent>('live:state', (event) => {
      const state = event.payload;
      
      setIsBlackout(state.isBlackout);
      setIsClear(state.isClear);
      setVisibleLayerIds(state.visibleLayerIds ?? null);

      // For now, we just track the state
      // In a full implementation, we'd also receive the presentation data
      // or fetch it from a shared store
    });

    return () => {
      unlisten.then((fn) => fn());
    };
  }, [isTauriApp]);

  useEffect(() => {
    if (!isTauriApp) return;

    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.ctrlKey || event.metaKey || event.altKey) return;

      const key = event.key;
      if (key === 'ArrowRight' || key === 'ArrowDown' || key === 'PageDown' || key === ' ' || key === 'Enter') {
        event.preventDefault();
        emit('live:next');
        return;
      }
      if (key === 'ArrowLeft' || key === 'ArrowUp' || key === 'PageUp' || key === 'Backspace') {
        event.preventDefault();
        emit('live:previous');
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isTauriApp]);

  const effectiveVisibleLayerIds = useMemo(() => {
    if (!currentSlide) return undefined;
    const hasAdvanceBuilds = currentSlide.animations?.buildIn?.some(
      (step) => step.trigger === 'onAdvance'
    );
    if (!hasAdvanceBuilds && (!visibleLayerIds || visibleLayerIds.length === 0)) {
      return undefined;
    }
    return visibleLayerIds ?? undefined;
  }, [currentSlide, visibleLayerIds]);

  // Listen for presentation data updates
  useEffect(() => {
    if (!isTauriApp) return;
    const unlisten = listen<LivePresentationEvent>(
      'live:presentation',
      (event) => {
        const { presentation, slideId } = event.payload;
        setPresentation(presentation);
        setPresentationPath(event.payload.presentationPath ?? null);

        if (!presentation || !slideId) {
          setCurrentSlide(null);
          setTheme(null);
          return;
        }

        const slide = presentation.slides.find((s) => s.id === slideId);
        setCurrentSlide(slide || null);

        const activeTheme = presentation.themes.find(
          (t) => t.id === presentation.manifest.themeId
        );
        setTheme(activeTheme || null);
      }
    );

    return () => {
      unlisten.then((fn) => fn());
    };
  }, [isTauriApp]);

  useEffect(() => {
    void loadBundledFonts(presentation, presentationPath);
  }, [presentation, presentationPath]);

  // Listen for slide changes
  useEffect(() => {
    if (!isTauriApp) return;
    const unlisten = listen<string | null>('live:slide', (event) => {
      if (presentation && event.payload) {
        const slide = presentation.slides.find((s) => s.id === event.payload);
        setCurrentSlide(slide || null);
      } else if (!event.payload) {
        setCurrentSlide(null);
      }
    });

    return () => {
      unlisten.then((fn) => fn());
    };
  }, [isTauriApp, presentation]);

  return (
    <div className="h-screen w-screen bg-black overflow-hidden">
      <AnimatedSlideStage
        slide={currentSlide}
        theme={theme}
        isBlackout={isBlackout}
        isClear={isClear}
        visibleLayerIds={effectiveVisibleLayerIds}
        className="h-full w-full"
      />
    </div>
  );
}

export default OutputApp;
