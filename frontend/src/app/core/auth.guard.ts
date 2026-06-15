import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

// Page-key based guard. Each guarded route carries `data: { pageKey: '<key>' }`.
// - Not logged in            -> redirect to /login
// - Logged in but no access  -> redirect to the first allowed page (or /login if none)
export const authGuard: CanActivateFn = (route) => {
  const auth = inject(AuthService);
  const router = inject(Router);

  if (!auth.isLoggedIn()) {
    return router.createUrlTree(['/login']);
  }

  const pageKey = route.data['pageKey'] as string | undefined;
  if (pageKey && !auth.hasPage(pageKey)) {
    const first = auth.firstAllowedPage();
    return router.createUrlTree([first ? `/${first}` : '/login']);
  }

  return true;
};
