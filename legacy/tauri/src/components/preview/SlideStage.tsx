import { useEffect, useMemo, useRef, useState } from 'react';
import type { Slide, Layer } from '@/lib/models';
import { BackgroundMedia } from '@/components/preview/BackgroundMedia';
import {
  LayerContent,
  getLayerContainerStyle,
  getLayerContentStyle,
  getLayerTransformStyle,
} from '@/components/preview/slide-layer-content';
import { cn } from '@/lib/utils';
import { getBackgroundStyle, resolveSlideBackground } from '@/lib/models';

interface SlideStageProps {
  slide: Slide | null;
  aspectRatio?: '16:9' | '4:3' | '16:10';
  slideSize?: { width: number; height: number };
  className?: string;
  isBlackout?: boolean;
  isClear?: boolean;
  showSafeArea?: boolean;
  renderMode?: 'live' | 'thumbnail' | 'canvas';
  backgroundMode?: 'auto' | 'transparent';
  backgroundMediaSrc?: string | null;
  visibleLayerIds?: string[];
}

export function SlideStage({
  slide,
  aspectRatio,
  slideSize,
  className,
  isBlackout = false,
  isClear = false,
  showSafeArea = false,
  renderMode = 'live',
  backgroundMode = 'auto',
  backgroundMediaSrc,
  visibleLayerIds,
}: SlideStageProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [textScale, setTextScale] = useState(1);
  const baseSize = useMemo(
    () => getBaseSlideSize(aspectRatio, slideSize),
    [aspectRatio, slideSize]
  );
  const aspectClass = getAspectClass(aspectRatio);

  const background = useMemo(() => resolveSlideBackground(slide), [slide]);
  const effectiveBackgroundSrc = backgroundMediaSrc ?? undefined;

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

  if (isBlackout) {
    return <div ref={containerRef} className={cn('relative bg-black', aspectClass, className)} />;
  }

  if (isClear) {
    return (
      <div
        ref={containerRef}
        className={cn(
          'relative bg-black flex items-center justify-center',
          aspectClass,
          className
        )}
      >
        <div className="text-white/20 text-2xl font-light">Church Presenter</div>
      </div>
    );
  }

  if (!slide) {
    return (
      <div
        ref={containerRef}
        className={cn(
          'relative bg-muted flex items-center justify-center',
          aspectClass,
          className
        )}
      >
        <div className="text-muted-foreground text-sm">No slide selected</div>
      </div>
    );
  }

  const allowBackground = backgroundMode !== 'transparent';
  const layerIds = useMemo(
    () => (visibleLayerIds ? new Set(visibleLayerIds) : null),
    [visibleLayerIds]
  );

  return (
    <div
      ref={containerRef}
      className={cn('relative overflow-hidden', aspectClass, className)}
      style={allowBackground ? getBackgroundStyle(background) : undefined}
    >
      {allowBackground && (
        <BackgroundMedia
          background={background}
          src={effectiveBackgroundSrc}
          className="absolute inset-0"
        />
      )}
      {showSafeArea && (
        <div className="absolute inset-0 pointer-events-none">
          <div className="absolute inset-[5%] border border-dashed border-white/30" />
          <div className="absolute inset-[10%] border border-dashed border-white/20" />
        </div>
      )}

      {slide.layers.map((layer) => {
        if (layerIds && !layerIds.has(layer.id)) return null;
        return (
          <LayerRenderer
            key={layer.id}
            layer={layer}
            textScale={textScale}
            baseSize={baseSize}
            renderMode={renderMode}
          />
        );
      })}
    </div>
  );
}

interface LayerRendererProps {
  layer: Layer;
  textScale: number;
  baseSize: { width: number; height: number };
  renderMode: 'live' | 'thumbnail' | 'canvas';
}

function LayerRenderer({ layer, textScale, baseSize, renderMode }: LayerRendererProps) {
  if (!layer.visible) return null;

  const contentStyle: React.CSSProperties = {
    ...getLayerTransformStyle(layer),
    ...getLayerContentStyle(layer),
  };

  return (
    <div
      className="absolute"
      data-preview-layer-root
      data-layer-id={layer.id}
      style={getLayerContainerStyle(layer, baseSize)}
    >
      <div className="absolute inset-0" data-preview-layer-content style={contentStyle}>
        <LayerContent layer={layer} textScale={textScale} renderMode={renderMode} />
      </div>
    </div>
  );
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
