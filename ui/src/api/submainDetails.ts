import type { SubmainResponse } from './types'

/**
 * Every field PATCH /submains/{id} replaces outright, as the submain has them
 * now. The endpoint takes a missing one as "clear it" — a blank reference, no
 * notes, the default ruleset in place of the chosen one, the panel taken out
 * of its location — so a caller that
 * changes only some spreads this first and overrides the ones it changes.
 * One helper, so the panel screen and the edit form cannot drift apart.
 */
export const currentDetails = (s: SubmainResponse) => ({
  name: s.name,
  reference: s.reference,
  feedCableSize: s.feedCableSize,
  originBreakerAmps: s.originBreakerAmps,
  phase: s.phase,
  enclosureTypeId: s.enclosureTypeId,
  ruleSetId: s.ruleSetId,
  notes: s.notes,
  locationId: s.locationId,
})
