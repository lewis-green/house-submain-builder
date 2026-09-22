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
  Cover:         { fill: 'fill-violet-200', stroke: 'stroke-violet-500', text: 'fill-violet-950', kind: 'Cover' },
  LedController: { fill: 'fill-fuchsia-200',stroke: 'stroke-fuchsia-500',text: 'fill-fuchsia-950',kind: 'LED controller' },
  EnergyMeter:   { fill: 'fill-lime-200',   stroke: 'stroke-lime-500',   text: 'fill-lime-950',   kind: 'Energy meter' },
  Network:       { fill: 'fill-stone-200',  stroke: 'stroke-stone-500',  text: 'fill-stone-800',  kind: 'Network' },
  Isolator:      { fill: 'fill-red-200',    stroke: 'stroke-red-500',    text: 'fill-red-950',    kind: 'Isolator' },
  Dc24VPositive: { fill: 'fill-amber-200',  stroke: 'stroke-amber-500',  text: 'fill-amber-950',  kind: '+24V' },
  Dc24VNegative: { fill: 'fill-slate-300',  stroke: 'stroke-slate-500',  text: 'fill-slate-900',  kind: '-24V' },
  ExternalDriver:{ fill: 'fill-slate-100',  stroke: 'stroke-slate-300',  text: 'fill-slate-600',  kind: 'Driver (external)' },
  Terminal240: { fill: 'fill-slate-200',  stroke: 'stroke-slate-400',  text: 'fill-slate-800',  kind: 'Terminal' },
  Accessory:   { fill: 'fill-slate-100',  stroke: 'stroke-slate-300',  text: 'fill-slate-600',  kind: 'Accessory' },
}

export const deviceStyle = (category: DeviceCategory) => styles[category]
