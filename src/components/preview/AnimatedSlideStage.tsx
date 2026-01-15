/**
 * AnimatedSlideStage - Wrapper for slide transitions and build animations using Motion
 */

import { AnimatePresence, motion, useReducedMotion, type Variants } from 'motion/react';
import { useEffect, useMemo, useRef, useState } from 'react';
import type {
  Slide,
  Theme,
  SlideTransitionType,
  AnimationPreset,
  Layer,
  TextStyle,
  Background,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
} from '@/lib/models';
import { cn } from '@/lib/utils';

interface AnimatedSlideStageProps {
  slide: Slide | null;
  theme: Theme | null;
  className?: string;
  isBlackout?: boolean;
  isClear?: boolean;
  showSafeArea?: boolean;
  visibleLayerIds?: string[]; // If provided, controls which layers are visible (for builds)
}

export function AnimatedSlideStage({
  slide,
  theme,
  className,
  isBlackout = false,
  isClear = false,
  showSafeArea = false,
  visibleLayerIds,
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
              theme={theme}
              showSafeArea={showSafeArea}
              visibleLayerIds={visibleLayerIds}
              reduceMotion={shouldReduceMotion || false}
            />
          </motion.div>
        ) : (
          <motion.div
            key="empty"
            className="absolute inset-0 bg-muted flex items-center justify-center"
            initial={{ opacity: 0 }}
            animate={{ opacity: 1 }}
            exit={{ opacity: 0 }}
            transition={{ duration: 0.2 }}
          >
            <div className="text-muted-foreground text-sm">No slide selected</div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// Animated slide content with layer animations
interface AnimatedSlideContentProps {
  slide: Slide;
  theme: Theme | null;
  showSafeArea: boolean;
  visibleLayerIds?: string[];
  reduceMotion: boolean;
}

function AnimatedSlideContent({
  slide,
  theme,
  showSafeArea,
  visibleLayerIds,
  reduceMotion,
}: AnimatedSlideContentProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [textScale, setTextScale] = useState(1);
  const baseSize = useMemo(() => getBaseSlideSize(theme?.aspectRatio), [theme?.aspectRatio]);

  // Get effective background
  const background = useMemo((): Background => {
    if (slide.overrides?.background) {
      return slide.overrides.background;
    }
    return theme?.background || { type: 'solid', color: '#000000' } as Background;
  }, [slide, theme]);

  // Text scaling for responsive text
  useEffect(() => {
    if (!containerRef.current) return;

    const updateScale = (width: number, height: number) => {
      if (!width || !height) return;
      const scale = Math.min(width / baseSize.width, height / baseSize.height);
      setTextScale(scale || 1);
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

  // Get primary text style
  const primaryTextStyle = useMemo(() => {
    if (slide.overrides?.primaryText) {
      return { ...theme?.primaryText, ...slide.overrides.primaryText };
    }
    return theme?.primaryText;
  }, [slide, theme]);

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
      className="relative w-full h-full slide-aspect overflow-hidden"
      style={getBackgroundStyle(background)}
    >
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

          return (
            <motion.div
              key={layer.id}
              className="absolute"
              style={{
                left: `${(layer.transform.x / baseSize.width) * 100}%`,
                top: `${(layer.transform.y / baseSize.height) * 100}%`,
                width: `${(layer.transform.width / baseSize.width) * 100}%`,
                height: `${(layer.transform.height / baseSize.height) * 100}%`,
                transform: `rotate(${layer.transform.rotation}deg)`,
                opacity: visible ? layer.transform.opacity : 0,
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
              <LayerContent
                layer={layer}
                primaryTextStyle={primaryTextStyle}
                textScale={textScale}
              />
            </motion.div>
          );
        })}
      </AnimatePresence>
    </div>
  );
}

// Layer content renderer
interface LayerContentProps {
  layer: Layer;
  primaryTextStyle?: Partial<TextStyle>;
  textScale: number;
}

function LayerContent({ layer, primaryTextStyle, textScale }: LayerContentProps) {
  switch (layer.type) {
    case 'text':
      return <TextLayerRenderer layer={layer} primaryTextStyle={primaryTextStyle} textScale={textScale} />;
    case 'shape':
      return <ShapeLayerRenderer layer={layer} />;
    case 'media':
      return <MediaLayerRenderer layer={layer} />;
    case 'web':
      return <WebLayerRenderer layer={layer} />;
    default:
      return null;
  }
}

function TextLayerRenderer({
  layer,
  primaryTextStyle,
  textScale,
}: {
  layer: TextLayer;
  primaryTextStyle?: Partial<TextStyle>;
  textScale: number;
}) {
  const containerRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [fitScale, setFitScale] = useState(1);

  const style = layer.style || primaryTextStyle;
  const textFit = layer.textFit || 'auto';
  const padding = layer.padding ?? 2;

  // Calculate fit scale based on container and text dimensions
  useEffect(() => {
    if (textFit === 'auto') {
      setFitScale(1);
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
        setFitScale(1);
        return;
      }

      const scaleX = availableWidth / textWidth;
      const scaleY = availableHeight / textHeight;
      let newScale = Math.min(scaleX, scaleY);

      if (textFit === 'shrink') {
        newScale = Math.min(1, newScale);
      }

      newScale = Math.max(0.1, Math.min(5, newScale));
      setFitScale(newScale);
    };

    calculateFitScale();

    const observer = new ResizeObserver(calculateFitScale);
    if (containerRef.current) {
      observer.observe(containerRef.current);
    }

    return () => observer.disconnect();
  }, [textFit, padding, layer.content, style, textScale]);

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
          style={getTextStyle(style, textScale)}
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

function ShapeLayerRenderer({ layer }: { layer: ShapeLayer }) {
  const { shapeType, style } = layer;

  if (shapeType === 'line') {
    return (
      <svg className="w-full h-full" preserveAspectRatio="none">
        <line
          x1="0"
          y1="50%"
          x2="100%"
          y2="50%"
          stroke={style.stroke}
          strokeWidth={style.strokeWidth}
          strokeOpacity={style.strokeOpacity}
        />
      </svg>
    );
  }

  if (shapeType === 'triangle') {
    return (
      <svg className="w-full h-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        <polygon
          points="50,0 100,100 0,100"
          fill={style.fill}
          fillOpacity={style.fillOpacity}
          stroke={style.stroke}
          strokeWidth={style.strokeWidth}
          strokeOpacity={style.strokeOpacity}
        />
      </svg>
    );
  }

  const commonStyle: React.CSSProperties = {
    width: '100%',
    height: '100%',
    backgroundColor: style.fill,
    opacity: style.fillOpacity,
    border: `${style.strokeWidth}px solid ${style.stroke}`,
    borderRadius: shapeType === 'ellipse' ? '50%' : style.cornerRadius,
  };

  return <div style={commonStyle} />;
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
    <div className="w-full h-full bg-white overflow-hidden">
      <iframe
        src={layer.url}
        className="w-full h-full border-0"
        style={{
          transform: `scale(${layer.zoom})`,
          transformOrigin: 'top left',
          width: `${100 / layer.zoom}%`,
          height: `${100 / layer.zoom}%`,
          pointerEvents: layer.interactive ? 'auto' : 'none',
        }}
        title="Web content"
        sandbox="allow-scripts allow-same-origin"
      />
    </div>
  );
}

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
function getBackgroundStyle(background: Background): React.CSSProperties {
  switch (background.type) {
    case 'solid':
      return { backgroundColor: background.color };

    case 'gradient':
      const stops = background.stops
        .map((s) => `${s.color} ${s.position}%`)
        .join(', ');
      return {
        background: `linear-gradient(${background.angle}deg, ${stops})`,
      };

    case 'image':
      return {
        backgroundImage: `url(${background.mediaId})`,
        backgroundSize: background.fit,
        backgroundPosition: `${background.position.x}% ${background.position.y}%`,
        backgroundRepeat: 'no-repeat',
        opacity: background.opacity,
      };

    case 'video':
      return { backgroundColor: '#000' };

    default:
      return { backgroundColor: '#000' };
  }
}

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

function getTextStyle(style?: Partial<TextStyle>, scale = 1): React.CSSProperties {
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
    css.letterSpacing = style.font.letterSpacing * scale;
  }

  if (style.shadow?.enabled) {
    css.textShadow = `${style.shadow.offsetX * scale}px ${style.shadow.offsetY * scale}px ${style.shadow.blur * scale}px ${style.shadow.color}`;
  }

  if (style.outline?.enabled) {
    css.WebkitTextStroke = `${style.outline.width * scale}px ${style.outline.color}`;
  }

  return css;
}

function getBaseSlideSize(aspectRatio?: Theme['aspectRatio']) {
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

/**
 * Simple slide stage without animations (for editor preview)
 */
export function StaticSlideStage({
  slide,
  theme,
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
        theme={theme}
        className="w-full h-full"
        isBlackout={isBlackout}
        isClear={isClear}
        showSafeArea={showSafeArea}
      />
    </div>
  );
}
