export function formatTimestamp(value: string): string {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'Unknown timestamp';
  return `${date.toISOString().slice(0, 16).replace('T', ' ')} UTC`;
}
