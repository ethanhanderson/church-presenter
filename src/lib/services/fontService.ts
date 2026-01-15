import type { Presentation } from '@/lib/models';
import { readBundleMedia } from '@/lib/tauri-api';

const loadedFontIds = new Set<string>();

const toArrayBuffer = (data: Uint8Array) =>
  data.buffer.slice(data.byteOffset, data.byteOffset + data.byteLength);

export async function loadBundledFonts(
  presentation: Presentation | null,
  bundlePath: string | null
): Promise<void> {
  const isTauriApp =
    typeof window !== 'undefined' &&
    ('__TAURI_INTERNALS__' in window || '__TAURI__' in window);
  if (!isTauriApp) return;
  if (!presentation || !bundlePath) return;
  const fonts = Array.isArray(presentation.manifest.fonts) ? presentation.manifest.fonts : [];
  if (fonts.length === 0) return;

  for (const font of fonts) {
    if (!font.path || loadedFontIds.has(font.id)) continue;

    try {
      const data = await readBundleMedia(bundlePath, font.path);
      const fontFace = new FontFace(font.family, toArrayBuffer(data), {
        weight: String(font.weight),
        style: font.style,
      });
      await fontFace.load();
      document.fonts.add(fontFace);
      loadedFontIds.add(font.id);
    } catch (error) {
      console.warn(`Failed to load bundled font ${font.family}`, error);
    }
  }
}
