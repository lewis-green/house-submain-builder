import { describe, expect, it } from 'vitest'
import type { RackItemResponse } from '../src/api/types'
import { defaultUplinkPort, downlinkText, portLinks, removalWarning, uplinkText } from '../src/racks/uplinks'

const sw: RackItemResponse = {
  id: 'i1', rackDeviceTypeId: 't48', kind: 'Switch', model: 'Ubiquiti USW-Pro-Max-48-PoE', partNumber: 'USW', label: 'Switch 1',
  topU: 12, heightU: 1, poeBudgetW: 720,
  portGroups: [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }],
  ports: [], uplink: null, downlinks: [],
}
const down = (port: number, fromLabel: string, fromRackId = 'r1', fromRackName = 'Comms') =>
  ({ port, fromItemId: `from-${fromLabel}-${fromRackId}`, fromLabel, fromRackId, fromRackName })

describe('uplinks', () => {
  it('says where an uplink goes, with the rack, the place if any, and the port', () => {
    expect(uplinkText({
      port: 5, toItemId: 'a', toPort: 3, toText: null, toLabel: 'Switch 1', toRackId: 'r2', toRackName: 'Garage', toLocationName: 'Plant room',
    })).toBe('Uplink to Switch 1 · Garage · Plant room · port 3')
    expect(uplinkText({
      port: 5, toItemId: 'a', toPort: 3, toText: null, toLabel: 'Switch 1', toRackId: 'r2', toRackName: 'Garage', toLocationName: null,
    })).toBe('Uplink to Switch 1 · Garage · port 3')
    expect(uplinkText({
      port: 5, toItemId: null, toPort: null, toText: 'UDM Pro', toLabel: null, toRackId: null, toRackName: null, toLocationName: null,
    })).toBe('Uplink to UDM Pro')
    expect(downlinkText(down(3, 'Switch 2', 'r2', 'Garage'))).toBe('Downlink from Switch 2 · Garage')
  })

  it('puts each link on its port', () => {
    const links = portLinks({
      ...sw,
      uplink: { port: 6, toItemId: null, toPort: null, toText: 'ISP fibre', toLabel: null, toRackId: null, toRackName: null, toLocationName: null },
      downlinks: [down(2, 'Switch 2')],
    })

    expect(links.get(6)).toBe('Uplink to ISP fibre')
    expect(links.get(2)).toBe('Downlink from Switch 2 · Comms')
    expect(links.size).toBe(2)
  })

  it('leaves by the first free SFP+ port, or else the last free port', () => {
    expect(defaultUplinkPort(sw)).toBe(5)
    expect(defaultUplinkPort({ ...sw, downlinks: [down(5, 'Switch 2')] })).toBe(6)
    expect(defaultUplinkPort({ ...sw, portGroups: [{ name: '1G', from: 1, to: 8, poeTick: true }] })).toBe(8)
    expect(defaultUplinkPort({ ...sw, portGroups: [{ name: '1G', from: 1, to: 8, poeTick: true }], downlinks: [down(8, 'Switch 2')] })).toBe(7)
  })

  it('warns that removing a switch clears the uplinks into it, naming another rack\'s switches by rack', () => {
    expect(removalWarning(sw, 'r1')).toBeNull()
    expect(removalWarning({ ...sw, downlinks: [down(5, 'Switch 3')] }, 'r1'))
      .toBe('Switch 3 uplinks into this switch; its uplink will be cleared.')
    expect(removalWarning({ ...sw, downlinks: [down(5, 'Switch 3'), down(6, 'Switch 4')] }, 'r1'))
      .toBe('Switch 3 and Switch 4 uplink into this switch; their uplinks will be cleared.')
    expect(removalWarning({ ...sw, downlinks: [down(2, 'Switch 3'), down(5, 'Switch 1', 'r2', 'Garage'), down(6, 'Switch 4')] }, 'r1'))
      .toBe('Switch 3, Switch 1 in Garage and Switch 4 uplink into this switch; their uplinks will be cleared.')
  })
})
