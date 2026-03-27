export type ResizeDirection =
  | 'n'
  | 'ne'
  | 'e'
  | 'se'
  | 's'
  | 'sw'
  | 'w'
  | 'nw';

export type CursorVariant =
  | 'default'
  | 'pointer'
  | 'text'
  | 'not-allowed'
  | 'grab'
  | 'grabbing'
  | 'move'
  | 'ew-resize'
  | `${ResizeDirection}-resize`
  | `rotate-${ResizeDirection}`;

export const isRotateVariant = (
  variant: CursorVariant
): variant is `rotate-${ResizeDirection}` =>
  variant.startsWith('rotate-');
