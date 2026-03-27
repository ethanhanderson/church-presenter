import { useEffect, useMemo, useState } from 'react';
import type { Background, MediaEntry, Presentation } from '@/lib/models';
import { getBackgroundMediaId } from '@/lib/models';
import { readBundleMedia } from '@/lib/tauri-api';

type ResolveInput = {
  mediaId: string | null | undefined;
  presentation: Presentation | null | undefined;
  presentationPath?: string | null;
  pendingMedia?: Map<string, string>;
};

const blobUrlCache = new Map<string, string>();

function isTauriEnvironment() {
  return (
    typeof window !== 'undefined' &&
    ('__TAURI_INTERNALS__' in window || '__TAURI__' in window)
  );
}

function getMediaEntry(presentation: Presentation | null | undefined, mediaId: string) {
  return presentation?.manifest.media?.find((entry) => entry.id === mediaId) || null;
}

export async function resolveMediaUrl({
  mediaId,
  presentation,
  presentationPath,
  pendingMedia,
}: ResolveInput): Promise<string | null> {
  if (!mediaId || !presentation) return null;

  const pendingUrl = pendingMedia?.get(mediaId);
  if (pendingUrl) {
    return pendingUrl;
  }

  const entry = getMediaEntry(presentation, mediaId);
  if (!entry) {
    return mediaId;
  }

  if (!presentationPath || !isTauriEnvironment()) {
    return entry.path || mediaId;
  }

  const cacheKey = `${presentationPath}::${entry.path}`;
  const cached = blobUrlCache.get(cacheKey);
  if (cached) return cached;

  const url = await createBlobUrl(presentationPath, entry);
  if (url) {
    blobUrlCache.set(cacheKey, url);
  }
  return url;
}

export function useResolvedMediaUrl({
  mediaId,
  presentation,
  presentationPath,
  pendingMedia,
}: ResolveInput) {
  const [src, setSrc] = useState<string | null>(null);

  const stableKey = useMemo(() => {
    if (!mediaId || !presentation) return null;
    return `${presentationPath ?? ''}:${mediaId}`;
  }, [mediaId, presentation, presentationPath]);

  useEffect(() => {
    let cancelled = false;
    if (!mediaId || !presentation) {
      setSrc(null);
      return;
    }

    resolveMediaUrl({ mediaId, presentation, presentationPath, pendingMedia })
      .then((url) => {
        if (!cancelled) setSrc(url);
      })
      .catch(() => {
        if (!cancelled) setSrc(null);
      });

    return () => {
      cancelled = true;
    };
  }, [stableKey, mediaId, presentation, presentationPath, pendingMedia]);

  return src;
}

type ResolveBackgroundInput = {
  background: Background | null | undefined;
  presentation: Presentation | null | undefined;
  presentationPath?: string | null;
  pendingMedia?: Map<string, string>;
};

export function useResolvedBackgroundMediaSrc({
  background,
  presentation,
  presentationPath,
  pendingMedia,
}: ResolveBackgroundInput) {
  const mediaId = useMemo(
    () => (background ? getBackgroundMediaId(background) : undefined),
    [background]
  );

  return useResolvedMediaUrl({
    mediaId,
    presentation,
    presentationPath,
    pendingMedia,
  });
}

async function createBlobUrl(bundlePath: string, entry: MediaEntry) {
  try {
    const data = await readBundleMedia(bundlePath, entry.path);
    const blob = new Blob([data], { type: entry.mime });
    return URL.createObjectURL(blob);
  } catch {
    return null;
  }
}
