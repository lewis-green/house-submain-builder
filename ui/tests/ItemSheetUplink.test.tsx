import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../src/api/client'
import type { HouseSwitch, RackItemResponse, RackLayoutResponse } from '../src/api/types'
import { ItemSheet } from '../src/racks/ItemSheet'

const groups = [{ name: '1G', from: 1, to: 4, poeTick: true }, { name: 'SFP+', from: 5, to: 6, poeTick: false }]
const sfp8 = [{ name: 'SFP+', from: 1, to: 8, poeTick: false }]
const sw: RackItemResponse = {
  id: 'i1', rackDeviceTypeId: 't48', kind: 'Switch', model: 'Ubiquiti USW-Pro-Max-48-PoE', partNumber: 'USW', label: 'Switch 1',
  topU: 12, heightU: 1, poeBudgetW: 720, portGroups: groups, ports: [], uplink: null, downlinks: [],
}
const layout: RackLayoutResponse = { rackId: 'r1', heightU: 12, freeU: 11, layoutVersion: 3, items: [sw] }

const house: HouseSwitch[] = [
  { id: 'i1', label: 'Switch 1', model: 'Ubiquiti USW-Pro-Max-48-PoE', rackId: 'r1', rackName: 'Comms', locationId: 'l1', locationName: 'Plant room', portGroups: groups, uplinkPort: null, downlinks: [], uplinkToItemId: null },
  { id: 'i7', label: 'Switch 2', model: 'Ubiquiti USW-Pro-Max-16-PoE', rackId: 'r1', rackName: 'Comms', locationId: 'l1', locationName: 'Plant room', portGroups: groups, uplinkPort: null, downlinks: [], uplinkToItemId: null },
  {
    id: 'i9', label: 'Switch 1', model: 'Ubiquiti USW-Aggregation', rackId: 'r2', rackName: 'Core', locationId: 'l1', locationName: 'Plant room', portGroups: sfp8, uplinkPort: 8,
    downlinks: [{ port: 1, fromItemId: 'i7', fromLabel: 'Switch 2', fromRackId: 'r1', fromRackName: 'Comms' }], uplinkToItemId: null,
  },
  { id: 'i8', label: 'Switch 1', model: 'Ubiquiti USW-Pro-Max-24-PoE', rackId: 'r3', rackName: 'Garage', locationId: null, locationName: null, portGroups: groups, uplinkPort: null, downlinks: [], uplinkToItemId: null },
]

function show(item: RackItemResponse = sw, switches: HouseSwitch[] | null = house, switchesError: string | null = null) {
  const onSave = vi.fn(async () => {})
  const onRemoveUplink = vi.fn(async () => {})
  const onRetrySwitches = vi.fn()
  const onRemove = vi.fn()
  render(
    <ItemSheet
      item={item}
      layout={{ ...layout, items: [item] }}
      onSavePort={vi.fn(async () => {})}
      onMove={vi.fn()}
      onRemove={onRemove}
      onClose={() => {}}
      uplinks={{ switches, switchesError, onRetrySwitches, onSave, onRemove: onRemoveUplink }}
    />,
  )
  return { onSave, onRemoveUplink, onRemove, onRetrySwitches }
}

const options = (label: string) => within(screen.getByLabelText(label)).getAllByRole('option') as HTMLOptionElement[]

describe('ItemSheet uplinks', () => {
  it('says on the uplink port where it goes, and on a destination port whose uplink arrives', () => {
    show({
      ...sw,
      uplink: { port: 6, toItemId: 'i9', toPort: 3, toText: null, toLabel: 'Switch 1', toRackId: 'r2', toRackName: 'Core', toLocationName: 'Plant room' },
      downlinks: [{ port: 2, fromItemId: 'i8', fromLabel: 'Switch 1', fromRackId: 'r3', fromRackName: 'Garage' }],
    })

    expect(screen.getByText('Uplink to Switch 1 · Core · Plant room · port 3')).toBeInTheDocument()
    expect(screen.getByLabelText('Port 6 label')).toHaveAccessibleDescription('Uplink to Switch 1 · Core · Plant room · port 3')
    expect(screen.getByLabelText('Port 2 label')).toHaveAccessibleDescription('Downlink from Switch 1 · Garage')
    // The link is read-only on the row; its label still edits as before.
    expect(screen.getByLabelText('Port 2 label')).toBeEnabled()
  })

  it('leaves by the first SFP+ port unless told otherwise, each port named with its group', () => {
    show()

    expect(screen.getByLabelText('Uplink port')).toHaveValue('5')
    expect(options('Uplink port').map(o => o.textContent)).toEqual(['1 · 1G', '2 · 1G', '3 · 1G', '4 · 1G', '5 · SFP+', '6 · SFP+'])
    expect(screen.getByLabelText('Goes to')).toHaveValue('none')
  })

  it('offers the house\'s other switches by place and rack, and saves the port picked on one', async () => {
    const { onSave } = show()

    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'switch')
    const picker = screen.getByLabelText('Switch')
    expect([...picker.querySelectorAll('optgroup')].map(g => g.label)).toEqual(['Plant room · Comms', 'Plant room · Core', 'Not placed · Garage'])
    expect(within(picker).queryByRole('option', { name: /Switch 1 · Ubiquiti USW-Pro-Max-48-PoE/ })).not.toBeInTheDocument()

    await userEvent.selectOptions(picker, 'i9')
    const ports = options('Its port')
    expect(ports.find(o => o.value === '1')).toMatchObject({ disabled: true, textContent: '1 · SFP+ — taken by Switch 2' })
    expect(ports.find(o => o.value === '8')).toMatchObject({ disabled: true, textContent: '8 · SFP+ — its own uplink' })
    expect(ports.find(o => o.value === '3')).toMatchObject({ disabled: false, textContent: '3 · SFP+' })

    await userEvent.selectOptions(screen.getByLabelText('Its port'), '3')
    await userEvent.click(screen.getByRole('button', { name: 'Save uplink' }))

    expect(onSave).toHaveBeenCalledWith({ port: 5, toItemId: 'i9', toPort: 3 })
  })

  it('saves something in words instead', async () => {
    const { onSave } = show()

    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'text')
    await userEvent.type(screen.getByLabelText('What it plugs into'), 'UDM Pro')
    await userEvent.selectOptions(screen.getByLabelText('Uplink port'), '6')
    await userEvent.click(screen.getByRole('button', { name: 'Save uplink' }))

    expect(onSave).toHaveBeenCalledWith({ port: 6, toText: 'UDM Pro' })
  })

  it('will not offer a port that takes another switch\'s uplink as this one\'s own', () => {
    show({ ...sw, downlinks: [{ port: 5, fromItemId: 'i8', fromLabel: 'Switch 1', fromRackId: 'r3', fromRackName: 'Garage' }] })

    expect(options('Uplink port').find(o => o.value === '5')).toMatchObject({ disabled: true, textContent: '5 · SFP+ — taken by Switch 1 in Garage' })
    expect(screen.getByLabelText('Uplink port')).toHaveValue('6')
  })

  it('shows a refusal under the field it is about', async () => {
    const { onSave } = show()
    onSave.mockRejectedValueOnce(new ApiError(400, 'One or more validation errors occurred.', { toPort: ['Port 3 on Switch 1 in Core already takes the uplink from Switch 2.'] }))

    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'switch')
    await userEvent.selectOptions(screen.getByLabelText('Switch'), 'i9')
    await userEvent.selectOptions(screen.getByLabelText('Its port'), '3')
    await userEvent.click(screen.getByRole('button', { name: 'Save uplink' }))

    expect(await screen.findByText('Port 3 on Switch 1 in Core already takes the uplink from Switch 2.')).toBeInTheDocument()
    expect(screen.getByLabelText('Its port')).toHaveAttribute('aria-invalid', 'true')
  })

  it('opens on the uplink as recorded, and removes it on request', async () => {
    const { onRemoveUplink } = show({
      ...sw, uplink: { port: 6, toItemId: null, toPort: null, toText: 'ISP fibre', toLabel: null, toRackId: null, toRackName: null, toLocationName: null },
    })

    expect(screen.getByLabelText('Uplink port')).toHaveValue('6')
    expect(screen.getByLabelText('Goes to')).toHaveValue('text')
    expect(screen.getByLabelText('What it plugs into')).toHaveValue('ISP fibre')
    await userEvent.click(screen.getByRole('button', { name: 'Remove uplink' }))

    expect(onRemoveUplink).toHaveBeenCalled()
  })

  it('still offers the switch an uplink goes to when the house\'s list has lost it', async () => {
    show({
      ...sw,
      uplink: { port: 6, toItemId: 'gone', toPort: 3, toText: null, toLabel: 'Switch 4', toRackId: 'r9', toRackName: 'Loft', toLocationName: null },
    }, null)

    expect(screen.getByLabelText('Switch')).toHaveValue('gone')
    expect(screen.getByText(/loading the house's switches/i)).toBeInTheDocument()
  })

  it('says which switches lose their uplinks before a switch is removed', async () => {
    const { onRemove } = show({
      ...sw,
      downlinks: [
        { port: 2, fromItemId: 'a', fromLabel: 'Switch 3', fromRackId: 'r1', fromRackName: 'Comms' },
        { port: 3, fromItemId: 'b', fromLabel: 'Switch 4', fromRackId: 'r1', fromRackName: 'Comms' },
      ],
    })

    await userEvent.click(screen.getByRole('button', { name: 'Remove from rack' }))
    expect(screen.getByText('Remove Switch 1? Switch 3 and Switch 4 uplink into this switch; their uplinks will be cleared.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Yes, remove it' }))

    expect(onRemove).toHaveBeenCalled()
  })

  it('offers no uplink on a patch panel', () => {
    show({ ...sw, kind: 'PatchPanel', label: 'Patch panel 1' })

    expect(screen.queryByLabelText('Uplink port')).not.toBeInTheDocument()
  })
  it('keeps out a switch whose own uplinks lead back here, saying it would make a loop', async () => {
    // Switch 1 in Garage uplinks into Switch 2, which uplinks into this switch.
    show(sw, house.map(s =>
      s.id === 'i7' ? { ...s, uplinkToItemId: 'i1' } : s.id === 'i8' ? { ...s, uplinkToItemId: 'i7' } : s))

    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'switch')
    const picker = options('Switch')
    expect(picker.find(o => o.value === 'i7')).toMatchObject({ disabled: true, textContent: 'Switch 2 · Ubiquiti USW-Pro-Max-16-PoE — would make a loop' })
    expect(picker.find(o => o.value === 'i8')).toMatchObject({ disabled: true })
    expect(picker.find(o => o.value === 'i9')).toMatchObject({ disabled: false })
  })

  it('removes the uplink when "Not recorded" is chosen, with Remove as the one action', async () => {
    const { onRemoveUplink, onSave } = show({
      ...sw, uplink: { port: 6, toItemId: null, toPort: null, toText: 'ISP fibre', toLabel: null, toRackId: null, toRackName: null, toLocationName: null },
    })

    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'none')
    expect(screen.queryByRole('button', { name: 'Save uplink' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Remove uplink' })).toHaveLength(1)
    // The primary action, as Save is in the other modes: the choice does what it says.
    expect(screen.getByRole('button', { name: 'Remove uplink' })).toHaveClass('bg-accent')
    expect(screen.getByText(/Remove uplink clears it/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Remove uplink' }))

    expect(onRemoveUplink).toHaveBeenCalled()
    expect(onSave).not.toHaveBeenCalled()
  })

  it('says when the house\'s switches did not load, and tries again on request', async () => {
    const { onRetrySwitches } = show(sw, null, 'The server hit a problem (500). Try again.')

    await userEvent.selectOptions(screen.getByLabelText('Goes to'), 'switch')
    expect(screen.getByRole('alert')).toHaveTextContent('The server hit a problem (500).')
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(onRetrySwitches).toHaveBeenCalled()
  })
})
