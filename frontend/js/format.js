// Formatters: rands, sheet numbers, dates, % changes, KPI values, month names and HTML escaping
export const randFormat = new Intl.NumberFormat("en-ZA", { style: "currency", currency: "ZAR", maximumFractionDigits: 0 });
export const numberFormat = new Intl.NumberFormat("en-ZA", { maximumFractionDigits: 0 });
const dateFormat = new Intl.DateTimeFormat("en-ZA", { day: "numeric", month: "short", year: "numeric" });

export function escapeHtml(value) {
  return String(value).replace(/[&<>"']/g, (c) => (
    { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]
  ));
}

export function formatSheet(value) {
  if (value === null) return "–";
  return value < 0 ? `(${numberFormat.format(-value)})` : numberFormat.format(value);
}

export function formatDate(iso) {
  return dateFormat.format(new Date(`${iso}T00:00:00`));
}

export function monthName(month) {
  return new Date(2000, month - 1, 1).toLocaleString("en-ZA", { month: "long" });
}

export function initials(name) {
  return name
    .split(" ")
    .filter(Boolean)
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
}

export function percentChange(value, base) {
  return base === 0 ? null : ((value - base) / Math.abs(base)) * 100;
}

export function changeClass(pct, kind) {
  const rounded = pct === null ? 0 : Math.round(pct * 10) / 10;
  if (rounded === 0) return "neutral";
  const up = rounded > 0;
  return (kind === "expense" ? !up : up) ? "good" : "bad";
}

export function pctText(pct) {
  if (pct === null) return "–";
  const rounded = Math.round(pct * 10) / 10;
  return `${rounded > 0 ? "+" : ""}${rounded.toFixed(1)}%`;
}

export function yearsText(years) {
  return years.length > 1 ? `${years[0]}–${years[years.length - 1]}` : years[0];
}

export function formatKpiValue(value, unit) {
  return unit === "percent" ? `${value.toFixed(1)}%` : randFormat.format(value);
}

export function formatGap(gap, unit) {
  const sign = gap > 0 ? "+" : gap < 0 ? "−" : "";
  return unit === "percent" ? `${sign}${Math.abs(gap).toFixed(1)} pts` : `${sign}${randFormat.format(Math.abs(gap))}`;
}
