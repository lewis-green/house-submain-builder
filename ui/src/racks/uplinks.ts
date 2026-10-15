import type { PortGroup, RackDownlinkResponse, RackItemResponse, RackUplinkResponse } from '../api/types'

/** "Uplink to Switch 1 · Core · Plant room · port 3", or "Uplink to UDM Pro". */
export function uplinkText(uplink: RackUplinkResponse): string {
  if (uplink.toText) return `Uplink to ${uplink.toText}`
  return [`Uplink to ${uplink.toLabel}`, uplink.toRackName, uplink.toLocationName, `port ${uplink.toPort}`]
    .filter(Boolean).join(' · ')
}

/** "Downlink from Switch 2 · Garage". */
export const downlinkText = (downlink: RackDownlinkResponse) => `Downlink from ${downlink.fromLabel} · ${downlink.fromRackName}`

/** What each linked port of the item says under its label. */
export function portLinks(item: RackItemResponse): Map<number, string> {
  const links = new Map((item.downlinks ?? []).map(d => [d.port, downlinkText(d)]))
  if (item.uplink) links.set(item.uplink.port, uplinkText(item.uplink))
  return links
}

/** Every port of the groups, in order, with the group it is in. */
export const allPorts = (groups: PortGroup[]) =>
  groups.flatMap(group => Array.from({ length: group.to - group.from + 1 }, (_, i) => ({ number: group.from + i, group })))

/**
 * Where a switch's uplink most likely leaves: its first SFP+ port, which is
 * what the SFP+ ports are for, or else its last port, by habit. A port that
 * already takes another switch's uplink is passed over.
 */
export function defaultUplinkPort(item: RackItemResponse): number {
  const taken = new Set((item.downlinks ?? []).map(d => d.port))
  const ports = allPorts(item.portGroups).filter(p => !taken.has(p.number))
  const sfp = ports.find(p => p.group.name === 'SFP+')
  return sfp?.number ?? ports.at(-1)?.number ?? item.portGroups[0]?.from ?? 1
}

/** "Switch 3" beside it, "Switch 1 in Garage" from another rack, whose own "Switch 1" is a different one. */
export const fromName = (downlink: RackDownlinkResponse, rackId: string) =>
  downlink.fromRackId === rackId ? downlink.fromLabel : `${downlink.fromLabel} in ${downlink.fromRackName}`

/** "A", "A and B", "A, B and C". */
const listed = (names: string[]) =>
  names.length <= 1 ? names.join('') : `${names.slice(0, -1).join(', ')} and ${names.at(-1)}`

/** What removing the switch does to the switches that uplink into it, or null when none do. */
export function removalWarning(item: RackItemResponse, rackId: string): string | null {
  const downlinks = item.downlinks ?? []
  if (downlinks.length === 0) return null
  const names = listed(downlinks.map(d => fromName(d, rackId)))
  return downlinks.length === 1
    ? `${names} uplinks into this switch; its uplink will be cleared.`
    : `${names} uplink into this switch; their uplinks will be cleared.`
}
