import type { BlendMode } from './types';

export const BLEND_MODE_OPTIONS: { value: BlendMode; label: string }[] = [
  { value: 'normal', label: 'Normal' },
  { value: 'multiply', label: 'Multiply' },
  { value: 'screen', label: 'Screen' },
  { value: 'overlay', label: 'Overlay' },
  { value: 'darken', label: 'Darken' },
  { value: 'lighten', label: 'Lighten' },
  { value: 'color-dodge', label: 'Color Dodge' },
  { value: 'color-burn', label: 'Color Burn' },
  { value: 'hard-light', label: 'Hard Light' },
  { value: 'soft-light', label: 'Soft Light' },
  { value: 'difference', label: 'Difference' },
  { value: 'exclusion', label: 'Exclusion' },
];

const BLEND_MODE_SET = new Set<BlendMode>(BLEND_MODE_OPTIONS.map((mode) => mode.value));

export function resolveBlendMode(blendMode?: BlendMode): BlendMode {
  if (!blendMode) return 'normal';
  return BLEND_MODE_SET.has(blendMode) ? blendMode : 'normal';
}
