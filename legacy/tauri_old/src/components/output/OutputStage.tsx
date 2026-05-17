/**
 * OutputStage - layered compositor for output + preview
 * 
 * Layer order (bottom to top):
 * 1. Media Underlay (behind everything, only visible through transparent backgrounds)
 * 2. Presentation Background (solid/gradient/image/video)
 * 3. Media Overlay (between background and slide elements)
 * 4. Slide Elements (text, shapes, etc.)
 * 5. Blackout/Clear overlays
 */

import { useEffect, useMemo, useRef } from 'react';
import { AnimatePresence, motion, type Variants } from 'motion/react';
import { AnimatedSlideStage } from '@/components/preview/AnimatedSlideStage';
import { BackgroundMedia } from '@/components/preview/BackgroundMedia';
import type {
  Background,
  OutputLayerMedia,
  MediaLayersState,
  SuppressState,
  Slide,
  SlideTransitionType,
} from '@/lib/models';
import { getBackgroundStyle, resolveSlideBackground } from '@/lib/models';
import { cn } from '@/lib/utils';

export interface OutputMediaLayer extends OutputLayerMedia {
  src?: string;
}

interface OutputStageProps {
  slide: Slide | null;
  aspectRatio?: '16:9' | '4:3' | '16:10';
  outputAspectRatio?: '16:9' | '4:3' | '16:10';
  slideSize?: { width: number; height: number };
  isBlackout?: boolean;
  isClear?: boolean;
  visibleLayerIds?: string[];
  className?: string;
  // New layer model
  mediaLayers?: MediaLayersState | null;
  suppress?: SuppressState;
  // Clearing animation state
  isClearing?: { presentation: boolean; media: boolean };
  // Callbacks for when clearing animation completes
  onClearPresentationComplete?: () => void;
  onClearMediaComplete?: () => void;
  // Resolved background media source (for image/video backgrounds)
  resolvedBackgroundSrc?: string | null;
}

export function OutputStage({
  slide,
  aspectRatio,
  outputAspectRatio,
  slideSize,
  isBlackout = false,
  isClear = false,
  visibleLayerIds,
  className,
  mediaLayers,
  suppress = { presentation: false, media: false },
  isClearing = { presentation: false, media: false },
  onClearPresentationComplete,
  onClearMediaComplete,
  resolvedBackgroundSrc,
}: OutputStageProps) {
  const outputAspectClass = getAspectClass(outputAspectRatio ?? aspectRatio);
  const suppressPresentation = suppress.presentation;
  const suppressMedia = suppress.media;
  const clearingPresentation = isClearing.presentation;
  const clearingMedia = isClearing.media;
  
  // Track when we've started clearing to trigger exit animation
  const clearingPresentationRef = useRef(false);
  const clearingMediaRef = useRef(false);
  
  // Get transition settings from the slide for clearing animation
  const transition = slide?.animations?.transition || { type: 'fade' as SlideTransitionType, duration: 300 };
  const transitionDuration = transition.duration / 1000;
  const transitionVariants = getTransitionVariants(transition.type);

  // Handle clearing animation completion for presentation
  useEffect(() => {
    if (clearingPresentation && !clearingPresentationRef.current) {
      clearingPresentationRef.current = true;
      // Wait for the animation to complete, then call the callback
      const timer = setTimeout(() => {
        onClearPresentationComplete?.();
        clearingPresentationRef.current = false;
      }, transition.duration);
      return () => clearTimeout(timer);
    }
    if (!clearingPresentation) {
      clearingPresentationRef.current = false;
    }
  }, [clearingPresentation, transition.duration, onClearPresentationComplete]);

  // Handle clearing animation completion for media
  useEffect(() => {
    if (clearingMedia && !clearingMediaRef.current) {
      clearingMediaRef.current = true;
      const timer = setTimeout(() => {
        onClearMediaComplete?.();
        clearingMediaRef.current = false;
      }, transition.duration);
      return () => clearTimeout(timer);
    }
    if (!clearingMedia) {
      clearingMediaRef.current = false;
    }
  }, [clearingMedia, transition.duration, onClearMediaComplete]);

  // Get the effective background from slide
  const effectiveBackground = useMemo<Background | null>(() => {
    if (!slide) return null;
    return resolveSlideBackground(slide);
  }, [slide]);

  // Compute background style for solid/gradient backgrounds
  const baseBackgroundStyle = useMemo(() => {
    if (!effectiveBackground) return undefined;
    if (suppressPresentation) return {};
    return getBackgroundStyle(effectiveBackground);
  }, [effectiveBackground, suppressPresentation]);

  // Get media underlay layer (behind everything)
  const mediaUnderlay = useMemo<OutputMediaLayer | null>(() => {
    if (suppressMedia) return null;
    const layer = mediaLayers?.mediaUnderlay;
    if (!layer) return null;
    return layer as OutputMediaLayer;
  }, [mediaLayers?.mediaUnderlay, suppressMedia]);

  // Get media overlay layer (between background and elements)
  const mediaOverlay = useMemo<OutputMediaLayer | null>(() => {
    if (suppressMedia) return null;
    const layer = mediaLayers?.mediaOverlay;
    if (!layer) return null;
    return layer as OutputMediaLayer;
  }, [mediaLayers?.mediaOverlay, suppressMedia]);

  // Render presentation background media (image/video) if applicable
  const backgroundSrc = useMemo(() => {
    if (suppressPresentation) return null;
    return resolvedBackgroundSrc ?? null;
  }, [resolvedBackgroundSrc, suppressPresentation]);
  
  // Show presentation content if not suppressed (even while clearing, to show exit animation)
  const showPresentation = !suppressPresentation;
  // Show media content if not suppressed (even while clearing, to show exit animation)
  const showMedia = !suppressMedia;

  return (
    <div className={cn('relative w-full h-full bg-black overflow-hidden', className)}>
      <div className="absolute inset-0 flex items-center justify-center">
        <div className={cn('relative w-full max-h-full', outputAspectClass)}>
          {/* 1. Media Underlay - behind everything, only visible through transparent backgrounds */}
          <div className="absolute inset-0">
            <AnimatePresence mode="wait">
              {showMedia && mediaUnderlay && !clearingMedia && (
                <motion.div
                  key="media-underlay"
                  className="w-full h-full"
                  initial="initial"
                  animate="animate"
                  exit="exit"
                  variants={transitionVariants}
                  transition={{ duration: transitionDuration }}
                >
                  <MediaLayer media={mediaUnderlay} />
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          {/* 2. Presentation Background - solid/gradient/image/video */}
          <AnimatePresence mode="wait">
            {showPresentation && !clearingPresentation && slide && (
              <motion.div
                key={`presentation-bg-${slide.id}`}
                className="absolute inset-0"
                style={baseBackgroundStyle}
                initial="initial"
                animate="animate"
                exit="exit"
                variants={transitionVariants}
                transition={{ duration: transitionDuration }}
              >
                <BackgroundMedia
                  background={effectiveBackground}
                  src={backgroundSrc}
                  className="absolute inset-0"
                />
              </motion.div>
            )}
          </AnimatePresence>

          {/* 3. Media Overlay - between background and slide elements */}
          <div className="absolute inset-0">
            <AnimatePresence mode="wait">
              {showMedia && mediaOverlay && !clearingMedia && (
                <motion.div
                  key="media-overlay"
                  className="w-full h-full"
                  initial="initial"
                  animate="animate"
                  exit="exit"
                  variants={transitionVariants}
                  transition={{ duration: transitionDuration }}
                >
                  <MediaLayer media={mediaOverlay} />
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          {/* 4. Slide Elements - text, shapes, media layers, etc. */}
          <div className="absolute inset-0">
            <AnimatePresence mode="wait">
              {showPresentation && !clearingPresentation && slide && (
                <motion.div
                  key={slide.id}
                  className="w-full h-full"
                  initial="initial"
                  animate="animate"
                  exit="exit"
                  variants={transitionVariants}
                  transition={{ duration: transitionDuration }}
                >
                  <AnimatedSlideStage
                    slide={slide}
                    aspectRatio={aspectRatio}
                    slideSize={slideSize}
                    isBlackout={false}
                    isClear={false}
                    visibleLayerIds={visibleLayerIds}
                    backgroundMode="transparent"
                    className="h-full w-full"
                  />
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          {/* 5. Blackout / Clear overlays */}
          {isBlackout && <div className="absolute inset-0 bg-black" />}
          {isClear && (
            <div className="absolute inset-0 bg-black flex items-center justify-center">
              <div className="text-white/20 text-2xl font-light">Church Presenter</div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

// Get variants for different transition types (for clearing animation)
function getTransitionVariants(type: SlideTransitionType): Variants {
  switch (type) {
    case 'none':
      return {
        initial: { opacity: 1 },
        animate: { opacity: 1 },
        exit: { opacity: 0 },
      };

    case 'fade':
      return {
        initial: { opacity: 0 },
        animate: { opacity: 1 },
        exit: { opacity: 0 },
      };

    case 'slide-left':
      return {
        initial: { x: '100%', opacity: 0 },
        animate: { x: 0, opacity: 1 },
        exit: { x: '-100%', opacity: 0 },
      };

    case 'slide-right':
      return {
        initial: { x: '-100%', opacity: 0 },
        animate: { x: 0, opacity: 1 },
        exit: { x: '100%', opacity: 0 },
      };

    case 'slide-up':
      return {
        initial: { y: '100%', opacity: 0 },
        animate: { y: 0, opacity: 1 },
        exit: { y: '-100%', opacity: 0 },
      };

    case 'slide-down':
      return {
        initial: { y: '-100%', opacity: 0 },
        animate: { y: 0, opacity: 1 },
        exit: { y: '100%', opacity: 0 },
      };

    case 'zoom-in':
      return {
        initial: { scale: 0.8, opacity: 0 },
        animate: { scale: 1, opacity: 1 },
        exit: { scale: 1.2, opacity: 0 },
      };

    case 'zoom-out':
      return {
        initial: { scale: 1.2, opacity: 0 },
        animate: { scale: 1, opacity: 1 },
        exit: { scale: 0.8, opacity: 0 },
      };

    case 'flip':
      return {
        initial: { rotateY: 90, opacity: 0 },
        animate: { rotateY: 0, opacity: 1 },
        exit: { rotateY: -90, opacity: 0 },
      };

    default:
      return {
        initial: { opacity: 0 },
        animate: { opacity: 1 },
        exit: { opacity: 0 },
      };
  }
}

function getAspectClass(aspectRatio?: '16:9' | '4:3' | '16:10'): string {
  if (aspectRatio === '4:3') return 'aspect-[4/3]';
  if (aspectRatio === '16:10') return 'aspect-[16/10]';
  return 'aspect-video';
}

function MediaLayer({ media }: { media: OutputMediaLayer | null }) {
  if (!media || media.mediaType === 'audio') return null;
  const src = media.src ?? media.mediaId;
  if (!src) return null;

  const fitClass =
    media.fit === 'contain'
      ? 'object-contain'
      : media.fit === 'fill'
        ? 'object-fill'
        : media.fit === 'none'
          ? 'object-none'
          : media.fit === 'scale-down'
            ? 'object-scale-down'
            : 'object-cover';
  const mediaClass = cn('h-full w-full', fitClass);

  if (media.mediaType === 'video') {
    return (
      <video
        src={src}
        className={mediaClass}
        loop={media.loop}
        muted={media.muted}
        autoPlay={media.autoplay}
        playsInline
      />
    );
  }

  return <img src={src} alt="" className={mediaClass} draggable={false} />;
}
