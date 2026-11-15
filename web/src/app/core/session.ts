import { computed, inject } from '@angular/core';
import { patchState, signalStore, withComputed, withMethods, withState } from '@ngrx/signals';
import { Api, problemMessage } from './api';

export interface ModuleFlag {
  name: string;
  enabled: boolean;
}

export interface Session {
  id: string | null;
  email: string | null;
  displayName: string | null;
  permissions: string[];
  modules: ModuleFlag[];
  mfaRequired: boolean;
}

export const SessionStore = signalStore(
  { providedIn: 'root' },
  withState({ session: null as Session | null, ready: false, error: null as string | null }),
  withComputed(({ session }) => ({
    signedIn: computed(() => !!session()?.id),
    permissions: computed(() => new Set(session()?.permissions ?? [])),
  })),
  withMethods((store, api = inject(Api)) => ({
    can(permission: string): boolean {
      return store.permissions().has(permission);
    },
    moduleEnabled(name: string): boolean {
      return store.session()?.modules.find((module) => module.name === name)?.enabled !== false;
    },
    async refresh(): Promise<void> {
      try {
        const session = await api.get<Session>('/api/identity/v1/auth/me');
        patchState(store, { session, ready: true, error: null });
      } catch {
        patchState(store, { session: null, ready: true });
      }
    },
    async signIn(email: string, password: string): Promise<boolean> {
      try {
        const session = await api.send<Session>('POST', '/api/identity/v1/auth/sign-in', { email, password });
        patchState(store, { session, ready: true, error: session.mfaRequired ? 'A second factor is required.' : null });
        return !!session.id;
      } catch (error) {
        patchState(store, { error: problemMessage(error) });
        return false;
      }
    },
    async signOut(): Promise<void> {
      await api.send('POST', '/api/identity/v1/auth/sign-out');
      patchState(store, { session: null });
    },
  })),
);
