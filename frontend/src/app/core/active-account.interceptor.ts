import { HttpInterceptorFn, HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AlertController } from '@ionic/angular/standalone';
import { AuthService } from './auth.service';

// Real-time active-account gate (mirrors the Flutter app). There is no JWT, so we
// send the logged-in employee's id on every request as "X-Emp-Id"; the backend
// re-checks IsActive and, if the account was deactivated mid-session, replies
// 403 + "X-Account-Inactive". On seeing that we sign out to /login and show a
// one-time popup. (CORS exposes the header so the browser can read it.)
let handlingInactive = false;

export const activeAccountInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const alertCtrl = inject(AlertController);

  const empId = auth.currentUser()?.employeeId;
  const authReq = empId != null
    ? req.clone({ setHeaders: { 'X-Emp-Id': String(empId) } })
    : req;

  return next(authReq).pipe(
    catchError((err: unknown) => {
      if (
        err instanceof HttpErrorResponse &&
        err.status === 403 &&
        err.headers.get('X-Account-Inactive') === '1' &&
        !handlingInactive
      ) {
        handlingInactive = true;
        auth.logout(); // clears session + navigates to /login
        void alertCtrl
          .create({
            header: 'Account Deactivated',
            message:
              'Your account has been set to inactive. You have been signed out. Please contact HR / your admin.',
            backdropDismiss: false,
            buttons: [{ text: 'OK', handler: () => (handlingInactive = false) }],
          })
          .then((a) => a.present());
      }
      return throwError(() => err);
    }),
  );
};
