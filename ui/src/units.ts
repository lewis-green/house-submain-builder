/** Mirrors DinUnits.PerModule on the server. Three of these make one DIN module. */
export const SLOT_UNITS_PER_MODULE = 3

export const toModules = (slotUnits: number) => slotUnits / SLOT_UNITS_PER_MODULE

/** A number of modules as a person reads it: "1.3", never the thirds underneath. */
export function formatModuleCount(slotUnits: number): string {
  const modules = toModules(slotUnits)
  return Number.isInteger(modules) ? String(modules) : modules.toFixed(1)
}

/** With its unit spelt out, because "T" means nothing to someone new to DIN gear. */
export function formatModules(slotUnits: number): string {
  const count = formatModuleCount(slotUnits)
  return `${count} ${count === '1' ? 'module' : 'modules'}`
}
