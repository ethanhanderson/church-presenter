import type { Slide, SongSection } from './types';
import { formatSectionLabel } from './defaults';

const normalizeText = (value: string) => value.trim().replace(/\s+/g, ' ');

export const normalizeLayoutType = (value?: string | null): string | null => {
  if (!value) return null;
  const normalized = normalizeText(value).toLowerCase();
  return normalized.length > 0 ? normalized : null;
};

const getSectionLabel = (section: SongSection, sectionIndex?: number, sectionLabel?: string) => {
  if (sectionLabel && sectionLabel.trim()) {
    return normalizeText(sectionLabel);
  }
  return formatSectionLabel(section, sectionIndex);
};

export const deriveLayoutTypeFromSlide = (slide: Slide): string | undefined => {
  if (slide.section) {
    return getSectionLabel(slide.section, slide.sectionIndex, slide.sectionLabel);
  }
  if (slide.sectionLabel && slide.sectionLabel.trim()) {
    return normalizeText(slide.sectionLabel);
  }
  return undefined;
};
