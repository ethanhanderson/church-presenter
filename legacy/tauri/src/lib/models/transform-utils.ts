export const coerceRotation = (value: number) => {
  if (!Number.isFinite(value)) return 0;
  return Object.is(value, -0) ? 0 : value;
};
