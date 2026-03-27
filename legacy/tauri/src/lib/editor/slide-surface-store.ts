import { useShallow } from 'zustand/react/shallow';
import { useEditorStore } from '@/lib/stores';
import type {
  Background,
  BuildStep,
  Layer,
  LayerTransform,
  MediaEntry,
  Presentation,
  ShapeType,
  Slide,
  SlideMediaCue,
  SlideTransition,
  SongSection,
  TextLayer,
  WebLayer,
  ShapeLayer,
  MediaLayer,
} from '@/lib/models';

export interface SlideSurfaceSelection {
  slideIds: string[];
  layerIds: string[];
}

export interface SlideCanvasStore {
  addTextLayer: (slideId: string, content?: string) => TextLayer | null;
  updateLayer: (slideId: string, layerId: string, updates: Partial<Layer>) => void;
  updateLayers: (
    slideId: string,
    updates: { layerId: string; updates: Partial<Layer> }[]
  ) => void;
  deleteLayer: (slideId: string, layerId: string) => void;
  selection: SlideSurfaceSelection;
  selectLayer: (layerId: string | null) => void;
  selectLayers: (layerIds: string[]) => void;
  beginLayerTransform: () => void;
  updateLayerTransform: (
    slideId: string,
    layerId: string,
    transform: Partial<LayerTransform>
  ) => void;
  updateLayerTransforms: (
    slideId: string,
    updates: { layerId: string; transform: Partial<LayerTransform> }[]
  ) => void;
  commitLayerTransform: () => void;
  bringLayerForward: (slideId: string, layerId: string) => void;
  sendLayerBackward: (slideId: string, layerId: string) => void;
  bringLayerToFront: (slideId: string, layerId: string) => void;
  sendLayerToBack: (slideId: string, layerId: string) => void;
  alignSelection?: (
    slideId: string,
    alignment: 'left' | 'center' | 'right' | 'top' | 'middle' | 'bottom'
  ) => void;
  distributeSelection?: (slideId: string, axis: 'horizontal' | 'vertical') => void;
  addShapeLayer?: (slideId: string, shapeType?: ShapeType) => ShapeLayer | null;
  addMediaLayer?: (slideId: string, mediaId: string, mediaType: MediaLayer['mediaType']) => MediaLayer | null;
  addWebLayer?: (slideId: string, url: string) => WebLayer | null;
}

export interface SlideInspectorStore {
  updateSlides: (slideIds: string[], updates: Partial<Slide>) => void;
  setSlidesBackground: (slideIds: string[], background: Background) => void;
  setSlidesSection: (slideIds: string[], section?: SongSection) => void;
  setSlidesTransition: (slideIds: string[], transition: SlideTransition) => void;
  updateLayer: (slideId: string, layerId: string, updates: Partial<Layer>) => void;
  deleteLayer: (slideId: string, layerId: string) => void;
  bringLayerForward: (slideId: string, layerId: string) => void;
  sendLayerBackward: (slideId: string, layerId: string) => void;
  bringLayerToFront: (slideId: string, layerId: string) => void;
  sendLayerToBack: (slideId: string, layerId: string) => void;
  selectLayer: (layerId: string | null) => void;
  selectLayers: (layerIds: string[]) => void;
  selection: SlideSurfaceSelection;
  presentation: Presentation | null;
  filePath: string | null;
  pendingMedia: Map<string, string>;
  reorderLayer: (slideId: string, layerId: string, toIndex: number) => void;
  addBuildStep: (slideId: string, step: Omit<BuildStep, 'id'>) => void;
  setPresentationResolution?: (size: { width: number; height: number }) => void;
}

export interface AssetToolbarStore {
  presentation: Presentation | null;
  addTextLayer: (slideId: string, content?: string) => void;
  addShapeLayer: (slideId: string, shapeType?: ShapeType) => void;
  addWebLayer: (slideId: string, url: string) => void;
  addMedia?: (entry: MediaEntry, sourcePath: string) => void;
  updateSlide: (slideId: string, updates: { mediaCues?: SlideMediaCue[] }) => void;
}

export interface LayerContextMenuStore {
  updateLayer: (slideId: string, layerId: string, updates: Partial<Layer>) => void;
  deleteLayer: (slideId: string, layerId: string) => void;
  bringLayerForward: (slideId: string, layerId: string) => void;
  sendLayerBackward: (slideId: string, layerId: string) => void;
  bringLayerToFront: (slideId: string, layerId: string) => void;
  sendLayerToBack: (slideId: string, layerId: string) => void;
}

export const useEditorSlideCanvasStore = (): SlideCanvasStore =>
  useEditorStore(
    useShallow((state) => ({
      addTextLayer: state.addTextLayer,
      updateLayer: state.updateLayer,
      updateLayers: state.updateLayers,
      deleteLayer: state.deleteLayer,
      selection: state.selection,
      selectLayer: state.selectLayer,
      selectLayers: state.selectLayers,
      beginLayerTransform: state.beginLayerTransform,
      updateLayerTransform: state.updateLayerTransform,
      updateLayerTransforms: state.updateLayerTransforms,
      commitLayerTransform: state.commitLayerTransform,
      bringLayerForward: state.bringLayerForward,
      sendLayerBackward: state.sendLayerBackward,
      bringLayerToFront: state.bringLayerToFront,
      sendLayerToBack: state.sendLayerToBack,
      alignSelection: state.alignSelection,
      distributeSelection: state.distributeSelection,
      addShapeLayer: state.addShapeLayer,
      addMediaLayer: state.addMediaLayer,
      addWebLayer: state.addWebLayer,
    }))
  );

export const useEditorSlideInspectorStore = (): SlideInspectorStore =>
  useEditorStore(
    useShallow((state) => ({
      updateSlides: state.updateSlides,
      setSlidesBackground: state.setSlidesBackground,
      setSlidesSection: state.setSlidesSection,
      setSlidesTransition: state.setSlidesTransition,
      updateLayer: state.updateLayer,
      deleteLayer: state.deleteLayer,
      bringLayerForward: state.bringLayerForward,
      sendLayerBackward: state.sendLayerBackward,
      bringLayerToFront: state.bringLayerToFront,
      sendLayerToBack: state.sendLayerToBack,
      selectLayer: state.selectLayer,
      selectLayers: state.selectLayers,
      selection: state.selection,
      presentation: state.presentation,
      filePath: state.filePath,
      pendingMedia: state.pendingMedia,
      reorderLayer: state.reorderLayer,
      addBuildStep: state.addBuildStep,
      setPresentationResolution: state.setPresentationResolution,
    }))
  );

export const useEditorAssetToolbarStore = (): AssetToolbarStore =>
  useEditorStore(
    useShallow((state) => ({
      presentation: state.presentation,
      addTextLayer: state.addTextLayer,
      addShapeLayer: state.addShapeLayer,
      addWebLayer: state.addWebLayer,
      addMedia: state.addMedia,
      updateSlide: state.updateSlide,
    }))
  );

export const useEditorLayerContextMenuStore = (): LayerContextMenuStore =>
  useEditorStore(
    useShallow((state) => ({
      updateLayer: state.updateLayer,
      deleteLayer: state.deleteLayer,
      bringLayerForward: state.bringLayerForward,
      sendLayerBackward: state.sendLayerBackward,
      bringLayerToFront: state.bringLayerToFront,
      sendLayerToBack: state.sendLayerToBack,
    }))
  );
