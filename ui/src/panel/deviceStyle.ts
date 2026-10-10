import type { DeviceCategory } from '../api/types'

interface Style {
  fill: string
  stroke: string
  text: string
  kind: string
  /** One sentence on what the device does, for someone new to the kit. */
  what: string
}

/**
 * Colour is never the only thing distinguishing a device — every block carries
 * its label and its kind too — so this is decoration, not information. Each
 * entry has a dark pair: a pale fill with near-black text by day, a deep fill
 * with pale text at night, with the same hue so the key still reads.
 */
const styles: Record<DeviceCategory, Style> = {
  Dimmer240:     { fill: 'fill-sky-200 dark:fill-sky-900',         stroke: 'stroke-sky-600 dark:stroke-sky-400',         text: 'fill-sky-950 dark:fill-sky-100',         kind: 'Dimmer',           what: 'Dims mains lighting: one channel per circuit.' },
  Dimmer0_10V:   { fill: 'fill-indigo-200 dark:fill-indigo-900',   stroke: 'stroke-indigo-600 dark:stroke-indigo-400',   text: 'fill-indigo-950 dark:fill-indigo-100',   kind: 'Tape dimmer',      what: 'Dims LED tape through its 0–10V driver.' },
  Relay:         { fill: 'fill-teal-200 dark:fill-teal-900',       stroke: 'stroke-teal-600 dark:stroke-teal-400',       text: 'fill-teal-950 dark:fill-teal-100',       kind: 'Relay',            what: 'Switches loads on and off: one channel per circuit.' },
  Cover:         { fill: 'fill-violet-200 dark:fill-violet-900',   stroke: 'stroke-violet-600 dark:stroke-violet-400',   text: 'fill-violet-950 dark:fill-violet-100',   kind: 'Blinds',           what: 'Drives blinds up and down: one channel per blind.' },
  LedController: { fill: 'fill-fuchsia-200 dark:fill-fuchsia-900', stroke: 'stroke-fuchsia-600 dark:stroke-fuchsia-400', text: 'fill-fuchsia-950 dark:fill-fuchsia-100', kind: 'LED controller',   what: 'Drives colour (RGBW) LED tape.' },
  EnergyMeter:   { fill: 'fill-lime-200 dark:fill-lime-900',       stroke: 'stroke-lime-600 dark:stroke-lime-400',       text: 'fill-lime-950 dark:fill-lime-100',       kind: 'Energy meter',     what: 'Measures what this panel draws.' },
  Network:       { fill: 'fill-stone-200 dark:fill-stone-700',     stroke: 'stroke-stone-500 dark:stroke-stone-400',     text: 'fill-stone-900 dark:fill-stone-100',     kind: 'Network',          what: 'Connects the devices in this panel to the house network.' },
  Isolator:      { fill: 'fill-red-200 dark:fill-red-900',         stroke: 'stroke-red-600 dark:stroke-red-400',         text: 'fill-red-950 dark:fill-red-100',         kind: 'Isolator',         what: 'The two-pole main switch: cuts the supply to everything in this panel.' },
  Dc24VPositive: { fill: 'fill-amber-200 dark:fill-amber-900',     stroke: 'stroke-amber-600 dark:stroke-amber-400',     text: 'fill-amber-950 dark:fill-amber-100',     kind: '+24V',             what: 'The +24V joint block: the positive feed shared by every LED tape run.' },
  Dc24VNegative: { fill: 'fill-slate-300 dark:fill-slate-700',     stroke: 'stroke-slate-500 dark:stroke-slate-400',     text: 'fill-slate-900 dark:fill-slate-100',     kind: '-24V',             what: 'The −24V joint block: the return shared by every LED tape run.' },
  ExternalDriver:{ fill: 'fill-slate-100 dark:fill-slate-800',     stroke: 'stroke-slate-400 dark:stroke-slate-500',     text: 'fill-slate-700 dark:fill-slate-200',     kind: 'Driver (external)', what: 'The LED tape power supply, mounted outside the panel.' },
  Terminal240:   { fill: 'fill-slate-200 dark:fill-slate-700',     stroke: 'stroke-slate-500 dark:stroke-slate-400',     text: 'fill-slate-900 dark:fill-slate-100',     kind: 'Terminal',         what: 'Where a circuit cable lands in the panel.' },
  Accessory:     { fill: 'fill-slate-100 dark:fill-slate-800',     stroke: 'stroke-slate-400 dark:stroke-slate-500',     text: 'fill-slate-700 dark:fill-slate-200',     kind: 'Accessory',        what: 'A part fitted to the rail, such as an end stop or a jumper bar.' },
}

export const deviceStyle = (category: DeviceCategory) => styles[category]
