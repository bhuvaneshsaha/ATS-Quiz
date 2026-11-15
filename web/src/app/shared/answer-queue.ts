/** Delay before the next answer save, capped so a long outage does not wait forever. */
export function backoffDelay(attempt: number): number {
  return Math.min(8_000, 250 * 2 ** Math.max(0, attempt - 1));
}

/** A save is applied only when its sequence is newer than the one already stored. */
export function newerSequenceWins(stored: number, incoming: number): boolean {
  return incoming > stored;
}

export function shouldRetrySave(status: number): boolean {
  return status === 0 || status >= 500;
}
