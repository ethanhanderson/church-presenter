export interface ParsedColor {
  r: number;
  g: number;
  b: number;
  a?: number;
}

const clamp = (value: number, min = 0, max = 1) => Math.min(max, Math.max(min, value));

export function parseColor(color: string): ParsedColor | null {
  const trimmed = color.trim();
  if (trimmed.startsWith('#')) {
    let hex = trimmed.replace('#', '');
    if (hex.length === 3) {
      hex = hex.split('').map((c) => c + c).join('');
    }
    if (hex.length === 6 || hex.length === 8) {
      const num = Number.parseInt(hex.slice(0, 6), 16);
      const r = (num >> 16) & 255;
      const g = (num >> 8) & 255;
      const b = num & 255;
      const a = hex.length === 8 ? Number.parseInt(hex.slice(6), 16) / 255 : undefined;
      return { r, g, b, a };
    }
    return null;
  }

  const rgbMatch = trimmed.match(
    /^rgba?\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})(?:\s*,\s*(\d*\.?\d+))?\s*\)$/i
  );
  if (!rgbMatch) return null;
  const r = Number.parseInt(rgbMatch[1], 10);
  const g = Number.parseInt(rgbMatch[2], 10);
  const b = Number.parseInt(rgbMatch[3], 10);
  const a = rgbMatch[4] !== undefined ? Number.parseFloat(rgbMatch[4]) : undefined;
  return { r, g, b, a };
}

export function toRgba(color: string, opacity = 1): string {
  const parsed = parseColor(color);
  if (!parsed) return color;
  const alpha = clamp((parsed.a ?? 1) * opacity);
  return `rgba(${parsed.r}, ${parsed.g}, ${parsed.b}, ${alpha})`;
}

export function toHexWithAlpha(color: string, opacity = 1): string {
  const parsed = parseColor(color);
  if (!parsed) return '#000000ff';
  const alpha = clamp((parsed.a ?? 1) * opacity);
  const toHex = (value: number) => value.toString(16).padStart(2, '0');
  return `#${toHex(parsed.r)}${toHex(parsed.g)}${toHex(parsed.b)}${toHex(Math.round(alpha * 255))}`;
}

export function splitHexAlpha(value: string): { color: string; opacity: number } {
  const parsed = parseColor(value);
  if (!parsed) return { color: '#000000', opacity: 1 };
  const toHex = (n: number) => n.toString(16).padStart(2, '0');
  return {
    color: `#${toHex(parsed.r)}${toHex(parsed.g)}${toHex(parsed.b)}`,
    opacity: parsed.a ?? 1,
  };
}
