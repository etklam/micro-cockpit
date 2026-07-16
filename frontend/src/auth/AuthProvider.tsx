import { createContext, useContext, useEffect, useMemo, useState } from 'react'
import type { ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import * as session from '../api'

export type SessionState = 'restoring' | 'authenticated' | 'anonymous'
type AuthContextValue = { state: SessionState; login: (email: string, password: string) => Promise<void>; logout: () => Promise<void> }
const AuthContext = createContext<AuthContextValue>(null!)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<SessionState>('restoring')
  const queryClient = useQueryClient()

  useEffect(() => {
    const anonymous = () => { queryClient.clear(); setState('anonymous') }
    session.configureSession(anonymous)
    session.restoreSession().then(restored => setState(restored ? 'authenticated' : 'anonymous')).catch(anonymous)
  }, [queryClient])

  const value = useMemo<AuthContextValue>(() => ({
    state,
    login: async (email, password) => { await session.login(email, password); setState('authenticated') },
    logout: async () => { await session.logout(); queryClient.clear(); setState('anonymous') },
  }), [queryClient, state])

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}

export const useAuth = () => useContext(AuthContext)
