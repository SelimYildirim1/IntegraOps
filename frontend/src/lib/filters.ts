import { dateInput } from "@/components/ui";

// The API treats "to" as exclusive; a chosen end day covers that whole day.
export function apiFilters<T extends { to: string }>(filters: T): T {
  if (!dateInput.test(filters.to)) return filters;
  const next = new Date(filters.to + "T00:00:00Z"); next.setUTCDate(next.getUTCDate() + 1);
  return { ...filters, to: next.toISOString().slice(0, 10) };
}
