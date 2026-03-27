import type { Background, OutputLayerMedia, Slide } from './types';

const FALLBACK_BACKGROUND: Background = { type: 'solid', color: '#000000' };

export function resolveSlideBackground(slide: Slide | null): Background {
  return slide?.background ?? FALLBACK_BACKGROUND;
}

export function getBackgroundMediaId(background: Background): string | undefined {
  if (background.type === 'image' || background.type === 'video') {
    return background.mediaId;
  }
  return undefined;
}

export function getBackgroundMediaLayer(
  background: Background,
  src?: string | null
): OutputLayerMedia | null {
  if (background.type === 'image') {
    return {
      mediaId: background.mediaId,
      mediaType: 'image',
      fit: background.fit,
      src: src ?? undefined,
    };
  }
  if (background.type === 'video') {
    return {
      mediaId: background.mediaId,
      mediaType: 'video',
      fit: background.fit,
      loop: background.loop,
      muted: background.muted,
      autoplay: true,
      src: src ?? undefined,
    };
  }
  return null;
}

export function getBackgroundStyle(background: Background): Record<string, string | number> {
  switch (background.type) {
    case 'solid':
      return { backgroundColor: background.color };
    case 'gradient': {
      const stops = background.stops
        .map((stop) => `${stop.color} ${stop.position}%`)
        .join(', ');
      return {
        background: `linear-gradient(${background.angle}deg, ${stops})`,
      };
    }
    case 'transparent':
      return { backgroundColor: 'transparent' };
    case 'image':
    case 'video':
    default:
      return { backgroundColor: '#000' };
  }
}
