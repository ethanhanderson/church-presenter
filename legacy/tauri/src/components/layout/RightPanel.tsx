/**
 * RightPanel - Preview panel and tools tabs
 */

import { ChevronLeft, ChevronRight, Presentation, Film, Undo2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { ButtonGroup, ButtonGroupSeparator } from '@/components/ui/button-group';
import { OutputStage, type OutputMediaLayer } from '@/components/output/OutputStage';
import { Switch } from '@/components/ui/switch';
import { useCallback, useMemo } from 'react';
import { useLiveStore, useEditorStore, useSettingsStore, useShowStore } from '@/lib/stores';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import {
  ContextMenu,
  ContextMenuContent,
  ContextMenuItem,
  ContextMenuSeparator,
  ContextMenuTrigger,
} from '@/components/ui/context-menu';
import { useResolvedMediaUrl } from '@/lib/media/resolveMediaUrl';
import { cn } from '@/lib/utils';
import type { MediaLayersState, MediaLayerId } from '@/lib/models';
import { getBackgroundMediaId, resolveSlideBackground } from '@/lib/models';

interface RightPanelProps {
  onOpenOutputSettings?: () => void;
  collapsed: boolean;
  onToggleCollapse: () => void;
}

export function RightPanel({ onOpenOutputSettings: _onOpenOutputSettings, collapsed, onToggleCollapse }: RightPanelProps) {
  const { presentation, pendingMedia } = useEditorStore();
  const { selectedSlideId, clearSelectedSlide, setSelectedSlideId } = useShowStore();
  const { settings, updateSettings } = useSettingsStore();
  const {
    isLive,
    currentSlideId,
    isBlackout,
    isClear,
    visibleLayerIds,
    mediaLayers: rawMediaLayers,
    presentationPath,
    clearPresentation,
    clearMedia,
    undoClearPresentation,
    undoClearMedia,
    _finishClearPresentation,
    _finishClearMedia,
    suppress: rawSuppress,
    isClearing: rawIsClearing,
    canUndoClearPresentation,
    canUndoClearMedia,
  } = useLiveStore();
  
  // Provide defaults in case state is not yet initialized
  const mediaLayers = rawMediaLayers ?? { mediaUnderlay: null, mediaOverlay: null, audio: null };
  const suppress = rawSuppress ?? { presentation: false, media: false };
  const isClearing = rawIsClearing ?? { presentation: false, media: false };
  
  // Callbacks for when clearing animations complete
  const handleClearPresentationComplete = useCallback(() => {
    _finishClearPresentation();
    // Also clear the selected slide in showStore (for preview mode)
    clearSelectedSlide('live');
  }, [_finishClearPresentation, clearSelectedSlide]);
  
  const handleClearMediaComplete = useCallback(() => {
    _finishClearMedia();
  }, [_finishClearMedia]);
  
  // Wrapped undo that also restores selectedSlideId
  const handleUndoClearPresentation = useCallback(() => {
    const restoredSlideId = undoClearPresentation();
    if (restoredSlideId) {
      setSelectedSlideId(restoredSlideId, 'live');
    }
  }, [undoClearPresentation, setSelectedSlideId]);

  // Get the current slide and presentation aspect ratio
  const currentSlide = presentation?.slides.find((s) => s.id === currentSlideId) || null;
  const selectedSlide = presentation?.slides.find((s) => s.id === selectedSlideId) || null;
  const aspectRatio = presentation?.manifest.aspectRatio;
  const outputAspectRatio = settings.output.aspectRatio;
  const aspectClass = getAspectClass(outputAspectRatio);
  const isAudienceLive = settings.output.audienceEnabled && isLive;
  const previewSlide = isAudienceLive ? currentSlide : selectedSlide;
  const previewVisibleLayerIds = isAudienceLive ? visibleLayerIds : undefined;
  const previewBlackout = isAudienceLive ? isBlackout : false;
  const previewClear = isAudienceLive ? isClear : false;
  const previewRenderScale = 0.5;

  const effectiveVisibleLayerIds = useMemo(() => {
    if (!previewSlide) return undefined;
    const hasAdvanceBuilds = previewSlide.animations?.buildIn?.some(
      (step) => step.trigger === 'onAdvance'
    );
    if (!hasAdvanceBuilds && (!previewVisibleLayerIds || previewVisibleLayerIds.length === 0)) {
      return undefined;
    }
    return previewVisibleLayerIds;
  }, [previewSlide, previewVisibleLayerIds]);

  // Resolve media URLs for media layers
  const resolvedMediaUnderlaySrc = useResolvedMediaUrl({
    mediaId: mediaLayers.mediaUnderlay?.mediaId,
    presentation,
    presentationPath,
    pendingMedia,
  });
  const resolvedMediaOverlaySrc = useResolvedMediaUrl({
    mediaId: mediaLayers.mediaOverlay?.mediaId,
    presentation,
    presentationPath,
    pendingMedia,
  });

  // Resolve background media URL (for image/video backgrounds)
  const effectiveBackground = previewSlide ? resolveSlideBackground(previewSlide) : null;
  const backgroundMediaId = effectiveBackground ? getBackgroundMediaId(effectiveBackground) : undefined;
  const resolvedBackgroundSrc = useResolvedMediaUrl({
    mediaId: backgroundMediaId,
    presentation,
    presentationPath,
    pendingMedia,
  });

  // Build the preview media layers state
  // When not live, apply slide media cues to preview
  const previewMediaLayers = useMemo<MediaLayersState>(() => {
    const nextState: MediaLayersState = {
      mediaUnderlay: mediaLayers.mediaUnderlay,
      mediaOverlay: mediaLayers.mediaOverlay,
      audio: mediaLayers.audio,
    };
    
    if (!isAudienceLive && previewSlide?.mediaCues?.length && !suppress.media) {
      previewSlide.mediaCues.forEach((cue) => {
        const target = cue.target as MediaLayerId;
        nextState[target] = {
          mediaId: cue.mediaId,
          mediaType: cue.mediaType,
          fit: cue.fit,
          loop: cue.loop,
          muted: cue.muted,
          autoplay: cue.autoplay,
        };
      });
    }
    return nextState;
  }, [mediaLayers, isAudienceLive, previewSlide, suppress.media]);

  // Add resolved URLs to media layers for rendering
  const previewMediaLayersWithSrc = useMemo(() => ({
    ...previewMediaLayers,
    mediaUnderlay: previewMediaLayers.mediaUnderlay
      ? { ...previewMediaLayers.mediaUnderlay, src: resolvedMediaUnderlaySrc ?? undefined } as OutputMediaLayer
      : null,
    mediaOverlay: previewMediaLayers.mediaOverlay
      ? { ...previewMediaLayers.mediaOverlay, src: resolvedMediaOverlaySrc ?? undefined } as OutputMediaLayer
      : null,
  }), [previewMediaLayers, resolvedMediaUnderlaySrc, resolvedMediaOverlaySrc]);

  // Determine if there's active presentation content (slide + background)
  const hasPresentationContent = useMemo(() => {
    if (!previewSlide) return false;
    // Has content if slide exists and isn't suppressed
    return !suppress.presentation;
  }, [previewSlide, suppress.presentation]);

  // Determine if there's active media content (underlay, overlay, or audio)
  const hasMediaContent = useMemo(() => {
    if (suppress.media) return false;
    return Boolean(
      previewMediaLayers.mediaUnderlay ||
      previewMediaLayers.mediaOverlay ||
      previewMediaLayers.audio
    );
  }, [previewMediaLayers, suppress.media]);

  if (collapsed) {
    return (
      <div className="flex h-full flex-col items-center bg-card">
        <div className="p-1">
          <Tooltip>
            <TooltipTrigger asChild>
              <Button
                variant="ghost"
                size="icon"
                className="h-7 w-7"
                onClick={onToggleCollapse}
              >
                <ChevronLeft className="h-4 w-4" />
              </Button>
            </TooltipTrigger>
            <TooltipContent side="left">
              <p>Expand Output</p>
            </TooltipContent>
          </Tooltip>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-full flex-col bg-card">
      <div className="flex h-14 items-center justify-between border-b px-4 py-3">
        <div className="flex items-center gap-3">
          <Switch
            checked={settings.output.audienceEnabled}
            onCheckedChange={(checked) =>
              updateSettings({
                output: { ...settings.output, audienceEnabled: checked },
              })
            }
            className="h-6 w-10 p-1 data-[state=checked]:bg-green-500 data-[state=unchecked]:bg-red-500"
          />
          <span className="text-sm font-semibold">Audience</span>
        </div>
        <Tooltip>
          <TooltipTrigger asChild>
            <Button
              variant="ghost"
              size="icon"
              className="h-7 w-7"
              onClick={onToggleCollapse}
            >
              <ChevronRight className="h-4 w-4" />
            </Button>
          </TooltipTrigger>
          <TooltipContent side="bottom">
            <p>Collapse Output</p>
          </TooltipContent>
        </Tooltip>
      </div>

      <div className="flex-1 p-3">
        <div className="rounded-md overflow-hidden border bg-black">
          <div className={aspectClass}>
            <div
              className="origin-top-left"
              style={{
                width: `${previewRenderScale * 100}%`,
                height: `${previewRenderScale * 100}%`,
                transform: `scale(${1 / previewRenderScale})`,
              }}
            >
              <OutputStage
                slide={previewSlide}
                aspectRatio={aspectRatio}
                outputAspectRatio={outputAspectRatio}
                slideSize={presentation?.manifest.slideSize}
                isBlackout={previewBlackout}
                isClear={previewClear}
                visibleLayerIds={effectiveVisibleLayerIds}
                mediaLayers={previewMediaLayersWithSrc}
                suppress={suppress}
                isClearing={isClearing}
                onClearPresentationComplete={handleClearPresentationComplete}
                onClearMediaComplete={handleClearMediaComplete}
                resolvedBackgroundSrc={resolvedBackgroundSrc}
                className="w-full h-full pointer-events-none select-none"
              />
            </div>
          </div>

          <ContextMenu>
            <ContextMenuTrigger asChild>
              <div className="border-t bg-background overflow-hidden">
                <ButtonGroup className="w-full overflow-hidden">
                  {/* Clear Presentation button with inline undo */}
                  <div className="flex-1 flex items-stretch">
                    <Button
                      variant="ghost"
                      size="sm"
                      className={cn(
                        'flex-1 h-auto px-2 py-2 text-xs hover:bg-muted/80 rounded-none rounded-r-none',
                        hasPresentationContent && 'bg-primary/20 text-primary hover:bg-primary/30'
                      )}
                      onClick={clearPresentation}
                      disabled={isClearing.presentation}
                    >
                      <span className="flex flex-col items-center gap-1">
                        <Presentation className="h-4 w-4" />
                        <span>Presentation</span>
                      </span>
                    </Button>
                    {canUndoClearPresentation && (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button
                            variant="ghost"
                            size="sm"
                            className="h-auto px-2 py-2 text-xs hover:bg-muted/80 rounded-none rounded-l-none border-l"
                            onClick={handleUndoClearPresentation}
                          >
                            <Undo2 className="h-3 w-3" />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>
                          <p>Undo Clear</p>
                        </TooltipContent>
                      </Tooltip>
                    )}
                  </div>
                  <ButtonGroupSeparator />
                  {/* Clear Media button with inline undo */}
                  <div className="flex-1 flex items-stretch">
                    <Button
                      variant="ghost"
                      size="sm"
                      className={cn(
                        'flex-1 h-auto px-2 py-2 text-xs hover:bg-muted/80 rounded-none rounded-r-none',
                        hasMediaContent && 'bg-primary/20 text-primary hover:bg-primary/30'
                      )}
                      onClick={clearMedia}
                      disabled={isClearing.media}
                    >
                      <span className="flex flex-col items-center gap-1">
                        <Film className="h-4 w-4" />
                        <span>Media</span>
                      </span>
                    </Button>
                    {canUndoClearMedia && (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <Button
                            variant="ghost"
                            size="sm"
                            className="h-auto px-2 py-2 text-xs hover:bg-muted/80 rounded-none rounded-l-none border-l"
                            onClick={undoClearMedia}
                          >
                            <Undo2 className="h-3 w-3" />
                          </Button>
                        </TooltipTrigger>
                        <TooltipContent>
                          <p>Undo Clear</p>
                        </TooltipContent>
                      </Tooltip>
                    )}
                  </div>
                </ButtonGroup>
              </div>
            </ContextMenuTrigger>
            <ContextMenuContent>
              <ContextMenuItem onClick={clearPresentation} disabled={isClearing.presentation}>
                Clear Presentation
              </ContextMenuItem>
              <ContextMenuItem onClick={clearMedia} disabled={isClearing.media}>
                Clear Media
              </ContextMenuItem>
              {(canUndoClearPresentation || canUndoClearMedia) && (
                <>
                  <ContextMenuSeparator />
                  {canUndoClearPresentation && (
                    <ContextMenuItem onClick={handleUndoClearPresentation}>
                      Undo Clear Presentation
                    </ContextMenuItem>
                  )}
                  {canUndoClearMedia && (
                    <ContextMenuItem onClick={undoClearMedia}>
                      Undo Clear Media
                    </ContextMenuItem>
                  )}
                </>
              )}
            </ContextMenuContent>
          </ContextMenu>
        </div>
      </div>
    </div>
  );
}

function getAspectClass(aspectRatio?: '16:9' | '4:3' | '16:10'): string {
  if (aspectRatio === '4:3') return 'aspect-[4/3]';
  if (aspectRatio === '16:10') return 'aspect-[16/10]';
  return 'aspect-video';
}
