import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { FixturePicker } from '../src/wizard/FixturePicker'
import type { DeviceTypeResponse, ExtraFixture } from '../src/api/types'

const device = (
  id: string,
  model: string,
  category: DeviceTypeResponse['category'],
  moduleWidth = 9,
  active = true,
): DeviceTypeResponse => ({
  id, manufacturer: 'Shelly', model, partNumber: id.toUpperCase(), category,
  moduleWidth, channelCount: 3, maxLoadPerChannelW: null, maxTotalLoadW: null, active,
})

const catalogue = [
  device('meter', 'Pro 3EM', 'EnergyMeter'),
  device('lan', 'LAN Switch', 'Network', 6),
  device('relay', 'Pro 4PM', 'Relay'),
  device('driver', 'DR-240-24', 'ExternalDriver', 0),
  device('old', 'Pro 1 (retired)', 'Relay', 3, false),
]

function show(fixtures: ExtraFixture[] = [], onChange = vi.fn()) {
  render(<FixturePicker catalogue={catalogue} fixtures={fixtures} onChange={onChange} />)
  return onChange
}

describe('FixturePicker', () => {
  it('offers devices that take rail space', async () => {
    show()

    const options = screen.getAllByRole('option').map(o => o.textContent)
    expect(options).toContain('Shelly Pro 3EM')
    expect(options).toContain('Shelly LAN Switch')
  })

  it('leaves out anything that is never placed', () => {
    show()

    // An external driver lives outside the panel; it has no rail position to add.
    expect(screen.queryByRole('option', { name: /DR-240-24/ })).not.toBeInTheDocument()
  })

  it('leaves out retired catalogue entries', () => {
    show()

    expect(screen.queryByRole('option', { name: /retired/ })).not.toBeInTheDocument()
  })

  it('adds a chosen device with a quantity of one', async () => {
    const onChange = show()

    await userEvent.selectOptions(screen.getByLabelText('Device to add'), 'meter')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))

    expect(onChange).toHaveBeenCalledWith([{ deviceTypeId: 'meter', quantity: 1 }])
  })

  it('adding the same device again raises its quantity rather than listing it twice', async () => {
    const onChange = show([{ deviceTypeId: 'meter', quantity: 1 }])

    await userEvent.selectOptions(screen.getByLabelText('Device to add'), 'meter')
    await userEvent.click(screen.getByRole('button', { name: 'Add' }))

    expect(onChange).toHaveBeenCalledWith([{ deviceTypeId: 'meter', quantity: 2 }])
  })

  it('names what is already on the list', () => {
    // By its quantity box: the name also appears in the select, so matching on
    // text alone would pass without a row ever being rendered.
    show([{ deviceTypeId: 'lan', quantity: 1 }])

    expect(screen.getByLabelText('Quantity of Shelly LAN Switch')).toHaveValue(1)
  })

  it('removes a device from the list', async () => {
    const onChange = show([{ deviceTypeId: 'meter', quantity: 2 }, { deviceTypeId: 'lan', quantity: 1 }])

    await userEvent.click(screen.getAllByRole('button', { name: 'Remove' })[0])

    expect(onChange).toHaveBeenCalledWith([{ deviceTypeId: 'lan', quantity: 1 }])
  })

  it('never lets a quantity fall below one', async () => {
    const onChange = show([{ deviceTypeId: 'meter', quantity: 2 }])

    const quantity = screen.getByLabelText(/Quantity of Shelly Pro 3EM/)
    await userEvent.clear(quantity)

    expect(onChange).toHaveBeenLastCalledWith([{ deviceTypeId: 'meter', quantity: 1 }])
  })

  it('cannot add without choosing a device first', () => {
    show()

    expect(screen.getByRole('button', { name: 'Add' })).toBeDisabled()
  })
})
