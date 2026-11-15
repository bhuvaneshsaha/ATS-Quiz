import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { firstAllowed } from '../features/shell/home';
import { SessionStore } from './session';

export function permissionGuard(permission: string): CanActivateFn {
  return async () => {
    const session = inject(SessionStore);
    const router = inject(Router);
    if (!session.ready()) {
      await session.refresh();
    }
    if (!session.signedIn()) {
      return router.createUrlTree(['/login']);
    }
    if (session.can(permission)) {
      return true;
    }
    return router.createUrlTree([firstAllowed((item) => session.can(item)) ?? '/']);
  };
}
