import { useEffect } from 'react'

export const APP_NAME = 'House Panel Designer'

/** The tab, the history list and the Home Screen bookmark all read this. */
export function useTitle(title: string) {
  useEffect(() => {
    document.title = title === APP_NAME ? APP_NAME : `${title} · ${APP_NAME}`
  }, [title])
}
