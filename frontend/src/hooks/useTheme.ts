import { useCallback, useEffect, useState } from 'react'

export type Theme = 'light' | 'dark'
const KEY = 'mdel.theme'

function stored(): Theme | null {
  try {
    const v = localStorage.getItem(KEY)
    return v === 'light' || v === 'dark' ? v : null
  } catch {
    return null
  }
}

const systemDark = () => window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false

/** Tema light/dark: alegerea se salvează în localStorage; fără alegere, urmează sistemul. */
export function useTheme() {
  const [theme, setTheme] = useState<Theme>(() => stored() ?? (systemDark() ? 'dark' : 'light'))

  useEffect(() => {
    document.documentElement.setAttribute('data-theme', theme)
  }, [theme])

  const toggle = useCallback(() => {
    setTheme((current) => {
      const next: Theme = current === 'dark' ? 'light' : 'dark'
      try {
        localStorage.setItem(KEY, next)
      } catch {
        // stocare indisponibilă: tema rămâne doar pentru sesiunea curentă
      }
      return next
    })
  }, [])

  return { theme, toggle }
}
