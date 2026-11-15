import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { SessionStore } from './session';

export const staffGuard: CanActivateFn = async () => {
  const session = inject(SessionStore);
  const router = inject(Router);
  if (!session.ready()) {
    await session.refresh();
  }

  return session.signedIn() ? true : router.createUrlTree(['/login']);
};
