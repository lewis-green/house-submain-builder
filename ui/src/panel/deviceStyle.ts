import type { DeviceCategory } from '../api/types'

interface Style { fill: string; stroke: string; text: string; kind: string }

/**
 * Colour is never the only thing distinguishing a device — every block carries
 * its label too — so this is decoration, not information.
 */
const styles: Record<DeviceCategory, Style> = {
  Dimmer240:   { fill: 'fill-sky-200',    stroke: 'stroke-sky-500',    text: 'fill-sky-950',    kind: 'Dimmer' },
  Dimmer0_10V: { fill: 'fill-indigo-200', stroke: 'stroke-indigo-500', text: 'fill-indigo-950', kind: 'Tape dimmer' },
  Relay:       { fill: 'fill-teal-200',   stroke: 'stroke-teal-500',   text: 'fill-teal-950',   kind: 'Relay' },
  Psu24V:      { fill: 'fill-amber-200',  stroke: 'stroke-amber-500',  text: 'fill-amber-950',  kind: 'PSU' },
  Terminal240: { fill: 'fill-slate-200',  stroke: 'stroke-slate-400',  text: 'fill-slate-800',  kind: 'Terminal' },
  Accessory:   { fill: 'fill-slate-100',  stroke: 'stroke-slate-300',  text: 'fill-slate-600',  kind: 'Accessory' },
}

export const deviceStyle = (category: DeviceCategory) => styles[category]
