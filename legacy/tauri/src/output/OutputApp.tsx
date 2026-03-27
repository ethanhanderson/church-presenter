/**
 * OutputApp - Presentation output window
 * This is rendered in a separate Tauri window
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { listen } from '@tauri-apps/api/event';
import { emit } from '@tauri-apps/api/event';
import { X } from 'lucide-react';
import { OutputStage, type OutputMediaLayer } from '@/components/output/OutputStage';
import type { MediaLayersState, SuppressState, Presentation, Slide } from '@/lib/models';
import { getBackgroundMediaId, resolveSlideBackground } from '@/lib/models';
import type { LivePresentationEvent, LiveStateEvent } from '@/lib/stores/liveStore';
import { loadBundledFonts } from '@/lib/services/fontService';
import { useResolvedMediaUrl } from '@/lib/media/resolveMediaUrl';
import { useSettingsStore } from '@/lib/stores';

export function OutputApp() {
  const { settings } = useSettingsStore();
  const [presentation, setPresentation] = useState<Presentation | null>(null);
  const [presentationPath, setPresentationPath] = useState<string | null>(null);
  const [currentSlide, setCurrentSlide] = useState<Slide | null>(null);
  const aspectRatio = presentation?.manifest.aspectRatio;
  const outputAspectRatio = settings.output.aspectRatio;
  const [isBlackout, setIsBlackout] = useState(false);
  const [isClear, setIsClear] = useState(false);
  const [visibleLayerIds, setVisibleLayerIds] = useState<string[] | null>(null);
  const [suppress, setSuppress] = useState<SuppressState>({
    presentation: false,
    media: false,
  });
  const [isClearing, setIsClearing] = useState<{ presentation: boolean; media: boolean }>({
    presentation: false,
    media: false,
  });
  const [mediaLayers, setMediaLayers] = useState<MediaLayersState>({
    mediaUnderlay: null,
    mediaOverlay: null,
    audio: null,
  });
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
      setMediaLayers(state.mediaLayers);
      setSuppress(state.suppress);
      setIsClearing(state.isClearing);

      // For now, we just track the state
      // In a full implementation, we'd also receive the presentation data
      // or fetch it from a shared store
    });

    return () => {
      unlisten.then((fn) => fn());
    };
  }, [isTauriApp]);
  
  // Callbacks for when clearing animations complete
  // These emit events back to the main window to finish the clear
  const handleClearPresentationComplete = useCallback(() => {
    emit('live:finish-clear-presentation');
  }, []);
  
  const handleClearMediaComplete = useCallback(() => {
    emit('live:finish-clear-media');
  }, []);

  const handleDisableAudience = useCallback(() => {
    if (!isTauriApp) return;
    emit('output:disable-audience');
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

  // Resolve media URLs for media layers
  const resolvedMediaUnderlaySrc = useResolvedMediaUrl({
    mediaId: mediaLayers.mediaUnderlay?.mediaId,
    presentation,
    presentationPath,
  });
  const resolvedMediaOverlaySrc = useResolvedMediaUrl({
    mediaId: mediaLayers.mediaOverlay?.mediaId,
    presentation,
    presentationPath,
  });

  // Resolve background media URL (for image/video backgrounds)
  const effectiveBackground = currentSlide ? resolveSlideBackground(currentSlide) : null;
  const backgroundMediaId = effectiveBackground ? getBackgroundMediaId(effectiveBackground) : undefined;
  const resolvedBackgroundSrc = useResolvedMediaUrl({
    mediaId: backgroundMediaId,
    presentation,
    presentationPath,
  });

  // Build resolved media layers with URLs
  const resolvedMediaLayers = useMemo(() => ({
    ...mediaLayers,
    mediaUnderlay: mediaLayers.mediaUnderlay
      ? { ...mediaLayers.mediaUnderlay, src: resolvedMediaUnderlaySrc ?? undefined } as OutputMediaLayer
      : null,
    mediaOverlay: mediaLayers.mediaOverlay
      ? { ...mediaLayers.mediaOverlay, src: resolvedMediaOverlaySrc ?? undefined } as OutputMediaLayer
      : null,
  }), [mediaLayers, resolvedMediaUnderlaySrc, resolvedMediaOverlaySrc]);

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
          return;
        }

        const slide = presentation.slides.find((s) => s.id === slideId);
        setCurrentSlide(slide || null);

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
    <div
      className="group relative h-screen w-screen bg-black overflow-hidden select-none"
      onContextMenu={(event) => event.preventDefault()}
    >
      <OutputStage
        slide={currentSlide}
        aspectRatio={aspectRatio}
        outputAspectRatio={outputAspectRatio}
        slideSize={presentation?.manifest.slideSize}
        isBlackout={isBlackout}
        isClear={isClear}
        visibleLayerIds={effectiveVisibleLayerIds}
        mediaLayers={resolvedMediaLayers}
        suppress={suppress}
        isClearing={isClearing}
        onClearPresentationComplete={handleClearPresentationComplete}
        onClearMediaComplete={handleClearMediaComplete}
        resolvedBackgroundSrc={resolvedBackgroundSrc}
        className="h-full w-full"
      />
      <button
        type="button"
        aria-label="Disable audience output"
        onClick={handleDisableAudience}
        className="absolute bottom-[2%] right-[2%] z-50 flex items-center justify-center rounded-full bg-white mix-blend-difference opacity-0 transition-opacity group-hover:opacity-100"
        style={{ width: '5vmin', height: '5vmin' }}
      >
        <X className="h-3/5 w-3/5 text-white mix-blend-difference" aria-hidden="true" />
      </button>
    </div>
  );
}

export default OutputApp;
