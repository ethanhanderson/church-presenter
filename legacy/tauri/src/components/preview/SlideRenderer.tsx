/**
 * SlideRenderer - Renders a single slide with its background and layers
 * Used by both the preview panel and the output window
 */

import { useMemo } from 'react';
import type { Presentation, Slide } from '@/lib/models';
import { getSlideGroupColor, resolveSlideBackground } from '@/lib/models';
import { useResolvedBackgroundMediaSrc } from '@/lib/media/resolveMediaUrl';
import { SlideStage } from '@/components/preview/SlideStage';
import { ThumbnailCheckerboard } from '@/components/preview/ThumbnailCheckerboard';
import { cn } from '@/lib/utils';

interface SlideRendererProps {
  slide: Slide | null;
  aspectRatio?: '16:9' | '4:3' | '16:10';
  slideSize?: { width: number; height: number };
  className?: string;
  isBlackout?: boolean;
  isClear?: boolean;
  showSafeArea?: boolean;
  renderMode?: 'live' | 'thumbnail';
  backgroundMediaSrc?: string | null;
}

export function SlideRenderer({
  slide,
  aspectRatio,
  slideSize,
  className,
  isBlackout = false,
  isClear = false,
  showSafeArea = false,
  renderMode = 'live',
  backgroundMediaSrc,
}: SlideRendererProps) {
  return (
    <SlideStage
      slide={slide}
      aspectRatio={aspectRatio}
      slideSize={slideSize}
      className={className}
      isBlackout={isBlackout}
      isClear={isClear}
      showSafeArea={showSafeArea}
      renderMode={renderMode}
      backgroundMediaSrc={backgroundMediaSrc}
    />
  );
}

interface SlideThumbnailProps {
  slide: Slide;
  isSelected?: boolean;
  isActive?: boolean;
  showLabel?: boolean;
  slideNumber?: number;
  groupLabel?: string | null;
  numberPlacement?: 'footer-right' | 'overlay-left';
  footerOrder?: 'label-first' | 'number-first';
  presentation?: Presentation | null;
  presentationPath?: string | null;
  pendingMedia?: Map<string, string>;
  onClick?: () => void;
  onDoubleClick?: () => void;
  className?: string;
  useGroupColor?: boolean;
  groupColorOutline?: boolean;
  showFooter?: boolean;
}

export function SlideThumbnail({
  slide,
  isSelected = false,
  isActive = false,
  showLabel = true,
  slideNumber,
  groupLabel,
  numberPlacement = 'footer-right',
  footerOrder = 'label-first',
  presentation,
  presentationPath,
  pendingMedia,
  onClick,
  onDoubleClick,
  className,
  useGroupColor = true,
  groupColorOutline = false,
  showFooter = true,
}: SlideThumbnailProps) {
  const background = useMemo(() => resolveSlideBackground(slide), [slide]);
  const isTransparent = background.type === 'transparent';
  const backgroundMediaSrc = useResolvedBackgroundMediaSrc({
    background,
    presentation,
    presentationPath,
    pendingMedia,
  });
  const label = showLabel ? groupLabel : null;
  const groupColor = useGroupColor ? getSlideGroupColor(slide.section ?? null) : null;
  const showFooterContent =
    showFooter && slideNumber !== undefined && numberPlacement === 'footer-right';
  const showOverlayNumber = slideNumber !== undefined && numberPlacement === 'overlay-left';

  const footerOverlapClass = groupColorOutline ? '-mt-[2px]' : 'mt-0';
  const wrapperBackgroundStyle =
    groupColorOutline && groupColor ? { backgroundColor: groupColor } : undefined;
  return (
    <div
      className={cn(
        'flex flex-col gap-0 rounded-md overflow-hidden',
        isActive ? 'ring-2 ring-green-500 ring-offset-2 ring-offset-background' : null,
        !isActive && isSelected
          ? 'ring-2 ring-primary ring-offset-2 ring-offset-background'
          : null,
        className
      )}
      style={wrapperBackgroundStyle}
      onClick={onClick}
      onDoubleClick={onDoubleClick}
    >
      <div
        className={cn(
          'relative overflow-hidden rounded-md border bg-card',
          groupColorOutline ? 'border-2' : 'border border-border',
          groupColorOutline && !groupColor ? 'border-border' : null
        )}
        style={groupColorOutline && groupColor ? { borderColor: groupColor } : undefined}
      >
        {isTransparent && <ThumbnailCheckerboard />}
        <SlideStage
          slide={slide}
          aspectRatio={presentation?.manifest.aspectRatio}
          slideSize={presentation?.manifest.slideSize}
          className="relative w-full pointer-events-none"
          renderMode="thumbnail"
          backgroundMode={isTransparent ? 'transparent' : 'auto'}
          backgroundMediaSrc={backgroundMediaSrc ?? undefined}
        />
        {showOverlayNumber && (
          <div className="absolute bottom-2 left-2 rounded bg-muted/80 px-1.5 py-0.5 text-[10px] font-semibold text-foreground">
            {slideNumber}
          </div>
        )}
      </div>
      {showFooterContent && (
        <div
          className={cn(
            'flex items-center justify-between rounded-b-md px-2 py-1 text-[10px]',
            groupColor ? 'text-white' : 'text-muted-foreground',
            footerOverlapClass,
            groupColorOutline ? 'border-x-2 border-b-2' : null
          )}
          style={
            groupColor
              ? {
                  backgroundColor: groupColor,
                  borderColor: groupColor,
                }
              : undefined
          }
        >
          {footerOrder === 'label-first' ? (
            <>
              <span className="truncate font-semibold">
                {label}
              </span>
              <span className="font-semibold">{slideNumber}</span>
            </>
          ) : (
            <>
              <span className="font-semibold">{slideNumber}</span>
              <span className="truncate font-semibold">
                {label}
              </span>
            </>
          )}
        </div>
      )}
    </div>
  );
}
