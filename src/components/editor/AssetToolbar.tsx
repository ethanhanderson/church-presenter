/**
 * AssetToolbar - Floating toolbar for inserting content into slides
 */

import { useState, useMemo, useRef, useEffect } from 'react';
import {
  Type,
  Square,
  Circle,
  Triangle,
  Minus,
  Image,
  Video,
  Globe,
  Upload,
  ChevronDown,
  Heading1,
  Loader2,
  ExternalLink,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { ScrollArea } from '@/components/ui/scroll-area';
import { Separator } from '@/components/ui/separator';
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from '@/components/ui/popover';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from '@/components/ui/tooltip';
import { useEditorStore } from '@/lib/stores';
import type { ShapeType, MediaEntry } from '@/lib/models';
import { cn } from '@/lib/utils';

interface AssetToolbarProps {
  slideId: string | null;
}

export function AssetToolbar({ slideId }: AssetToolbarProps) {
  const {
    presentation,
    addTextLayer,
    addShapeLayer,
    addMediaLayer,
    addWebLayer,
    addMedia,
  } = useEditorStore();

  const [webUrl, setWebUrl] = useState('');
  const [webPopoverOpen, setWebPopoverOpen] = useState(false);
  const [mediaPopoverOpen, setMediaPopoverOpen] = useState(false);

  if (!slideId) {
    return null;
  }

  const handleAddText = () => {
    addTextLayer(slideId);
  };

  const handleAddHeading = () => {
    const layer = addTextLayer(slideId, '');
    if (layer) {
      useEditorStore.getState().updateLayer(slideId, layer.id, {
        style: {
          font: {
            family: 'Inter',
            size: 96,
            weight: 800,
            italic: false,
            lineHeight: 1.1,
            letterSpacing: -2,
          },
        },
      });
    }
  };

  const handleAddShape = (shapeType: ShapeType) => {
    addShapeLayer(slideId, shapeType);
  };

  const handleAddMedia = (media: MediaEntry) => {
    addMediaLayer(slideId, media.id, media.type === 'video' ? 'video' : 'image');
    setMediaPopoverOpen(false);
  };

  const handleAddWebLayer = () => {
    if (!webUrl.trim()) return;
    
    let url = webUrl.trim();
    if (!url.startsWith('http://') && !url.startsWith('https://')) {
      url = 'https://' + url;
    }
    
    addWebLayer(slideId, url);
    setWebUrl('');
    setWebPopoverOpen(false);
  };

  const mediaItems = presentation?.manifest.media || [];

  return (
    <div className="flex items-center justify-center gap-1 px-3 py-2 bg-background/95 backdrop-blur supports-[backdrop-filter]:bg-background/80 border rounded-xl shadow-lg">
      {/* Text */}
      <DropdownMenu>
        <Tooltip>
          <TooltipTrigger asChild>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="sm" className="gap-1 px-2">
                <Type className="h-4 w-4" />
                <ChevronDown className="h-3 w-3 opacity-50" />
              </Button>
            </DropdownMenuTrigger>
          </TooltipTrigger>
          <TooltipContent side="top">
            <p>Add Text</p>
          </TooltipContent>
        </Tooltip>
        <DropdownMenuContent align="center" className="w-40">
          <DropdownMenuItem onClick={handleAddText}>
            <Type className="mr-2 h-4 w-4" />
            Text Box
          </DropdownMenuItem>
          <DropdownMenuItem onClick={handleAddHeading}>
            <Heading1 className="mr-2 h-4 w-4" />
            Heading
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>

      <Separator orientation="vertical" className="h-6 mx-1" />

      {/* Shapes */}
      <Popover>
        <Tooltip>
          <TooltipTrigger asChild>
            <PopoverTrigger asChild>
              <Button variant="ghost" size="sm" className="gap-1 px-2">
                <Square className="h-4 w-4" />
                <ChevronDown className="h-3 w-3 opacity-50" />
              </Button>
            </PopoverTrigger>
          </TooltipTrigger>
          <TooltipContent side="top">
            <p>Add Shape</p>
          </TooltipContent>
        </Tooltip>
        <PopoverContent align="center" className="w-auto p-2">
          <div className="grid grid-cols-4 gap-1">
            <ShapeButton
              icon={<Square className="h-5 w-5" />}
              label="Rectangle"
              onClick={() => handleAddShape('rectangle')}
            />
            <ShapeButton
              icon={<Circle className="h-5 w-5" />}
              label="Ellipse"
              onClick={() => handleAddShape('ellipse')}
            />
            <ShapeButton
              icon={<Triangle className="h-5 w-5" />}
              label="Triangle"
              onClick={() => handleAddShape('triangle')}
            />
            <ShapeButton
              icon={<Minus className="h-5 w-5" />}
              label="Line"
              onClick={() => handleAddShape('line')}
            />
          </div>
        </PopoverContent>
      </Popover>

      <Separator orientation="vertical" className="h-6 mx-1" />

      {/* Media */}
      <Popover open={mediaPopoverOpen} onOpenChange={setMediaPopoverOpen}>
        <Tooltip>
          <TooltipTrigger asChild>
            <PopoverTrigger asChild>
              <Button variant="ghost" size="sm" className="gap-1 px-2">
                <Image className="h-4 w-4" />
                <ChevronDown className="h-3 w-3 opacity-50" />
              </Button>
            </PopoverTrigger>
          </TooltipTrigger>
          <TooltipContent side="top">
            <p>Add Media</p>
          </TooltipContent>
        </Tooltip>
        <PopoverContent align="center" className="w-72 p-3">
          <div className="space-y-3">
            <div className="flex items-center justify-between">
              <span className="text-sm font-medium">Media</span>
              <ImportMediaButton onImport={(entry, path) => {
                addMedia(entry, path);
              }} />
            </div>
            
            {mediaItems.length === 0 ? (
              <div className="text-center py-6 text-muted-foreground">
                <Image className="h-8 w-8 mx-auto mb-2 opacity-50" />
                <p className="text-xs">No media in presentation</p>
                <p className="text-[10px]">Import images or videos</p>
              </div>
            ) : (
              <ScrollArea className="h-40">
                <div className="grid grid-cols-3 gap-2">
                  {mediaItems.map((media) => (
                    <MediaThumbnail
                      key={media.id}
                      media={media}
                      onClick={() => handleAddMedia(media)}
                    />
                  ))}
                </div>
              </ScrollArea>
            )}
          </div>
        </PopoverContent>
      </Popover>

      <Separator orientation="vertical" className="h-6 mx-1" />

      {/* Web */}
      <Popover open={webPopoverOpen} onOpenChange={(open) => {
        setWebPopoverOpen(open);
        if (!open) setWebUrl('');
      }}>
        <Tooltip>
          <TooltipTrigger asChild>
            <PopoverTrigger asChild>
              <Button variant="ghost" size="sm" className="gap-1 px-2">
                <Globe className="h-4 w-4" />
                <ChevronDown className="h-3 w-3 opacity-50" />
              </Button>
            </PopoverTrigger>
          </TooltipTrigger>
          <TooltipContent side="top">
            <p>Add Web Page</p>
          </TooltipContent>
        </Tooltip>
        <PopoverContent align="center" className="w-80 p-3">
          <WebLayerPopover
            url={webUrl}
            onUrlChange={setWebUrl}
            onAdd={handleAddWebLayer}
          />
        </PopoverContent>
      </Popover>
    </div>
  );
}

interface ShapeButtonProps {
  icon: React.ReactNode;
  label: string;
  onClick: () => void;
}

function ShapeButton({ icon, label, onClick }: ShapeButtonProps) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className="h-10 w-10"
          onClick={onClick}
        >
          {icon}
        </Button>
      </TooltipTrigger>
      <TooltipContent side="bottom">
        <p>{label}</p>
      </TooltipContent>
    </Tooltip>
  );
}

interface ImportMediaButtonProps {
  onImport: (entry: MediaEntry, sourcePath: string) => void;
}

function ImportMediaButton({ onImport }: ImportMediaButtonProps) {
  const handleImport = async () => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/*,video/*';
    input.onchange = async (e) => {
      const file = (e.target as HTMLInputElement).files?.[0];
      if (!file) return;

      const entry: MediaEntry = {
        id: crypto.randomUUID(),
        filename: file.name,
        path: `media/${crypto.randomUUID()}.${file.name.split('.').pop()}`,
        mime: file.type,
        sha256: '',
        byteSize: file.size,
        type: file.type.startsWith('video/') ? 'video' : 'image',
      };

      const objectUrl = URL.createObjectURL(file);
      onImport(entry, objectUrl);
    };
    input.click();
  };

  return (
    <Button variant="ghost" size="sm" className="h-7 text-xs" onClick={handleImport}>
      <Upload className="mr-1 h-3 w-3" />
      Import
    </Button>
  );
}

interface MediaThumbnailProps {
  media: MediaEntry;
  onClick: () => void;
}

function MediaThumbnail({ media, onClick }: MediaThumbnailProps) {
  return (
    <button
      className={cn(
        'relative aspect-video rounded-md overflow-hidden border-2 border-transparent',
        'hover:border-primary transition-colors bg-muted'
      )}
      onClick={onClick}
    >
      {media.type === 'video' ? (
        <div className="absolute inset-0 flex items-center justify-center">
          <Video className="h-5 w-5 text-muted-foreground" />
        </div>
      ) : (
        <img
          src={media.path}
          alt={media.filename}
          className="w-full h-full object-cover"
        />
      )}
      <div className="absolute bottom-0 left-0 right-0 bg-black/60 px-1 py-0.5">
        <p className="text-[8px] text-white truncate">{media.filename}</p>
      </div>
    </button>
  );
}

interface WebLayerPopoverProps {
  url: string;
  onUrlChange: (url: string) => void;
  onAdd: () => void;
}

function WebLayerPopover({ url, onUrlChange, onAdd }: WebLayerPopoverProps) {
  const [isLoading, setIsLoading] = useState(false);
  const [hasError, setHasError] = useState(false);

  // Normalize the URL for preview
  const previewUrl = useMemo(() => {
    if (!url.trim()) return '';
    let normalized = url.trim();
    if (!normalized.startsWith('http://') && !normalized.startsWith('https://')) {
      normalized = 'https://' + normalized;
    }
    try {
      new URL(normalized);
      return normalized;
    } catch {
      return '';
    }
  }, [url]);

  const isValidUrl = previewUrl.length > 0;

  return (
    <div className="space-y-3">
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium">Embed Web Page</span>
        {previewUrl && (
          <a
            href={previewUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-xs text-muted-foreground hover:text-foreground flex items-center gap-1"
          >
            <ExternalLink className="h-3 w-3" />
            Open
          </a>
        )}
      </div>
      
      <div className="space-y-2">
        <Label htmlFor="web-url" className="text-xs">URL</Label>
        <Input
          id="web-url"
          placeholder="Paste a website URL..."
          value={url}
          onChange={(e) => {
            onUrlChange(e.target.value);
            setHasError(false);
            setIsLoading(true);
          }}
          onKeyDown={(e) => e.key === 'Enter' && isValidUrl && onAdd()}
          className="h-8 text-sm"
        />
      </div>

      {/* Preview - scaled down to show full page at slide scale */}
      {previewUrl && (
        <div className="space-y-2">
          <ScaledWebPreview
            url={previewUrl}
            isLoading={isLoading}
            hasError={hasError}
            onLoad={() => setIsLoading(false)}
            onError={() => {
              setIsLoading(false);
              setHasError(true);
            }}
          />
          <p className="text-[10px] text-muted-foreground truncate">
            {previewUrl}
          </p>
        </div>
      )}

      {!previewUrl && url.trim() && (
        <div className="text-xs text-destructive">
          Please enter a valid URL
        </div>
      )}

      <Button 
        className="w-full"
        onClick={onAdd}
        disabled={!isValidUrl}
      >
        <Globe className="mr-2 h-4 w-4" />
        Add Web Layer
      </Button>
    </div>
  );
}

interface ScaledWebPreviewProps {
  url: string;
  isLoading: boolean;
  hasError: boolean;
  onLoad: () => void;
  onError: () => void;
}

function ScaledWebPreview({ url, isLoading, hasError, onLoad, onError }: ScaledWebPreviewProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [scale, setScale] = useState(0.15);

  // Base resolution for the iframe (1920x1080 = 16:9)
  const baseWidth = 1920;
  const baseHeight = 1080;

  useEffect(() => {
    const updateScale = () => {
      if (containerRef.current) {
        const containerWidth = containerRef.current.offsetWidth;
        setScale(containerWidth / baseWidth);
      }
    };

    updateScale();
    window.addEventListener('resize', updateScale);
    return () => window.removeEventListener('resize', updateScale);
  }, []);

  return (
    <div 
      ref={containerRef}
      className="relative aspect-video rounded-md overflow-hidden border bg-muted"
    >
      {isLoading && !hasError && (
        <div className="absolute inset-0 flex items-center justify-center bg-muted z-10">
          <Loader2 className="h-5 w-5 animate-spin text-muted-foreground" />
        </div>
      )}
      {hasError ? (
        <div className="absolute inset-0 flex flex-col items-center justify-center bg-muted text-muted-foreground">
          <Globe className="h-6 w-6 mb-1 opacity-50" />
          <span className="text-xs">Preview unavailable</span>
        </div>
      ) : (
        <div 
          className="absolute top-0 left-0 origin-top-left"
          style={{
            width: `${baseWidth}px`,
            height: `${baseHeight}px`,
            transform: `scale(${scale})`,
          }}
        >
          <iframe
            src={url}
            className="w-full h-full border-0 bg-white"
            title="Web preview"
            sandbox="allow-scripts allow-same-origin"
            onLoad={onLoad}
            onError={onError}
          />
        </div>
      )}
    </div>
  );
}
