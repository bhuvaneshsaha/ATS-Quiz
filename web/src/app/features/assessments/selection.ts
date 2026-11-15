export function toggleId(ids: readonly string[], id: string, checked: boolean): string[] {
  if (checked) {
    return ids.includes(id) ? [...ids] : [...ids, id];
  }
  return ids.filter((item) => item !== id);
}

export function moveItem<T>(items: readonly T[], index: number, delta: number): T[] {
  const next = index + delta;
  if (next < 0 || next >= items.length) {
    return [...items];
  }
  const copy = [...items];
  const [item] = copy.splice(index, 1);
  copy.splice(next, 0, item);
  return copy;
}
