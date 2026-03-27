import type { Background } from '@/lib/models';
import { cn } from '@/lib/utils';

interface BackgroundMediaProps {
  background?: Background | null;
  src?: string | null;
  className?: string;
}

const clamp = (value: number, min = 0, max = 1) =>
  Number.isFinite(value) ? Math.min(max, Math.max(min, value)) : min;

export function BackgroundMedia({ background, src, className }: BackgroundMediaProps) {
  if (!background) return null;

  if (background.type === 'image') {
    const mediaSrc = src ?? background.mediaId;
    if (!mediaSrc) return null;
    const position = background.position ?? { x: 50, y: 50 };
    const opacity = clamp(background.opacity ?? 1);
    return (
      <img
        src={mediaSrc}
        alt=""
        draggable={false}
        className={cn(className)}
        style={{
          width: '100%',
          height: '100%',
          objectFit: background.fit ?? 'cover',
          objectPosition: `${position.x}% ${position.y}%`,
          opacity,
        }}
      />
    );
  }

  if (background.type === 'video') {
    const mediaSrc = src ?? background.mediaId;
    if (!mediaSrc) return null;
    const opacity = clamp(background.opacity ?? 1);
    return (
      <video
        src={mediaSrc}
        loop={background.loop ?? true}
        muted={background.muted ?? true}
        autoPlay
        playsInline
        className={cn(className)}
        style={{
          width: '100%',
          height: '100%',
          objectFit: background.fit ?? 'cover',
          opacity,
        }}
      />
    );
  }

  return null;
}
