const counterFormatter = new Intl.NumberFormat();

export function formatCounter(value: number | null | undefined): string {
  return value === null || value === undefined || !Number.isFinite(value)
    ? '-'
    : counterFormatter.format(value);
}
