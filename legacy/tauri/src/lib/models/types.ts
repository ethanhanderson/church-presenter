/**
 * Church Presenter - Core Type Definitions
 * .cpres file format v1.0.0
 */

// ============================================================================
// Media Types
// ============================================================================

export type MediaType = 'image' | 'video' | 'audio';

export interface MediaEntry {
  id: string;
  filename: string; // Original filename
  path: string; // Path within the bundle (e.g., "media/abc123.jpg")
  mime: string;
  sha256: string;
  byteSize: number;
  type: MediaType;
  width?: number; // For images/videos
  height?: number;
  duration?: number; // For videos/audio in seconds
}

export type FontStyleType = 'normal' | 'italic';

export interface FontEntry {
  id: string;
  family: string;
  fullName: string;
  postscriptName?: string | null;
  path: string; // Path within the bundle (e.g., "fonts/abc123.ttf")
  mime: string;
  sha256: string;
  byteSize: number;
  weight: number;
  style: FontStyleType;
}

// ============================================================================
// Theme Types
// ============================================================================

export interface FontStyle {
  family: string;
  size: number; // in pixels
  weight: number; // 100-900
  italic: boolean;
  lineHeight?: number; // multiplier
  letterSpacing: number; // in pixels
}

export interface TextShadow {
  enabled: boolean;
  color: string;
  offsetX: number;
  offsetY: number;
  blur: number;
}

export interface TextOutline {
  enabled: boolean;
  color: string;
  width: number;
}

export interface TextStyle {
  font: FontStyle;
  color: string;
  alignment: 'left' | 'center' | 'right';
  verticalAlignment: 'top' | 'middle' | 'bottom';
  shadow: TextShadow;
  outline: TextOutline;
  effectsOrder?: Array<'shadow' | 'outline'>;
}

export type BackgroundType = 'solid' | 'gradient' | 'image' | 'video' | 'transparent';

export interface SolidBackground {
  type: 'solid';
  color: string;
}

export interface GradientStop {
  color: string;
  position: number; // 0-100
}

export interface GradientBackground {
  type: 'gradient';
  angle: number; // degrees
  stops: GradientStop[];
}

export interface ImageBackground {
  type: 'image';
  mediaId: string;
  fit: 'cover' | 'contain' | 'fill' | 'none';
  position: { x: number; y: number }; // percentage 0-100
  opacity: number; // 0-1
}

export interface VideoBackground {
  type: 'video';
  mediaId: string;
  fit: 'cover' | 'contain' | 'fill';
  loop: boolean;
  muted: boolean;
  opacity: number;
}

export interface TransparentBackground {
  type: 'transparent';
}

export type Background =
  | SolidBackground
  | GradientBackground
  | ImageBackground
  | VideoBackground
  | TransparentBackground;

export interface Theme {
  id: string;
  name: string;
  createdAt: string; // ISO 8601
  updatedAt: string;

  // Default background for slides using this theme
  background: Background;

  // Text styles
  primaryText: TextStyle;
  secondaryText: TextStyle;

  // Layout
  padding: {
    top: number;
    right: number;
    bottom: number;
    left: number;
  };

  // Aspect ratio preference
  aspectRatio: '16:9' | '4:3' | '16:10';
}

export interface ThemeTemplateTextLayer {
  id: string;
  content?: string;
  transform: LayerTransform;
  style: TextStyle;
  fills: LayerFill[];
  strokes?: LayerStroke[];
  textFit?: TextFitMode;
  padding?: number;
  name?: string;
}

export interface ThemeTemplateSlide {
  id: string;
  name?: string;
  layoutType?: string;
  background: Background;
  layers?: Layer[];
  mediaCues?: SlideMediaCue[];
  textLayers?: ThemeTemplateTextLayer[];
}

export interface ThemeTemplate {
  id: string;
  name: string;
  createdAt: string; // ISO 8601
  updatedAt: string;
  aspectRatio: '16:9' | '4:3' | '16:10';
  baseSize: { width: number; height: number };
  slides: ThemeTemplateSlide[];
}

// ============================================================================
// Slide Types
// ============================================================================

export type SlideType = 'song' | 'sermon' | 'scripture' | 'announcement' | 'blank' | 'media';

export type SongSection =
  | 'title'
  | 'verse'
  | 'chorus'
  | 'bridge'
  | 'pre-chorus'
  | 'tag'
  | 'refrain'
  | 'intro'
  | 'outro'
  | 'vamp'
  | 'interlude'
  | 'ending'
  | 'custom';

// ============================================================================
// Layer Types (replacing TextBlock)
// ============================================================================

export type LayerType = 'text' | 'shape' | 'media' | 'web';

export type BlendMode =
  | 'normal'
  | 'multiply'
  | 'screen'
  | 'overlay'
  | 'darken'
  | 'lighten'
  | 'color-dodge'
  | 'color-burn'
  | 'hard-light'
  | 'soft-light'
  | 'difference'
  | 'exclusion';

export type LayerEffectType = 'drop-shadow' | 'layer-blur';

export interface BaseLayerEffect {
  id: string;
  type: LayerEffectType;
  enabled?: boolean;
}

export interface DropShadowEffect extends BaseLayerEffect {
  type: 'drop-shadow';
  color: string;
  opacity: number; // 0-1
  offsetX: number; // px
  offsetY: number; // px
  blur: number; // px
  spread?: number; // px
}

export interface LayerBlurEffect extends BaseLayerEffect {
  type: 'layer-blur';
  radius: number; // px
}

export type LayerEffect = DropShadowEffect | LayerBlurEffect;

export interface LayerTransform {
  x: number; // slide pixels (0-base width)
  y: number; // slide pixels (0-base height)
  width: number; // slide pixels
  height: number; // slide pixels
  rotation: number; // degrees
  opacity: number; // 0-1
  cornerRadius?: number; // px
  cornerRadiusTopLeft?: number; // px
  cornerRadiusTopRight?: number; // px
  cornerRadiusBottomRight?: number; // px
  cornerRadiusBottomLeft?: number; // px
  flipX?: boolean; // horizontal mirror
  flipY?: boolean; // vertical mirror
  lockAspectRatio?: boolean; // keep width/height proportional on resize
  clipContent?: boolean; // clip contents to layer bounds
}

export interface LayerFill {
  id: string;
  color: string;
  opacity: number; // 0-1
  enabled?: boolean;
}

export type StrokePosition = 'inside' | 'center' | 'outside';
export type StrokeSides = 'all' | 'top' | 'bottom' | 'left' | 'right' | 'custom';

export interface LayerStroke {
  id: string;
  color: string;
  opacity: number; // 0-1
  width: number; // px
  position?: StrokePosition;
  sides?: StrokeSides;
  customSides?: {
    top: boolean;
    right: boolean;
    bottom: boolean;
    left: boolean;
  };
  enabled?: boolean;
}

export interface BaseLayer {
  id: string;
  name: string;
  locked: boolean;
  visible: boolean;
  blendMode?: BlendMode;
  transform: LayerTransform;
  fills?: LayerFill[];
  strokes?: LayerStroke[];
  effects?: LayerEffect[];
}

// Text fitting modes for how text scales within its container
export type TextFitMode =
  | 'auto'    // Natural size, no scaling (default)
  | 'shrink'  // Shrink to fit if text overflows, but never scale up
  | 'fill';   // Scale text to fill the container

// Text display presets for common use cases
export type TextDisplayMode =
  | 'lyrics'    // Large centered text for music slides
  | 'paragraph' // Left-aligned wrapped text for sermon notes
  | 'custom';   // User-defined settings

export interface TextLayer extends BaseLayer {
  type: 'text';
  content: string;
  style?: Partial<TextStyle>; // Override theme styles
  textFit?: TextFitMode;      // How text scales within container
  textMode?: TextDisplayMode; // Display preset
  padding?: number;           // Inner padding as percentage (0-20)
}

export type ShapeType = 'rectangle' | 'ellipse' | 'line' | 'triangle';

export interface ShapeStyle {
  fill: string;
  fillOpacity: number;
  stroke: string;
  strokeWidth: number;
  strokeOpacity: number;
  cornerRadius: number; // for rectangles
}

export interface ShapeLayer extends BaseLayer {
  type: 'shape';
  shapeType: ShapeType;
  style: ShapeStyle;
}

export interface MediaLayer extends BaseLayer {
  type: 'media';
  mediaId: string;
  mediaType: 'image' | 'video';
  fit: 'cover' | 'contain' | 'fill' | 'none';
  // Video-specific
  loop?: boolean;
  muted?: boolean;
  autoplay?: boolean;
}

export interface WebLayer extends BaseLayer {
  type: 'web';
  url: string;
  zoom: number; // scale factor, 1 = 100%
  interactive: boolean; // whether to allow pointer events
  refreshInterval?: number; // auto-refresh in seconds, 0 = disabled
}

export type VectorFillRule = 'nonzero' | 'evenodd';

export interface VectorLayer extends BaseLayer {
  type: 'vector';
  path: string; // SVG path data
  viewBox?: string; // e.g. "0 0 100 100"
  fillRule?: VectorFillRule;
}

export type Layer = TextLayer | ShapeLayer | MediaLayer | WebLayer | VectorLayer;

// Legacy type alias for backward compatibility during migration
export type TextBlock = TextLayer;

export interface SlideOverrides {
  background?: Background;
  primaryText?: Partial<TextStyle>;
  secondaryText?: Partial<TextStyle>;
  padding?: Partial<Theme['padding']>;
}

// New media cue targets for the simplified layer model
export type MediaCueTarget = 'mediaUnderlay' | 'mediaOverlay' | 'audio';

// Legacy media cue targets for backward compatibility with older .cpres files
export type LegacyMediaCueTarget = 'slideBackgroundMedia' | 'slideForegroundMedia' | 'audio';

// Maps legacy targets to new targets
export const LEGACY_CUE_TARGET_MAP: Record<LegacyMediaCueTarget, MediaCueTarget> = {
  slideBackgroundMedia: 'mediaUnderlay',
  slideForegroundMedia: 'mediaOverlay',
  audio: 'audio',
};

export interface SlideMediaCue {
  id: string;
  mediaId: string;
  mediaType: MediaType;
  target: MediaCueTarget;
  fit?: 'cover' | 'contain' | 'fill' | 'none';
  loop?: boolean;
  muted?: boolean;
  autoplay?: boolean;
}

// ============================================================================
// Slide Transition & Animation Types
// ============================================================================

export type SlideTransitionType =
  | 'none'
  | 'fade'
  | 'slide-left'
  | 'slide-right'
  | 'slide-up'
  | 'slide-down'
  | 'zoom-in'
  | 'zoom-out'
  | 'flip';

export interface SlideTransition {
  type: SlideTransitionType;
  duration: number; // ms
  easing?: string; // CSS easing function
}

export type AnimationPreset =
  | 'fade-in'
  | 'fade-out'
  | 'slide-in-left'
  | 'slide-in-right'
  | 'slide-in-up'
  | 'slide-in-down'
  | 'scale-in'
  | 'scale-out'
  | 'bounce-in'
  | 'spin-in';

export type BuildTrigger = 'onEnter' | 'onAdvance' | 'onExit' | 'withPrevious' | 'afterPrevious';

export interface BuildStep {
  id: string;
  layerId: string;
  preset: AnimationPreset;
  trigger: BuildTrigger;
  delay: number; // ms
  duration: number; // ms
}

export interface SlideAnimations {
  transition: SlideTransition;
  buildIn: BuildStep[];
  buildOut: BuildStep[];
}

export interface Slide {
  id: string;
  type: SlideType;
  layoutType?: string;

  // For song slides
  section?: SongSection;
  sectionLabel?: string; // e.g., "Verse 1", "Chorus", custom label
  sectionIndex?: number; // For ordering within section type

  // Content - layers replace the old blocks array
  layers: Layer[];
  mediaCues?: SlideMediaCue[];
  notes?: string; // Speaker/presenter notes

  // Slide background (theme-applied, no overrides)
  background?: Background;

  // Per-slide overrides (optional)
  overrides?: SlideOverrides;

  // Animations and transitions
  animations?: SlideAnimations;

  // Metadata
  createdAt: string;
  updatedAt: string;
}

// ============================================================================
// Arrangement Types
// ============================================================================

export interface SectionGroup {
  section: SongSection;
  label: string;
  slideIds: string[];
}

export interface Arrangement {
  // Full ordered list of slide IDs for presentation flow
  order: string[];

  // Section groupings for the editor view
  sections: SectionGroup[];
}

// ============================================================================
// Presentation Types
// ============================================================================

// ============================================================================
// External Link Metadata (for Music Manager sync)
// ============================================================================

export type ExternalSyncStatus = 'linked' | 'synced' | 'pending' | 'conflict' | 'error';

export interface ExternalSongLink {
  songId: string; // Music Manager song UUID
  groupId: string; // Music Manager group UUID
  syncedAt?: string; // ISO 8601 - last sync time
  remoteVersion?: number; // For conflict detection
}

export interface ExternalSetLink {
  setId: string; // Music Manager set UUID
  groupId: string; // Music Manager group UUID
  syncedAt?: string; // ISO 8601 - last sync time
  serviceDate?: string; // ISO 8601 - service date for set
  remoteVersion?: number; // For conflict detection
}

export interface SyncMetadata {
  status: ExternalSyncStatus;
  lastSyncAttempt?: string; // ISO 8601
  conflictUrl?: string; // URL to resolve conflicts in manager app
  error?: string; // Error message if status is 'error'
}

// ============================================================================
// Presentation Types
// ============================================================================

export interface PresentationManifest {
  formatVersion: string; // semver, e.g., "1.0.0"
  presentationId: string;
  title: string;
  author?: string;
  createdAt: string;
  updatedAt: string;
  aspectRatio?: '16:9' | '4:3' | '16:10';
  slideSize?: { width: number; height: number }; // presentation resolution in pixels

  // Theme reference
  themeId?: string; // Reference to embedded theme

  // Media manifest
  media: MediaEntry[];

  // Font manifest
  fonts: FontEntry[];

  // External link to Music Manager song (optional)
  externalSong?: ExternalSongLink;

  // Sync metadata (optional)
  sync?: SyncMetadata;
}

export interface Presentation {
  manifest: PresentationManifest;
  slides: Slide[];
  arrangement: Arrangement;
  themes: Theme[]; // Embedded themes (usually just one)
}

// ============================================================================
// Library & Playlist Types
// ============================================================================

export interface PresentationRef {
  path: string; // File path to .cpres file
  title: string; // Cached from manifest
  updatedAt: string; // Cached from manifest
  thumbnailData?: string; // Base64 encoded thumbnail (optional)
}

export interface Library {
  id: string;
  name: string;
  description?: string;
  createdAt: string;
  updatedAt: string;
  presentations: PresentationRef[];
  defaultFolder?: string; // Default save location
}

export interface Playlist {
  id: string;
  name: string;
  description?: string;
  createdAt: string;
  updatedAt: string;
  items: PresentationRef[];

  // External link to Music Manager set (optional)
  externalSet?: ExternalSetLink;

  // Sync metadata (optional)
  sync?: SyncMetadata;
}

// ============================================================================
// App Settings Types
// ============================================================================

export interface OutputSettings {
  monitorIds: string[];
  audienceEnabled: boolean;
  scaling: 'fit' | 'fill';
  aspectRatio: '16:9' | '4:3' | '16:10';
  clearGroups: OutputClearGroup[];
}

export interface EditorSettings {
  autosaveInterval: number; // seconds, 0 = disabled
  autoSaveEnabled: boolean; // master toggle for auto-save
  autoSaveOnCreate: boolean; // auto-save when creating new presentations
  showGrid: boolean;
  snapToGrid: boolean;
  gridSize: number;
}

export interface ShowSettings {
  defaultCenterView: 'slides' | 'playlist' | 'library';
  thumbnailSize: number; // pixels
  showSlideLabels: boolean;
  autoTakeOnDoubleClick: boolean;
}

export interface ReflowSettings {
  textSize: number; // pixels
  previewDensity: 'comfortable' | 'compact';
  showSlideLabels: boolean;
}

export interface IntegrationsSettings {
  musicManager: {
    defaultSongAction: 'import' | 'link';
    preferSetImportView: boolean;
  };
}

export interface AppSettings {
  output: OutputSettings;
  editor: EditorSettings;
  show: ShowSettings;
  reflow: ReflowSettings;
  integrations: IntegrationsSettings;
  theme: 'light' | 'dark' | 'system';
  recentFiles: PresentationRef[];
  maxRecentFiles: number;
  contentDir?: string | null;
  mediaLibraryDir?: string | null;
  updates: {
    autoCheck: boolean;
    lastCheckedAt?: string | null;
  };
}

// ============================================================================
// Catalog (stored in appDataDir)
// ============================================================================

export interface Catalog {
  libraries: Library[];
  playlists: Playlist[];
}

// ============================================================================
// Live State Types
// ============================================================================

export interface LiveState {
  isLive: boolean;
  presentationId: string | null;
  presentationPath: string | null;
  currentSlideId: string | null;
  currentSlideIndex: number;
  isBlackout: boolean;
  isClear: boolean; // Show logo/clear screen
}

// ============================================================================
// Output Layer Types
// ============================================================================

// Legacy output layer IDs (for backward compatibility)
export type LegacyOutputLayerId =
  | 'background'
  | 'slideBackgroundMedia'
  | 'slideForegroundMedia'
  | 'audio';

// New internal media layer IDs for the compositor
// These are the actual layers the compositor uses internally
export type MediaLayerId = 'mediaUnderlay' | 'mediaOverlay' | 'audio';

// Operator control groups - the two clearable groups exposed to the user
export type OutputControlGroup = 'presentation' | 'media';

export interface OutputLayerMedia {
  mediaId: string;
  mediaType: MediaType;
  fit?: 'cover' | 'contain' | 'fill' | 'none';
  loop?: boolean;
  muted?: boolean;
  autoplay?: boolean;
}

// Internal media layers state (what the compositor actually renders)
export type MediaLayersState = Record<MediaLayerId, OutputLayerMedia | null>;

// Suppress state for operator controls (presentation = background+elements, media = underlay+overlay+audio)
export type SuppressState = Record<OutputControlGroup, boolean>;

// Legacy output layers state (for backward compatibility during migration)
export type LegacyOutputLayersState = Record<LegacyOutputLayerId, OutputLayerMedia | null>;

// Alias for backward compatibility
export type OutputLayerId = LegacyOutputLayerId;
export type OutputLayersState = LegacyOutputLayersState;

export interface OutputClearGroup {
  id: string;
  name: string;
  layers: OutputControlGroup[];
}

// ============================================================================
// Editor State Types
// ============================================================================

export interface EditorSelection {
  slideIds: string[];
  layerIds: string[];
}

export interface UndoableAction {
  id: string;
  type: string;
  timestamp: number;
  undo: () => void;
  redo: () => void;
}

export interface EditorState {
  presentation: Presentation | null;
  filePath: string | null;
  isDirty: boolean;
  selection: EditorSelection;
  activeSlideId: string | null;
  undoStack: UndoableAction[];
  redoStack: UndoableAction[];
}
