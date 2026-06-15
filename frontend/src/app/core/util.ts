// Small shared helpers.

// Minutes -> "Hh Mm" (e.g. 485 -> "8h 05m").
export function fmtMinutes(min: number | null | undefined): string {
  if (min == null || min <= 0) return '0h 00m';
  const h = Math.floor(min / 60);
  const m = min % 60;
  return `${h}h ${m.toString().padStart(2, '0')}m`;
}

// ISO timestamp -> "HH:mm" local time, or "-" if empty.
export function fmtTime(iso: string | null | undefined): string {
  if (!iso) return '-';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '-';
  return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: false });
}

// Today's date as "yyyy-MM-dd" in local time.
export function todayIso(): string {
  const d = new Date();
  const y = d.getFullYear();
  const m = (d.getMonth() + 1).toString().padStart(2, '0');
  const day = d.getDate().toString().padStart(2, '0');
  return `${y}-${m}-${day}`;
}

// Current month as "yyyy-MM".
export function currentMonth(): string {
  const d = new Date();
  return `${d.getFullYear()}-${(d.getMonth() + 1).toString().padStart(2, '0')}`;
}
