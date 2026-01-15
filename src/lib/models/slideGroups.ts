import type { Slide, SongSection } from './types';
import { formatSectionLabel } from './defaults';

export const PROPRESENTER_GROUP_COLORS: Record<SongSection, string> = {
  intro: '#64748b',
  verse: '#3b82f6',
  'pre-chorus': '#06b6d4',
  chorus: '#10b981',
  bridge: '#8b5cf6',
  refrain: '#eab308',
  tag: '#f97316',
  vamp: '#a16207',
  interlude: '#22d3ee',
  outro: '#ef4444',
  ending: '#dc2626',
  custom: '#ec4899',
};

export function getSlideGroupColor(section?: SongSection | null): string | null {
  if (!section) return null;
  return PROPRESENTER_GROUP_COLORS[section] ?? null;
}

export function getSlideGroupLabel(slide: Slide): string | null {
  if (slide.sectionLabel && slide.sectionLabel.trim()) {
    return slide.sectionLabel.trim();
  }
  if (slide.section) {
    return formatSectionLabel(slide.section, slide.sectionIndex);
  }
  return null;
}

export function getSlideGroupKey(slide: Slide): string | null {
  const label = getSlideGroupLabel(slide);
  if (!label) return null;
  const sectionKey = slide.section ?? 'custom';
  return `${sectionKey}:${label}`;
}
