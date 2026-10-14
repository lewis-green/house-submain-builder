import type { LocationResponse } from './api/types'

/**
 * A house's locations as a picker offers them, "Not placed" first. If the
 * current location is missing from the list — it failed to load, or someone
 * removed it a moment ago — it is still offered, so the picker never shows
 * "Not placed" over a placed panel and a save never unplaces it by accident.
 */
export function locationOptions(locations: LocationResponse[], current: string | null) {
  const options = [{ value: '', label: 'Not placed' }, ...locations.map(l => ({ value: l.id, label: l.name }))]
  if (current && !locations.some(l => l.id === current)) options.push({ value: current, label: 'Its current location' })
  return options
}

/** The name of the location with this id, or null when there is none to name. */
export const locationName = (locations: LocationResponse[], id: string | null) =>
  locations.find(l => l.id === id)?.name ?? null

/** What a GET of the locations gave, as a list even when it was not one. */
export const asLocations = (body: unknown): LocationResponse[] => (Array.isArray(body) ? body : [])
