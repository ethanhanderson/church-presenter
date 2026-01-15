import { useEffect, useState } from 'react';
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

export function useSystemFonts() {
  const [fonts, setFonts] = useState<SystemFontInfo[]>(cachedFonts ?? []);
  const [isLoading, setIsLoading] = useState(!cachedFonts);

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
      inFlight = listSystemFonts().then((result) => {
        cachedFonts = result;
        return result;
      });
    }

    inFlight
      .then((result) => {
        setFonts(result);
      })
      .catch(() => {
        setFonts([]);
      })
      .finally(() => {
        setIsLoading(false);
      });
  }, []);

  return { fonts, isLoading };
}
