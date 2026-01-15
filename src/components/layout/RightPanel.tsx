/**
 * RightPanel - Preview panel and tools tabs
 */

import { ChevronLeft, ChevronRight } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { AnimatedSlideStage } from '@/components/preview/AnimatedSlideStage';
import { Switch } from '@/components/ui/switch';
import { useMemo } from 'react';
import { useLiveStore, useEditorStore, useSettingsStore, useShowStore } from '@/lib/stores';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';

interface RightPanelProps {
  onOpenOutputSettings?: () => void;
  collapsed: boolean;
  onToggleCollapse: () => void;
}

export function RightPanel({ onOpenOutputSettings: _onOpenOutputSettings, collapsed, onToggleCollapse }: RightPanelProps) {
  const { presentation } = useEditorStore();
  const { selectedSlideId } = useShowStore();
  const { settings, updateSettings } = useSettingsStore();
  const {
    isLive,
    currentSlideId,
    isBlackout,
    isClear,
    visibleLayerIds,
  } = useLiveStore();

  // Get the current slide and theme
  const currentSlide = presentation?.slides.find((s) => s.id === currentSlideId) || null;
  const selectedSlide = presentation?.slides.find((s) => s.id === selectedSlideId) || null;
  const theme = presentation?.themes.find(
    (t) => t.id === presentation.manifest.themeId
  ) || null;
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
        <div className="rounded-md overflow-hidden border bg-muted slide-aspect">
          <div className="h-full w-full">
            <div
              className="origin-top-left"
              style={{
                width: `${previewRenderScale * 100}%`,
                height: `${previewRenderScale * 100}%`,
                transform: `scale(${1 / previewRenderScale})`,
              }}
            >
              <AnimatedSlideStage
                slide={previewSlide}
                theme={theme}
                isBlackout={previewBlackout}
                isClear={previewClear}
                visibleLayerIds={effectiveVisibleLayerIds}
                className="w-full h-full"
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
