/** Mirrors DinUnits.PerModule on the server. Three of these make one DIN module (T). */
export const SLOT_UNITS_PER_MODULE = 3

export const toModules = (slotUnits: number) => slotUnits / SLOT_UNITS_PER_MODULE

export function formatModules(slotUnits: number): string {
  const modules = toModules(slotUnits)
  return Number.isInteger(modules) ? `${modules}T` : `${modules.toFixed(1)}T`
}
