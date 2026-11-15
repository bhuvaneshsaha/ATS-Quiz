import { backoffDelay, newerSequenceWins, shouldRetrySave } from './answer-queue';

describe('answer save queue', () => {
  it('keeps the newer sequence', () => {
    expect(newerSequenceWins(2, 1)).toBe(false);
    expect(newerSequenceWins(1, 2)).toBe(true);
  });

  it('backs off and then caps the wait', () => {
    expect(backoffDelay(1)).toBe(250);
    expect(backoffDelay(3)).toBe(1000);
    expect(backoffDelay(12)).toBe(8000);
  });

  it('retries only when the network or the server failed', () => {
    expect(shouldRetrySave(0)).toBe(true);
    expect(shouldRetrySave(503)).toBe(true);
    expect(shouldRetrySave(400)).toBe(false);
  });
});
