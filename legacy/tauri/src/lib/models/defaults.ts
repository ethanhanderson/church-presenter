/**
 * Default values and factory functions for creating new entities
 */

import { v4 as uuid } from 'uuid';
import type {
  Theme,
  Slide,
  Presentation,
  Library,
  Playlist,
  Arrangement,
  TextStyle,
  Background,
  AppSettings,
  Catalog,
  SongSection,
  SlideType,
  TextLayer,
  ShapeLayer,
  MediaLayer,
  WebLayer,
  LayerTransform,
  ShapeStyle,
  ShapeType,
  SlideTransition,
  LayerFill,
  LayerStroke,
  LayerEffect,
  DropShadowEffect,
  LayerBlurEffect,
  VectorLayer,
} from './types';
import { deriveLayoutTypeFromSlide } from './layoutTypes';

// ============================================================================
// Default Text Styles
// ============================================================================

export const defaultPrimaryTextStyle: TextStyle = {
  font: {
    family: 'Inter',
    size: 72,
    weight: 700,
    italic: false,
    lineHeight: 1.2,
    letterSpacing: 0,
  },
  color: '#FFFFFF',
  alignment: 'center',
  verticalAlignment: 'middle',
  shadow: {
    enabled: false,
    color: 'rgba(0, 0, 0, 0.8)',
    offsetX: 2,
    offsetY: 2,
    blur: 8,
  },
  outline: {
    enabled: false,
    color: '#000000',
    width: 2,
  },
};

export const defaultSecondaryTextStyle: TextStyle = {
  font: {
    family: 'Inter',
    size: 36,
    weight: 400,
    italic: false,
    lineHeight: 1.4,
    letterSpacing: 0,
  },
  color: '#E0E0E0',
  alignment: 'center',
  verticalAlignment: 'bottom',
  shadow: {
    enabled: false,
    color: 'rgba(0, 0, 0, 0.6)',
    offsetX: 1,
    offsetY: 1,
    blur: 4,
  },
  outline: {
    enabled: false,
    color: '#000000',
    width: 1,
  },
};

// ============================================================================
// Default Background
// ============================================================================

export const defaultBackground: Background = {
  type: 'solid',
  color: '#1a1a2e',
};

// ============================================================================
// Factory Functions
// ============================================================================

export function createTheme(name: string, partial?: Partial<Theme>): Theme {
  const now = new Date().toISOString();
  return {
    id: uuid(),
    name,
    createdAt: now,
    updatedAt: now,
    background: defaultBackground,
    primaryText: defaultPrimaryTextStyle,
    secondaryText: defaultSecondaryTextStyle,
    padding: {
      top: 80,
      right: 80,
      bottom: 80,
      left: 80,
    },
    aspectRatio: '16:9',
    ...partial,
  };
}

// ============================================================================
// Default Layer Transform
// ============================================================================

export const defaultLayerTransform: LayerTransform = {
  x: 96,
  y: 324,
  width: 1728,
  height: 432,
  rotation: 0,
  opacity: 1,
  cornerRadius: 0,
  flipX: false,
  flipY: false,
  lockAspectRatio: false,
  clipContent: false,
};

// ============================================================================
// Default Shape Style
// ============================================================================

export const defaultShapeStyle: ShapeStyle = {
  fill: '#3b82f6',
  fillOpacity: 1,
  stroke: '#1d4ed8',
  strokeWidth: 2,
  strokeOpacity: 1,
  cornerRadius: 0,
};

export function createLayerFill(color: string, opacity = 1): LayerFill {
  return {
    id: uuid(),
    color,
    opacity,
    enabled: true,
  };
}

export function createLayerStroke(
  color: string,
  width = 1,
  opacity = 1,
  position: LayerStroke['position'] = 'inside',
  sides: LayerStroke['sides'] = 'all',
  customSides?: LayerStroke['customSides']
): LayerStroke {
  return {
    id: uuid(),
    color,
    opacity,
    width,
    position,
    sides,
    customSides,
    enabled: true,
  };
}

export function createDropShadowEffect(
  color: string,
  opacity = 0.6,
  offsetX = 0,
  offsetY = 2,
  blur = 8,
  spread = 0
): DropShadowEffect {
  return {
    id: uuid(),
    type: 'drop-shadow',
    color,
    opacity,
    offsetX,
    offsetY,
    blur,
    spread,
    enabled: true,
  };
}

export function createLayerBlurEffect(radius = 8): LayerBlurEffect {
  return {
    id: uuid(),
    type: 'layer-blur',
    radius,
    enabled: true,
  };
}

export function createLayerEffect(effect: LayerEffect): LayerEffect {
  return {
    ...effect,
    id: effect.id ?? uuid(),
  };
}

// ============================================================================
// Default Slide Transition
// ============================================================================

export const defaultSlideTransition: SlideTransition = {
  type: 'fade',
  duration: 300,
  easing: 'ease-out',
};

// ============================================================================
// Layer Factory Functions
// ============================================================================

let layerCounter = 0;

function generateLayerName(type: string): string {
  layerCounter++;
  return `${type} ${layerCounter}`;
}

export function createTextLayer(
  content: string,
  partial?: Partial<Omit<TextLayer, 'type'>>
): TextLayer {
  const inferredStyle = partial?.style ?? defaultPrimaryTextStyle;
  const inferredColor = inferredStyle.color ?? defaultPrimaryTextStyle.color;
  return {
    id: uuid(),
    type: 'text',
    name: partial?.name || generateLayerName('Text'),
    locked: false,
    visible: true,
    transform: { ...defaultLayerTransform, ...partial?.transform },
    content,
    style: inferredStyle,
    fills: partial?.fills ?? [createLayerFill(inferredColor, 1)],
  };
}

export function createShapeLayer(
  shapeType: ShapeType = 'rectangle',
  partial?: Partial<Omit<ShapeLayer, 'type'>>
): ShapeLayer {
  const resolvedStyle = { ...defaultShapeStyle, ...partial?.style };
  return {
    id: uuid(),
    type: 'shape',
    name: partial?.name || generateLayerName('Shape'),
    locked: false,
    visible: true,
    transform: {
      ...defaultLayerTransform,
      width: 384,
      height: 216,
      x: 768,
      y: 432,
      ...partial?.transform,
    },
    shapeType,
    style: resolvedStyle,
    fills: partial?.fills ?? [createLayerFill(resolvedStyle.fill, resolvedStyle.fillOpacity)],
    strokes:
      partial?.strokes ??
      [createLayerStroke(resolvedStyle.stroke, resolvedStyle.strokeWidth, resolvedStyle.strokeOpacity)],
  };
}

export function createMediaLayer(
  mediaId: string,
  mediaType: 'image' | 'video',
  partial?: Partial<Omit<MediaLayer, 'type'>>
): MediaLayer {
  return {
    id: uuid(),
    type: 'media',
    name: partial?.name || generateLayerName(mediaType === 'image' ? 'Image' : 'Video'),
    locked: false,
    visible: true,
    transform: {
      ...defaultLayerTransform,
      x: 192,
      y: 108,
      width: 1536,
      height: 864,
      ...partial?.transform,
    },
    mediaId,
    mediaType,
    fit: partial?.fit || 'contain',
    loop: partial?.loop ?? (mediaType === 'video'),
    muted: partial?.muted ?? true,
    autoplay: partial?.autoplay ?? true,
  };
}

export function createWebLayer(
  url: string,
  partial?: Partial<Omit<WebLayer, 'type'>>
): WebLayer {
  return {
    id: uuid(),
    type: 'web',
    name: partial?.name || generateLayerName('Web'),
    locked: false,
    visible: true,
    transform: {
      ...defaultLayerTransform,
      x: 96,
      y: 54,
      width: 1728,
      height: 972,
      ...partial?.transform,
    },
    url,
    zoom: partial?.zoom ?? 1,
    interactive: partial?.interactive ?? false,
    refreshInterval: partial?.refreshInterval ?? 0,
  };
}

export function createVectorLayer(
  path: string,
  partial?: Partial<Omit<VectorLayer, 'type'>>
): VectorLayer {
  return {
    id: uuid(),
    type: 'vector',
    name: partial?.name || generateLayerName('Vector'),
    locked: false,
    visible: true,
    transform: {
      ...defaultLayerTransform,
      width: 512,
      height: 512,
      x: 704,
      y: 284,
      ...partial?.transform,
    },
    path,
    viewBox: partial?.viewBox ?? '0 0 100 100',
    fillRule: partial?.fillRule ?? 'nonzero',
    fills: partial?.fills ?? [createLayerFill('#ffffff', 1)],
    strokes: partial?.strokes ?? [createLayerStroke('#000000', 0, 1)],
    effects: partial?.effects,
  };
}

// Legacy function for backward compatibility
export function createTextBlock(
  content: string,
  partial?: { position?: Partial<LayerTransform>; style?: Partial<TextStyle> }
): TextLayer {
  return createTextLayer(content, {
    transform: partial?.position ? {
      ...defaultLayerTransform,
      ...partial.position,
    } : undefined,
    style: partial?.style,
  });
}

export function createSlide(
  type: SlideType,
  content: string = '',
  options?: {
    section?: SongSection;
    sectionLabel?: string;
    sectionIndex?: number;
  }
): Slide {
  const now = new Date().toISOString();
  const slide: Slide = {
    id: uuid(),
    type,
    section: options?.section,
    sectionLabel: options?.sectionLabel,
    sectionIndex: options?.sectionIndex,
    layers: content ? [createTextLayer(content)] : [],
    mediaCues: [],
    background: defaultBackground,
    animations: {
      transition: { ...defaultSlideTransition },
      buildIn: [],
      buildOut: [],
    },
    createdAt: now,
    updatedAt: now,
  };
  slide.layoutType = deriveLayoutTypeFromSlide(slide);
  return slide;
}

export function createArrangement(slides: Slide[]): Arrangement {
  // Group slides by section
  const sectionMap = new Map<SongSection | 'none', string[]>();
  
  for (const slide of slides) {
    const section = slide.section || 'none';
    if (!sectionMap.has(section)) {
      sectionMap.set(section, []);
    }
    sectionMap.get(section)!.push(slide.id);
  }
  
  const sections = Array.from(sectionMap.entries())
    .filter(([section]) => section !== 'none')
    .map(([section, slideIds]) => ({
      section: section as SongSection,
      label: formatSectionLabel(section as SongSection),
      slideIds,
    }));
  
  return {
    order: slides.map(s => s.id),
    sections,
  };
}

const getBaseSlideSize = (aspectRatio?: Theme['aspectRatio']) => {
  switch (aspectRatio) {
    case '4:3':
      return { width: 1440, height: 1080 };
    case '16:10':
      return { width: 1920, height: 1200 };
    case '16:9':
    default:
      return { width: 1920, height: 1080 };
  }
};

export function createPresentation(title: string, partial?: Partial<Presentation>): Presentation {
  const now = new Date().toISOString();
  const defaultTheme = createTheme('Default Theme');
  const blankSlide = createSlide('blank');
  
  return {
    manifest: {
      formatVersion: '1.0.0',
      presentationId: uuid(),
      title,
      createdAt: now,
      updatedAt: now,
      aspectRatio: defaultTheme.aspectRatio,
      slideSize: getBaseSlideSize(defaultTheme.aspectRatio),
      themeId: defaultTheme.id,
      media: [],
      fonts: [],
    },
    slides: [blankSlide],
    arrangement: createArrangement([blankSlide]),
    themes: [defaultTheme],
    ...partial,
  };
}

export function createLibrary(name: string): Library {
  const now = new Date().toISOString();
  return {
    id: uuid(),
    name,
    createdAt: now,
    updatedAt: now,
    presentations: [],
  };
}

export function createPlaylist(name: string): Playlist {
  const now = new Date().toISOString();
  return {
    id: uuid(),
    name,
    createdAt: now,
    updatedAt: now,
    items: [],
  };
}

/**
 * Create a linked playlist from a Music Manager set
 */
export interface LinkedSetData {
  setId: string;
  groupId: string;
  title?: string | null;
  serviceDate: string;
}

export function createLinkedPlaylist(setData: LinkedSetData): Playlist {
  const now = new Date().toISOString();
  const displayTitle = setData.title || new Date(setData.serviceDate).toLocaleDateString(undefined, {
    weekday: 'short',
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  });

  return {
    id: uuid(),
    name: displayTitle,
    description: `Linked to Music Manager set for ${new Date(setData.serviceDate).toLocaleDateString()}`,
    createdAt: now,
    updatedAt: now,
    items: [],
    externalSet: {
      setId: setData.setId,
      groupId: setData.groupId,
      syncedAt: now,
      serviceDate: setData.serviceDate,
    },
    sync: {
      status: 'linked',
      lastSyncAttempt: now,
    },
  };
}

/**
 * Create a presentation from an imported song
 */
export interface ImportedSong {
  id: string;
  title: string;
  groupId: string;
  artist?: string | null;
  lyrics?: string | null; // Extracted lyrics text (optional)
  arrangementSlides?: {
    label?: string | null;
    lines?: string[] | null;
  }[] | null;
}

export function createSongPresentation(song: ImportedSong): Presentation {
  const now = new Date().toISOString();
  const defaultTheme = createTheme('Default Theme');
  
  // Create slides from song
  const slides: Slide[] = [];
  
  // Title slide
  const titleSlide = createSlide('song', song.title, {
    section: 'title',
    sectionLabel: 'Title',
  });
  // Add artist as secondary text if present
  if (song.artist) {
    titleSlide.layers.push(createTextLayer(song.artist, {
      transform: { ...defaultLayerTransform, x: 96, y: 756, width: 1728, height: 216 },
    }));
  }
  slides.push(titleSlide);
  
  const arrangementSlides = song.arrangementSlides
    ? createSlidesFromArrangement(song.arrangementSlides)
    : [];
  if (arrangementSlides.length > 0) {
    slides.push(...arrangementSlides);
  } else if (song.lyrics) {
    const lyricSlides = parseLyricsToSlides(song.lyrics);
    slides.push(...lyricSlides);
  }
  
  return {
    manifest: {
      formatVersion: '1.0.0',
      presentationId: uuid(),
      title: song.title,
      author: song.artist || undefined,
      createdAt: now,
      updatedAt: now,
      aspectRatio: defaultTheme.aspectRatio,
      themeId: defaultTheme.id,
      media: [],
      fonts: [],
      externalSong: {
        songId: song.id,
        groupId: song.groupId,
        syncedAt: now,
      },
      sync: {
        status: 'linked',
        lastSyncAttempt: now,
      },
    },
    slides,
    arrangement: createArrangement(slides),
    themes: [defaultTheme],
  };
}

/**
 * Parse raw lyrics text into slides
 * Handles common formats: double newlines for sections, brackets for section labels
 */
function parseLyricsToSlides(lyrics: string): Slide[] {
  const slides: Slide[] = [];
  
  // Normalize line endings
  const normalizedLyrics = lyrics.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
  
  // Split by double newlines (sections) or section markers like [Verse 1]
  const sections = normalizedLyrics.split(/\n\s*\n/);
  
  for (const section of sections) {
    const trimmedSection = section.trim();
    if (!trimmedSection) continue;
    
    // Check for section header like [Verse 1] or (Chorus)
    const headerMatch = trimmedSection.match(/^[\[\(]([^\]\)]+)[\]\)]\s*\n?/i);
    let sectionLabel: string | undefined;
    let sectionType: SongSection = 'verse';
    let content = trimmedSection;
    
    if (headerMatch) {
      const parsed = parseSectionLabel(headerMatch[1]);
      sectionLabel = parsed.sectionLabel;
      sectionType = parsed.section;
      content = trimmedSection.slice(headerMatch[0].length).trim();
    }
    
    if (!content) continue;
    
    // Split content into chunks of ~4 lines for readability
    const lines = content.split('\n').filter(line => line.trim());
    const linesPerSlide = 4;
    
    for (let i = 0; i < lines.length; i += linesPerSlide) {
      const slideLines = lines.slice(i, i + linesPerSlide);
      const slideContent = slideLines.join('\n');
      
      const slide = createSlide('song', slideContent, {
        section: sectionType,
        sectionLabel: sectionLabel || formatSectionLabel(sectionType),
        sectionIndex: Math.floor(i / linesPerSlide),
      });
      
      slides.push(slide);
    }
  }
  
  return slides;
}

function createSlidesFromArrangement(
  arrangementSlides: NonNullable<ImportedSong['arrangementSlides']>
): Slide[] {
  const slides: Slide[] = [];
  const sectionCounts = new Map<SongSection, number>();

  for (const entry of arrangementSlides) {
    const label = typeof entry.label === 'string' ? entry.label.trim() : '';
    const lowerLabel = label.toLowerCase();

    const lines = Array.isArray(entry.lines)
      ? entry.lines.filter((line) => typeof line === 'string')
      : [];
    const content = lines.map((line) => line.trim()).filter(Boolean).join('\n').trim();
    if (!content) continue;

    let section: SongSection | undefined;
    let sectionLabel: string | undefined;
    let sectionIndex: number | undefined;

    if (label) {
      const parsed = parseSectionLabel(label);
      section = parsed.section;
      sectionLabel = parsed.sectionLabel;
      const count = sectionCounts.get(section) ?? 0;
      sectionCounts.set(section, count + 1);
      sectionIndex = count;
    }

    slides.push(
      createSlide('song', content, {
        section,
        sectionLabel,
        sectionIndex,
      })
    );
  }

  return slides;
}

function parseSectionLabel(rawLabel: string): { section: SongSection; sectionLabel: string } {
  const label = rawLabel.trim();
  const labelLower = label.toLowerCase();
  let section: SongSection = 'custom';

  if (labelLower.includes('title')) section = 'title';
  else if (labelLower.includes('chorus')) section = 'chorus';
  else if (labelLower.includes('verse')) section = 'verse';
  else if (labelLower.includes('bridge')) section = 'bridge';
  else if (labelLower.includes('pre-chorus') || labelLower.includes('prechorus')) section = 'pre-chorus';
  else if (labelLower.includes('outro')) section = 'outro';
  else if (labelLower.includes('intro')) section = 'intro';
  else if (labelLower.includes('tag')) section = 'tag';
  else if (labelLower.includes('interlude')) section = 'interlude';
  else if (labelLower.includes('ending')) section = 'ending';
  else if (labelLower.includes('vamp')) section = 'vamp';

  const sectionLabel = label
    .split(' ')
    .map((word) => (word ? word[0].toUpperCase() + word.slice(1) : ''))
    .join(' ')
    .trim();

  return {
    section,
    sectionLabel: sectionLabel || formatSectionLabel(section),
  };
}

// ============================================================================
// Default App State
// ============================================================================

export const defaultAppSettings: AppSettings = {
  output: {
    monitorIds: [],
    audienceEnabled: false,
    scaling: 'fit',
    aspectRatio: '16:9',
    clearGroups: [],
  },
  editor: {
    autosaveInterval: 30,
    autoSaveEnabled: true,
    autoSaveOnCreate: true,
    showGrid: false,
    snapToGrid: true,
    gridSize: 10,
  },
  show: {
    defaultCenterView: 'slides',
    thumbnailSize: 200,
    showSlideLabels: true,
    autoTakeOnDoubleClick: true,
  },
  reflow: {
    textSize: 13,
    previewDensity: 'comfortable',
    showSlideLabels: true,
  },
  integrations: {
    musicManager: {
      defaultSongAction: 'import',
      preferSetImportView: false,
    },
  },
  theme: 'system',
  recentFiles: [],
  maxRecentFiles: 10,
  contentDir: null,
  mediaLibraryDir: null,
  updates: {
    autoCheck: true,
    lastCheckedAt: null,
  },
};

export const defaultCatalog: Catalog = {
  libraries: [],
  playlists: [],
};

// ============================================================================
// Helpers
// ============================================================================

export function formatSectionLabel(section: SongSection, index?: number): string {
  const labels: Record<SongSection, string> = {
    title: 'Title',
    verse: 'Verse',
    chorus: 'Chorus',
    bridge: 'Bridge',
    'pre-chorus': 'Pre-Chorus',
    tag: 'Tag',
    refrain: 'Refrain',
    intro: 'Intro',
    outro: 'Outro',
    vamp: 'Vamp',
    interlude: 'Interlude',
    ending: 'Ending',
    custom: 'Custom',
  };
  
  const label = labels[section] || section;
  return index !== undefined ? `${label} ${index + 1}` : label;
}

export const SONG_SECTIONS: SongSection[] = [
  'title',
  'intro',
  'verse',
  'pre-chorus',
  'chorus',
  'bridge',
  'refrain',
  'tag',
  'vamp',
  'interlude',
  'outro',
  'ending',
  'custom',
];

export const SLIDE_TYPES: { value: SlideType; label: string }[] = [
  { value: 'song', label: 'Song' },
  { value: 'sermon', label: 'Sermon' },
  { value: 'scripture', label: 'Scripture' },
  { value: 'announcement', label: 'Announcement' },
  { value: 'media', label: 'Media' },
  { value: 'blank', label: 'Blank' },
];
