import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpHeaders } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

export interface Problem {
  title: string;
  detail?: string;
  code?: string;
}

export function problemMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as Problem | undefined;
    if (error.status === 403) {
      return body?.title || "You don't have access to this.";
    }
    return body?.title || body?.detail || `Request failed (${error.status}).`;
  }

  return 'Something went wrong.';
}

export const problemInterceptor: HttpInterceptorFn = (request, next) => next(request);

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  get<T>(url: string): Promise<T> {
    return firstValueFrom(this.http.get<T>(url));
  }

  send<T>(method: 'POST' | 'PUT' | 'DELETE', url: string, body?: unknown, idempotent = false): Promise<T> {
    let headers = new HttpHeaders();
    if (idempotent) {
      headers = headers.set('Idempotency-Key', crypto.randomUUID());
    }

    return firstValueFrom(this.http.request<T>(method, url, { body, headers }));
  }
}
