/**
 * AnimatedSlideStage - Wrapper for slide transitions and build animations using Motion
 */

import { AnimatePresence, motion, useReducedMotion, type Variants } from 'motion/react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type { Slide, SlideTransitionType, AnimationPreset, Background } from '@/lib/models';
import { BackgroundMedia } from '@/components/preview/BackgroundMedia';
import {
  LayerContent as SlideLayerContent,
  getLayerContainerStyle,
  getLayerContentStyle,
  getLayerTransformStyle,
} from '@/components/preview/slide-layer-content';
import { cn } from '@/lib/utils';
import { getBackgroundStyle, resolveSlideBackground } from '@/lib/models';

interface AnimatedSlideStageProps {
  slide: Slide | null;
  aspectRatio?: '16:9' | '4:3' | '16:10';
  slideSize?: { width: number; height: number };
  className?: string;
  isBlackout?: boolean;
  isClear?: boolean;
  showSafeArea?: boolean;
  visibleLayerIds?: string[]; // If provided, controls which layers are visible (for builds)
  backgroundMode?: 'auto' | 'transparent';
  backgroundMediaSrc?: string | null;
}

export function AnimatedSlideStage({
  slide,
  aspectRatio,
  slideSize,
  className,
  isBlackout = false,
  isClear = false,
  showSafeArea = false,
  visibleLayerIds,
  backgroundMode = 'auto',
  backgroundMediaSrc,
}: AnimatedSlideStageProps) {
  const shouldReduceMotion = useReducedMotion();
  
  // Get transition settings from the slide
  const transition = slide?.animations?.transition || { type: 'fade' as SlideTransitionType, duration: 300 };
  const transitionDuration = transition.duration / 1000; // Convert to seconds

  // Get animation variants based on transition type
  const variants = getTransitionVariants(transition.type, shouldReduceMotion);

  return (
    <div className={cn('relative overflow-hidden', className)}>
      <AnimatePresence mode="wait" initial={false}>
        {isBlackout ? (
          <motion.div
            key="blackout"
            className="absolute inset-0 bg-black"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.3 }}
          />
        ) : isClear ? (
          <motion.div
            key="clear"
            className="absolute inset-0 bg-black flex items-center justify-center"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.3 }}
          >
            <div className="text-white/20 text-2xl font-light">
              Church Presenter
            </div>
          </motion.div>
        ) : slide ? (
          <motion.div
            key={slide.id}
            className="absolute inset-0"
            initial="initial"
            animate="animate"
            exit="exit"
            variants={variants}
            transition={{
              duration: transitionDuration,
              ease: (transition.easing || 'easeOut') as any,
            }}
          >
            <AnimatedSlideContent
              slide={slide}
              aspectRatio={aspectRatio}
              slideSize={slideSize}
              showSafeArea={showSafeArea}
              visibleLayerIds={visibleLayerIds}
              reduceMotion={shouldReduceMotion || false}
              backgroundMode={backgroundMode}
              backgroundMediaSrc={backgroundMediaSrc}
            />
          </motion.div>
        ) : (
          <motion.div
            key="empty"
            className="absolute inset-0 bg-black flex items-center justify-center"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
          >
            <div className="text-white/40 text-sm">No slide selected</div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// Animated slide content with layer animations
interface AnimatedSlideContentProps {
  slide: Slide;
  aspectRatio?: '16:9' | '4:3' | '16:10';
  slideSize?: { width: number; height: number };
  showSafeArea: boolean;
  visibleLayerIds?: string[];
  reduceMotion: boolean;
  backgroundMode: 'auto' | 'transparent';
  backgroundMediaSrc?: string | null;
}

function AnimatedSlideContent({
  slide,
  aspectRatio,
  slideSize,
  showSafeArea,
  visibleLayerIds,
  reduceMotion,
  backgroundMode,
  backgroundMediaSrc,
}: AnimatedSlideContentProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [textScale, setTextScale] = useState(1);
  const baseSize = useMemo(
    () => getBaseSlideSize(aspectRatio, slideSize),
    [aspectRatio, slideSize]
  );
  const aspectClass = getAspectClass(aspectRatio);

  // Get effective background
  const background = useMemo(() => resolveSlideBackground(slide), [slide]);
  const effectiveBackgroundSrc = backgroundMediaSrc ?? undefined;

  // Text scaling for responsive text
  useEffect(() => {
    if (!containerRef.current) return;

    const updateScale = (width: number, height: number) => {
      if (!width || !height) return;
      const nextScale = Math.min(width / baseSize.width, height / baseSize.height) || 1;
      setTextScale((prev) => (Math.abs(prev - nextScale) < 0.001 ? prev : nextScale));
    };

    const observer = new ResizeObserver((entries) => {
      for (const entry of entries) {
        updateScale(entry.contentRect.width, entry.contentRect.height);
      }
    });

    observer.observe(containerRef.current);
    updateScale(containerRef.current.clientWidth, containerRef.current.clientHeight);

    return () => observer.disconnect();
  }, [baseSize.height, baseSize.width]);

  // Get build animation for a layer
  const getLayerBuildAnimation = (layerId: string): { preset: AnimationPreset; duration: number } | null => {
    if (!slide.animations?.buildIn) return null;
    const step = slide.animations.buildIn.find(s => s.layerId === layerId && s.trigger === 'onAdvance');
    if (!step) return null;
    return { preset: step.preset, duration: step.duration };
  };

  // Determine if a layer should be visible
  const isLayerVisible = (layerId: string): boolean => {
    if (!visibleLayerIds) return true; // If not controlling visibility, show all
    return visibleLayerIds.includes(layerId);
  };

  return (
    <div
      ref={containerRef}
      className={cn('relative w-full h-full overflow-hidden', aspectClass)}
      style={backgroundMode === 'transparent' ? undefined : getBackgroundStyle(background)}
    >
      {backgroundMode !== 'transparent' && (
        <BackgroundMedia
          background={background}
          src={effectiveBackgroundSrc}
          className="absolute inset-0"
        />
      )}
      {/* Safe area guides */}
      {showSafeArea && (
        <div className="absolute inset-0 pointer-events-none">
          <div className="absolute inset-[5%] border border-dashed border-white/30" />
          <div className="absolute inset-[10%] border border-dashed border-white/20" />
        </div>
      )}

      {/* Animated Layers */}
      <AnimatePresence>
        {slide.layers.map((layer) => {
          if (!layer.visible) return null;
          
          const visible = isLayerVisible(layer.id);
          const buildAnim = getLayerBuildAnimation(layer.id);
          
          // Only animate if there's a build animation and we're now visible
          const shouldAnimate = buildAnim && visible && !reduceMotion;
          const animVariants = shouldAnimate
            ? getAnimationPresetVariants(buildAnim.preset)
            : { initial: {}, animate: {}, exit: {} };

          const containerStyle = getLayerContainerStyle(layer, baseSize);
          const transformStyle = getLayerTransformStyle(layer);
          const contentStyle = getLayerContentStyle(layer);

          return (
            <motion.div
              key={layer.id}
              className="absolute"
              style={{
                ...containerStyle,
                opacity: visible ? 1 : 0,
              }}
              initial={shouldAnimate ? "initial" : false}
              animate={visible ? "animate" : "initial"}
              exit="exit"
              variants={animVariants}
              transition={{
                duration: buildAnim ? buildAnim.duration / 1000 : 0.3,
                ease: 'easeOut',
              }}
            >
              <div
                className="absolute inset-0"
                style={{ ...transformStyle, ...contentStyle }}
              >
                <SlideLayerContent layer={layer} textScale={textScale} renderMode="live" />
              </div>
            </motion.div>
          );
        })}
      </AnimatePresence>
    </div>
  );
}

/*
function TextLayerRenderer({
  layer,
  textScale,
}: {
  layer: TextLayer;
  textScale: number;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [fitScale, setFitScale] = useState(1);

  const style = useMemo(() => {
    const merged = mergeTextStyle(layer.style) ?? {};
    const hasExplicitFills = layer.fills !== undefined;
    const fills = layer.fills?.filter((fill) => fill.enabled !== false) ?? [];
    const primary = fills[0];
    if (primary) {
      return { ...merged, color: toRgba(primary.color, primary.opacity) };
    }
    if (hasExplicitFills) {
      return { ...merged, color: 'transparent' };
    }
    return merged;
  }, [layer.style, layer.fills]);
  const textFit = layer.textFit || 'auto';
  const padding = layer.padding ?? 2;
  const primaryStroke = useMemo(() => getPrimaryStroke(layer.strokes ?? []), [layer.strokes]);
  const styleSignature = useMemo(
    () => getTextStyleSignature(style, primaryStroke),
    [style, primaryStroke]
  );

  // Calculate fit scale based on container and text dimensions
  useEffect(() => {
      if (textFit === 'auto') {
        setFitScale((prev) => (prev === 1 ? prev : 1));
      return;
    }

    const calculateFitScale = () => {
      if (!containerRef.current || !textRef.current) return;

      const container = containerRef.current;
      const text = textRef.current;
      
      const paddingPercent = padding / 100;
      const availableWidth = container.clientWidth * (1 - paddingPercent * 2);
      const availableHeight = container.clientHeight * (1 - paddingPercent * 2);

      text.style.transform = 'scale(1)';
      const textWidth = text.scrollWidth;
      const textHeight = text.scrollHeight;

      if (textWidth === 0 || textHeight === 0) {
        setFitScale((prev) => (prev === 1 ? prev : 1));
        return;
      }

      const scaleX = availableWidth / textWidth;
      const scaleY = availableHeight / textHeight;
      let newScale = Math.min(scaleX, scaleY);

      if (textFit === 'shrink') {
        newScale = Math.min(1, newScale);
      }

      newScale = Math.max(0.1, Math.min(5, newScale));
      setFitScale((prev) => (Math.abs(prev - newScale) < 0.001 ? prev : newScale));
    };

    calculateFitScale();

    const observer = new ResizeObserver(calculateFitScale);
    if (containerRef.current) {
      observer.observe(containerRef.current);
    }

    return () => observer.disconnect();
  }, [textFit, padding, layer.content, styleSignature, textScale]);

  const containerStyle: React.CSSProperties = {
    ...getTextContainerStyle(style),
    padding: `${padding}%`,
    overflow: 'hidden',
  };

  const textWrapperStyle: React.CSSProperties = {
    transform: textFit !== 'auto' ? `scale(${fitScale})` : undefined,
    transformOrigin: getTransformOrigin(style),
    display: 'flex',
    flexDirection: 'column',
    width: textFit !== 'auto' ? `${100 / fitScale}%` : undefined,
  };

  return (
    <div ref={containerRef} className="w-full h-full flex" style={containerStyle}>
      <div style={textWrapperStyle}>
        <span
          ref={textRef}
          style={getTextStyle(style, textScale, primaryStroke)}
          className="whitespace-pre-wrap"
        >
          {layer.content}
        </span>
      </div>
    </div>
  );
}

function getTransformOrigin(style?: Partial<TextStyle>): string {
  const alignment = style?.alignment || 'center';
  const verticalAlignment = style?.verticalAlignment || 'middle';
  
  const horizontal = {
    left: 'left',
    center: 'center',
    right: 'right',
  }[alignment];

  const vertical = {
    top: 'top',
    middle: 'center',
    bottom: 'bottom',
  }[verticalAlignment];

  return `${horizontal} ${vertical}`;
}

function getTextStyleSignature(style?: Partial<TextStyle>, stroke?: LayerStroke | null): string {
  if (!style) return '';
  const font = style.font;
  return [
    style.alignment ?? '',
    style.verticalAlignment ?? '',
    style.color ?? '',
    font?.family ?? '',
    font?.size ?? '',
    font?.weight ?? '',
    font?.italic ?? '',
    font?.lineHeight ?? '',
    font?.letterSpacing ?? '',
    stroke?.color ?? '',
    stroke?.opacity ?? '',
    stroke?.width ?? '',
  ].join('|');
}

const resolveLayerFills = (layer: Layer) => {
  const hasExplicitFills = layer.fills !== undefined;
  const fills = layer.fills?.filter((fill) => fill.enabled !== false) ?? [];
  if (fills.length > 0) return fills;
  if (hasExplicitFills) return [];
  if (layer.type === 'shape') {
    return [
      {
        id: 'legacy-fill',
        color: layer.style?.fill ?? '#3b82f6',
        opacity: layer.style?.fillOpacity ?? 1,
      },
    ];
  }
  if (layer.type === 'text') {
    return [
      {
        id: 'legacy-fill',
        color: layer.style?.color ?? '#ffffff',
        opacity: 1,
      },
    ];
  }
  return [];
};

const resolveLayerStrokes = (layer: Layer) => {
  const enabledStrokes = layer.strokes?.filter((stroke) => stroke.enabled !== false) ?? [];
  if (enabledStrokes.length > 0) return enabledStrokes;
  if (layer.type === 'shape') {
    const usesLegacyStroke = layer.strokes === undefined;
    if (usesLegacyStroke) {
      return [
        {
          id: 'legacy-stroke',
          color: layer.style?.stroke ?? '#1d4ed8',
          opacity: layer.style?.strokeOpacity ?? 1,
          width: layer.style?.strokeWidth ?? 2,
          position: 'inside',
          sides: 'all',
        },
      ] as LayerStroke[];
    }
  }
  return [];
};

const getPrimaryStroke = (strokes: LayerStroke[]) => {
  const enabled = strokes.filter((stroke) => stroke.enabled !== false);
  return enabled[enabled.length - 1] ?? enabled[0] ?? strokes[0] ?? null;
};

const resolveStrokeSides = (stroke: LayerStroke) => {
  const sides = stroke.sides ?? 'all';
  if (sides === 'custom') {
    return stroke.customSides ?? {
      top: true,
      right: true,
      bottom: true,
      left: true,
    };
  }
  return {
    top: sides === 'all' || sides === 'top',
    right: sides === 'all' || sides === 'right',
    bottom: sides === 'all' || sides === 'bottom',
    left: sides === 'all' || sides === 'left',
  };
};

const getStrokeInset = (stroke: LayerStroke) => {
  const width = Math.max(0, stroke.width ?? 0);
  switch (stroke.position ?? 'inside') {
    case 'outside':
      return -width;
    case 'center':
      return -width / 2;
    default:
      return 0;
  }
};

function ShapeLayerRenderer({ layer }: { layer: ShapeLayer }) {
  const { shapeType, style } = layer;
  const fills = resolveLayerFills(layer);
  const strokes = resolveLayerStrokes(layer);

  if (shapeType === 'line') {
    return (
      <svg className="w-full h-full" preserveAspectRatio="none">
        {strokes.map((stroke, index) => (
          <line
            key={`${stroke.id}-${index}`}
            x1="0"
            y1="50%"
            x2="100%"
            y2="50%"
            stroke={stroke.color}
            strokeWidth={stroke.width}
            strokeOpacity={stroke.opacity}
          />
        ))}
      </svg>
    );
  }

  if (shapeType === 'triangle') {
    return (
      <svg className="w-full h-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        {fills.slice().reverse().map((fill, index) => (
          <polygon
            key={`${fill.id}-${index}`}
            points="50,0 100,100 0,100"
            fill={fill.color}
            fillOpacity={fill.opacity}
          />
        ))}
        {strokes.map((stroke, index) => (
          <polygon
            key={`${stroke.id}-${index}`}
            points="50,0 100,100 0,100"
            fill="none"
            stroke={stroke.color}
            strokeWidth={stroke.width}
            strokeOpacity={stroke.opacity}
          />
        ))}
      </svg>
    );
  }

  const baseRadius = shapeType === 'ellipse' ? null : style.cornerRadius;
  const borderRadius = shapeType === 'ellipse' ? '50%' : `${style.cornerRadius}px`;

  return (
    <div
      className="relative w-full h-full"
      style={{
        borderRadius,
      }}
    >
      {fills.slice().reverse().map((fill, index) => (
        <div
          key={`${fill.id}-${index}`}
          className="absolute inset-0"
          style={{
            backgroundColor: toRgba(fill.color, fill.opacity),
            borderRadius,
          }}
        />
      ))}
      {strokes.map((stroke, index) => {
        const inset = getStrokeInset(stroke);
        const sides = resolveStrokeSides(stroke);
        const radiusOffset = baseRadius === null ? 0 : Math.abs(inset);
        const resolvedRadius =
          baseRadius === null ? '50%' : `${Math.max(0, baseRadius + radiusOffset)}px`;
        const strokeColor = toRgba(stroke.color, stroke.opacity);
        return (
          <div
            key={`${stroke.id}-${index}`}
            className="absolute pointer-events-none"
            style={{
              top: inset,
              left: inset,
              right: inset,
              bottom: inset,
              borderStyle: 'solid',
              borderColor: strokeColor,
              borderTopWidth: sides.top ? stroke.width : 0,
              borderRightWidth: sides.right ? stroke.width : 0,
              borderBottomWidth: sides.bottom ? stroke.width : 0,
              borderLeftWidth: sides.left ? stroke.width : 0,
              borderRadius: resolvedRadius,
            }}
          />
        );
      })}
    </div>
  );
}

function MediaLayerRenderer({ layer }: { layer: MediaLayer }) {
  const style: React.CSSProperties = {
    width: '100%',
    height: '100%',
    objectFit: layer.fit,
  };

  if (layer.mediaType === 'video') {
    return (
      <video
        src={layer.mediaId}
        style={style}
        loop={layer.loop}
        muted={layer.muted}
        autoPlay={layer.autoplay}
        playsInline
      />
    );
  }

  return <img src={layer.mediaId} alt="" style={style} draggable={false} />;
}

function WebLayerRenderer({ layer }: { layer: WebLayer }) {
  return (
    <ScaledWebLayer
      url={layer.url}
      zoom={layer.zoom}
      baseWidth={layer.transform.width}
      baseHeight={layer.transform.height}
      interactive={false}
      title="Web content"
      className="w-full h-full bg-white"
    />
  );
}
*/

// Get animation preset variants
function getAnimationPresetVariants(preset: AnimationPreset): Variants {
  switch (preset) {
    case 'fade-in':
      return {
        initial: { opacity: 0 },
        animate: { opacity: 1 },
        exit: { opacity: 0 },
      };

    case 'fade-out':
      return {
        initial: { opacity: 1 },
        animate: { opacity: 0 },
        exit: { opacity: 0 },
      };

    case 'slide-in-left':
      return {
        initial: { x: '-100%', opacity: 0 },
        animate: { x: 0, opacity: 1 },
        exit: { x: '-100%', opacity: 0 },
      };

    case 'slide-in-right':
      return {
        initial: { x: '100%', opacity: 0 },
        animate: { x: 0, opacity: 1 },
        exit: { x: '100%', opacity: 0 },
      };

    case 'slide-in-up':
      return {
        initial: { y: '100%', opacity: 0 },
        animate: { y: 0, opacity: 1 },
        exit: { y: '100%', opacity: 0 },
      };

    case 'slide-in-down':
      return {
        initial: { y: '-100%', opacity: 0 },
        animate: { y: 0, opacity: 1 },
        exit: { y: '-100%', opacity: 0 },
      };

    case 'scale-in':
      return {
        initial: { scale: 0, opacity: 0 },
        animate: { scale: 1, opacity: 1 },
        exit: { scale: 0, opacity: 0 },
      };

    case 'scale-out':
      return {
        initial: { scale: 1.5, opacity: 0 },
        animate: { scale: 1, opacity: 1 },
        exit: { scale: 1.5, opacity: 0 },
      };

    case 'bounce-in':
      return {
        initial: { scale: 0, opacity: 0 },
        animate: { 
          scale: 1, 
          opacity: 1,
          transition: { type: 'spring', bounce: 0.5 }
        },
        exit: { scale: 0, opacity: 0 },
      };

    case 'spin-in':
      return {
        initial: { rotate: -180, scale: 0, opacity: 0 },
        animate: { rotate: 0, scale: 1, opacity: 1 },
        exit: { rotate: 180, scale: 0, opacity: 0 },
      };

    default:
      return {
        initial: { opacity: 0 },
        animate: { opacity: 1 },
        exit: { opacity: 0 },
      };
  }
}

// Get variants for different transition types
function getTransitionVariants(
  type: SlideTransitionType,
  reducedMotion: boolean | null
): Variants {
  if (reducedMotion) {
    return {
      initial: { opacity: 0 },
      animate: { opacity: 1 },
      exit: { opacity: 0 },
    };
  }

  switch (type) {
    case 'none':
      return {
        initial: {},
        animate: {},
        exit: {},
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

// Helper functions
function getTextContainerStyle(style?: Partial<TextStyle>): React.CSSProperties {
  if (!style) return {};

  const alignItems = {
    top: 'flex-start',
    middle: 'center',
    bottom: 'flex-end',
  }[style.verticalAlignment || 'middle'];

  const justifyContent = {
    left: 'flex-start',
    center: 'center',
    right: 'flex-end',
  }[style.alignment || 'center'];

  return { alignItems, justifyContent };
}

function getTextStyle(
  style?: Partial<TextStyle>,
  scale = 1,
  stroke?: LayerStroke | null
): React.CSSProperties {
  if (!style) return {};

  const css: React.CSSProperties = {
    color: style.color,
    textAlign: style.alignment,
  };

  if (style.font) {
    css.fontFamily = style.font.family;
    css.fontSize = style.font.size * scale;
    css.fontWeight = style.font.weight;
    css.fontStyle = style.font.italic ? 'italic' : 'normal';
    css.lineHeight = style.font.lineHeight;
    const letterSpacing = style.font.letterSpacing ?? 0;
    css.letterSpacing = Number.isFinite(letterSpacing) ? letterSpacing * scale : 0;
  }

  if (stroke) {
    css.WebkitTextStroke = `${stroke.width * scale}px ${toRgba(stroke.color, stroke.opacity)}`;
  }

  return css;
}

function getBaseSlideSize(
  aspectRatio?: '16:9' | '4:3' | '16:10',
  slideSize?: { width: number; height: number }
) {
  if (
    slideSize &&
    Number.isFinite(slideSize.width) &&
    Number.isFinite(slideSize.height) &&
    slideSize.width > 0 &&
    slideSize.height > 0
  ) {
    return {
      width: Math.round(slideSize.width),
      height: Math.round(slideSize.height),
    };
  }
  switch (aspectRatio) {
    case '4:3':
      return { width: 1440, height: 1080 };
    case '16:10':
      return { width: 1920, height: 1200 };
    case '16:9':
    default:
      return { width: 1920, height: 1080 };
  }
}

function getAspectClass(aspectRatio?: '16:9' | '4:3' | '16:10'): string {
  if (aspectRatio === '4:3') return 'aspect-[4/3]';
  if (aspectRatio === '16:10') return 'aspect-[16/10]';
  return 'aspect-video';
}

/**
 * Simple slide stage without animations (for editor preview)
 */
export function StaticSlideStage({
  slide,
  aspectRatio,
  slideSize,
  className,
  isBlackout = false,
  isClear = false,
  showSafeArea = false,
}: AnimatedSlideStageProps) {
  // Import SlideRenderer dynamically to avoid circular dependencies
  const SlideRenderer = require('./SlideRenderer').SlideRenderer;
  
  return (
    <div className={cn('relative', className)}>
      <SlideRenderer
        slide={slide}
        aspectRatio={aspectRatio}
        slideSize={slideSize}
        className="w-full h-full"
        isBlackout={isBlackout}
        isClear={isClear}
        showSafeArea={showSafeArea}
      />
    </div>
  );
}
