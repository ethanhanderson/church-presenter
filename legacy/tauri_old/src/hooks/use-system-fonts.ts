import { useCallback, useEffect, useMemo, useState } from 'react';
import { listSystemFonts, type SystemFontInfo } from '@/lib/tauri-api';

const fallbackFonts: SystemFontInfo[] = [
  {
    family: 'Inter',
    full_name: 'Inter',
    postscript_name: null,
    path: '',
    weight: 400,
    style: 'normal',
  },
  {
    family: 'Arial',
    full_name: 'Arial',
    postscript_name: null,
    path: '',
    weight: 400,
    style: 'normal',
  },
  {
    family: 'Georgia',
    full_name: 'Georgia',
    postscript_name: null,
    path: '',
    weight: 400,
    style: 'normal',
  },
  {
    family: 'Times New Roman',
    full_name: 'Times New Roman',
    postscript_name: null,
    path: '',
    weight: 400,
    style: 'normal',
  },
  {
    family: 'Courier New',
    full_name: 'Courier New',
    postscript_name: null,
    path: '',
    weight: 400,
    style: 'normal',
  },
];

let cachedFonts: SystemFontInfo[] | null = null;
let inFlight: Promise<SystemFontInfo[]> | null = null;
let cachedSignature: string | null = null;
let lastFetchedAt = 0;

const refreshIntervalMs = 30_000;

const getFontsSignature = (fonts: SystemFontInfo[]) =>
  fonts
    .map((font) => `${font.family}|${font.full_name}|${font.path}|${font.weight}|${font.style}`)
    .sort()
    .join('::');

const fetchFonts = async () => {
  const result = await listSystemFonts();
  const signature = getFontsSignature(result);
  const hasChanged = signature !== cachedSignature;
  cachedFonts = result;
  cachedSignature = signature;
  lastFetchedAt = Date.now();
  return { fonts: result, hasChanged };
};

export function useSystemFonts() {
  const [fonts, setFonts] = useState<SystemFontInfo[]>(cachedFonts ?? []);
  const [isLoading, setIsLoading] = useState(!cachedFonts);
  const fontsSignature = useMemo(() => getFontsSignature(fonts), [fonts]);

  useEffect(() => {
    const isTauriApp =
      typeof window !== 'undefined' &&
      ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);
    if (!isTauriApp) {
      setFonts(fallbackFonts);
      setIsLoading(false);
      return;
    }

    if (cachedFonts) {
      setIsLoading(false);
      return;
    }

    if (!inFlight) {
      inFlight = fetchFonts();
    }

    inFlight
      .then(({ fonts: result, hasChanged }) => {
        if (hasChanged || fontsSignature !== getFontsSignature(result)) {
          setFonts(result);
        }
      })
      .catch(() => {
        setFonts([]);
      })
      .finally(() => {
        setIsLoading(false);
        inFlight = null;
      });
  }, []);

  const refresh = useCallback(async (options?: { force?: boolean }) => {
    const isTauriApp =
      typeof window !== 'undefined' &&
      ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);
    if (!isTauriApp) {
      setFonts(fallbackFonts);
      return;
    }
    const isStale = Date.now() - lastFetchedAt > refreshIntervalMs;
    const shouldRefresh = options?.force || !cachedFonts || isStale;
    if (!shouldRefresh) return;
    if (!inFlight) {
      inFlight = fetchFonts();
    }
    setIsLoading(true);
    try {
      const { fonts: result, hasChanged } = await inFlight;
      if (hasChanged || fontsSignature !== getFontsSignature(result)) {
        setFonts(result);
      }
    } finally {
      setIsLoading(false);
      inFlight = null;
    }
  }, [fontsSignature]);

  useEffect(() => {
    if (typeof window === 'undefined') return;

    const handleFocus = () => {
      void refresh();
    };
    const handleVisibility = () => {
      if (document.visibilityState === 'visible') {
        void refresh();
      }
    };

    window.addEventListener('focus', handleFocus);
    document.addEventListener('visibilitychange', handleVisibility);

    return () => {
      window.removeEventListener('focus', handleFocus);
      document.removeEventListener('visibilitychange', handleVisibility);
    };
  }, [refresh]);

  return { fonts, isLoading, refresh };
}
