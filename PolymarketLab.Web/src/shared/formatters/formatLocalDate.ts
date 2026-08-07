const localDateTimeFormatter = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'medium',
});

export function formatLocalDate(value: string | null): string {
  if (value === null) {
    return '-';
  }

  const date = new Date(value);

  return Number.isNaN(date.getTime()) ? '-' : localDateTimeFormatter.format(date);
}
