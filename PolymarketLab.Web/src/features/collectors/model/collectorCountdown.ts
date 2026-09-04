export function formatCollectorCountdown(deadline: string | null, nowMs: number): string {
  if (deadline === null) {
    return '-';
  }

  const deadlineMs = Date.parse(deadline);
  if (Number.isNaN(deadlineMs) || !Number.isFinite(nowMs)) {
    return '-';
  }

  const remainingSeconds = Math.max(0, Math.ceil((deadlineMs - nowMs) / 1_000));
  const seconds = remainingSeconds % 60;
  const totalMinutes = Math.floor(remainingSeconds / 60);
  const minutes = totalMinutes % 60;
  const hours = Math.floor(totalMinutes / 60);
  const minuteSeconds = `${pad(minutes)}:${pad(seconds)}`;

  return hours > 0 ? `${pad(hours)}:${minuteSeconds}` : minuteSeconds;
}

function pad(value: number): string {
  return value.toString().padStart(2, '0');
}
