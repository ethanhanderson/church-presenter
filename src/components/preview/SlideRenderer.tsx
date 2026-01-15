/**
 * SlideRenderer - Renders a single slide with its background and layers
 * Used by both the preview panel and the output window
 */

import { useEffect, useMemo, useRef, useState } from 'react';
import type {
  Slide,
  Theme,
  Background,
  TextStyle,
  Layer,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
} from '@/lib/models';
import { cn } from '@/lib/utils';
import { getSlideGroupColor } from '@/lib/models';

interface SlideRendererProps {
  slide: Slide | null;
  theme: Theme | null;
  className?: string;
  isBlackout?: boolean;
  isClear?: boolean;
  showSafeArea?: boolean;
}

export function SlideRenderer({
  slide,
  theme,
  className,
  isBlackout = false,
  isClear = false,
  showSafeArea = false,
}: SlideRendererProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [textScale, setTextScale] = useState(1);
  const baseSize = useMemo(() => getBaseSlideSize(theme?.aspectRatio), [theme?.aspectRatio]);

  // Get effective background (slide override or theme default)
  const background = useMemo((): Background => {
    if (slide?.overrides?.background) {
      return slide.overrides.background;
    }
    return theme?.background || { type: 'solid', color: '#000000' } as Background;
  }, [slide, theme]);

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

  // Get effective text styles
  const primaryTextStyle = useMemo(() => {
    if (slide?.overrides?.primaryText) {
      return { ...theme?.primaryText, ...slide.overrides.primaryText };
    }
    return theme?.primaryText;
  }, [slide, theme]);

  // Render blackout
  if (isBlackout) {
    return (
      <div ref={containerRef} className={cn('relative bg-black slide-aspect', className)} />
    );
  }

  // Render clear/logo screen
  if (isClear) {
    return (
      <div
        ref={containerRef}
        className={cn('relative bg-black slide-aspect flex items-center justify-center', className)}
      >
        <div className="text-white/20 text-2xl font-light">
          Church Presenter
        </div>
      </div>
    );
  }

  // Render empty state
  if (!slide) {
    return (
      <div
        ref={containerRef}
        className={cn('relative bg-muted slide-aspect flex items-center justify-center', className)}
      >
        <div className="text-muted-foreground text-sm">No slide selected</div>
      </div>
    );
  }

  return (
    <div
      ref={containerRef}
      className={cn('relative slide-aspect overflow-hidden', className)}
      style={getBackgroundStyle(background)}
    >
      {/* Safe area guides */}
      {showSafeArea && (
        <div className="absolute inset-0 pointer-events-none">
          <div className="absolute inset-[5%] border border-dashed border-white/30" />
          <div className="absolute inset-[10%] border border-dashed border-white/20" />
        </div>
      )}

      {/* Layers */}
      {slide.layers.map((layer) => (
        <LayerRenderer
          key={layer.id}
          layer={layer}
          primaryTextStyle={primaryTextStyle}
          textScale={textScale}
          baseSize={baseSize}
        />
      ))}
    </div>
  );
}

interface LayerRendererProps {
  layer: Layer;
  primaryTextStyle?: Partial<TextStyle>;
  textScale: number;
  baseSize: { width: number; height: number };
}

function LayerRenderer({ layer, primaryTextStyle, textScale, baseSize }: LayerRendererProps) {
  if (!layer.visible) return null;

  const containerStyle: React.CSSProperties = {
    left: `${(layer.transform.x / baseSize.width) * 100}%`,
    top: `${(layer.transform.y / baseSize.height) * 100}%`,
    width: `${(layer.transform.width / baseSize.width) * 100}%`,
    height: `${(layer.transform.height / baseSize.height) * 100}%`,
    transform: `rotate(${layer.transform.rotation}deg)`,
    opacity: layer.transform.opacity,
  };

  return (
    <div className="absolute" style={containerStyle}>
      {layer.type === 'text' && (
        <TextLayerRenderer
          layer={layer}
          primaryTextStyle={primaryTextStyle}
          textScale={textScale}
        />
      )}
      {layer.type === 'shape' && <ShapeLayerRenderer layer={layer} />}
      {layer.type === 'media' && <MediaLayerRenderer layer={layer} />}
      {layer.type === 'web' && <WebLayerRenderer layer={layer} />}
    </div>
  );
}

interface TextLayerRendererProps {
  layer: TextLayer;
  primaryTextStyle?: Partial<TextStyle>;
  textScale: number;
}

function TextLayerRenderer({ layer, primaryTextStyle, textScale }: TextLayerRendererProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const textRef = useRef<HTMLSpanElement>(null);
  const [fitScale, setFitScale] = useState(1);

  const style = layer.style || primaryTextStyle;
  const textFit = layer.textFit || 'auto';
  const padding = layer.padding ?? 2;
  const styleSignature = useMemo(() => getTextStyleSignature(style), [style]);

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
      
      // Account for padding
      const paddingPercent = padding / 100;
      const availableWidth = container.clientWidth * (1 - paddingPercent * 2);
      const availableHeight = container.clientHeight * (1 - paddingPercent * 2);

      // Reset scale to measure natural size
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

function getTextStyleSignature(style?: Partial<TextStyle>): string {
  if (!style) return '';
  const font = style.font;
  const shadow = style.shadow;
  const outline = style.outline;
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
    shadow?.enabled ?? '',
    shadow?.offsetX ?? '',
    shadow?.offsetY ?? '',
    shadow?.blur ?? '',
    shadow?.color ?? '',
    outline?.enabled ?? '',
    outline?.width ?? '',
    outline?.color ?? '',
  ].join('|');
}

interface ShapeLayerRendererProps {
  layer: ShapeLayer;
}

function ShapeLayerRenderer({ layer }: ShapeLayerRendererProps) {
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

interface MediaLayerRendererProps {
  layer: MediaLayer;
}

function MediaLayerRenderer({ layer }: MediaLayerRendererProps) {
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

interface WebLayerRendererProps {
  layer: WebLayer;
}

function WebLayerRenderer({ layer }: WebLayerRendererProps) {
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
 * Slide thumbnail for lists
 */
interface SlideThumbnailProps {
  slide: Slide;
  theme: Theme | null;
  isSelected?: boolean;
  isActive?: boolean;
  showLabel?: boolean;
  slideNumber?: number;
  groupLabel?: string | null;
  onClick?: () => void;
  onDoubleClick?: () => void;
  className?: string;
}

export function SlideThumbnail({
  slide,
  theme,
  isSelected,
  isActive,
  showLabel = true,
  slideNumber,
  groupLabel,
  onClick,
  onDoubleClick,
  className,
}: SlideThumbnailProps) {
  const groupColor = getSlideGroupColor(slide.section);
  const showGroupLabel = showLabel && !!groupLabel;
  const stateRingClass = isActive
    ? 'ring-2 ring-green-500 ring-offset-2 ring-offset-background'
    : isSelected
      ? 'ring-2 ring-primary ring-offset-2 ring-offset-background'
      : '';
  const borderStyle = groupColor ? { borderColor: groupColor } : undefined;

  return (
    <div className={cn('w-full rounded-md', stateRingClass)}>
      <button
        className={cn(
          'group block w-full text-left transition-colors rounded-md border-2 overflow-hidden',
          !groupColor && 'border-muted/40',
          className
        )}
        style={{
          ...borderStyle,
          backgroundColor: groupColor ?? undefined,
        }}
        onClick={onClick}
        onDoubleClick={onDoubleClick}
      >
        <div className="relative w-full">
          <SlideRenderer slide={slide} theme={theme} className="w-full" />
        </div>
        <div
          className={cn(
            'flex items-center gap-2 px-2 py-1 text-[10px] font-semibold rounded-none',
            showGroupLabel ? 'justify-between' : 'justify-start',
            groupColor ? 'text-white' : 'text-muted-foreground bg-muted'
          )}
          style={groupColor ? { backgroundColor: groupColor } : undefined}
        >
          {slideNumber !== undefined && (
            <span className="tabular-nums">{slideNumber}</span>
          )}
          {showGroupLabel && (
            <span className="truncate">{groupLabel}</span>
          )}
        </div>
      </button>
    </div>
  );
}
