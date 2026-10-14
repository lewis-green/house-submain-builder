import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { RackResponse, RackTypeResponse } from '../src/api/types'
import { StatusRegion } from '../src/components/StatusRegion'
import { RackSettings } from '../src/racks/RackSettings'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const rack: RackResponse = {
  id: 'r1', projectId: 'p1', name: 'Comms cupboard', rackTypeId: 'c12', cabinet: 'Generic 12U wall cabinet', heightU: 12,
  usedU: 1, fedFromSubmainId: null, fedFromSubmainName: null, notes: null, layoutVersion: 3, locationId: null,
}
const cabinets: RackTypeResponse[] = [
  { id: 'c12', manufacturer: 'Generic', model: '12U wall cabinet', partNumber: 'C12', heightU: 12, mounting: 'Wall', description: 'Generic 12U wall cabinet' },
  { id: 'c22', manufacturer: 'Generic', model: '22U cabinet', partNumber: 'C22', heightU: 22, mounting: 'Floor', description: 'Generic 22U cabinet' },
]

function show(patch: () => Response) {
  vi.stubGlobal('fetch', vi.fn(async () => patch()))
  const onSaved = vi.fn()
  const onConflict = vi.fn()
  render(<><RackSettings rack={rack} cabinets={cabinets} submains={[]} onSaved={onSaved} onConflict={onConflict} /><StatusRegion /></>)
  return { onSaved, onConflict }
}

const locations = [
  { id: 'l1', name: 'Plant room', notes: null, sequence: 0 },
  { id: 'l2', name: 'Garage', notes: null, sequence: 1 },
]

/** RackSettings in a rack placed in the plant room, answering every PATCH with what was sent. */
function showPlaced() {
  const fetchMock = vi.fn(async (_url: string, init?: RequestInit) => json({ ...rack, ...JSON.parse(String(init!.body)) }))
  vi.stubGlobal('fetch', fetchMock)
  const onSaved = vi.fn()
  render(<RackSettings rack={{ ...rack, locationId: 'l1' }} cabinets={cabinets} submains={[]} locations={locations} onSaved={onSaved} onConflict={vi.fn()} />)
  const sent = () => JSON.parse(String(fetchMock.mock.calls[0][1]!.body))
  return { onSaved, sent }
}

describe('RackSettings', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('opens on the rack\'s location and moves it to another', async () => {
    const { onSaved, sent } = showPlaced()

    expect(screen.getByLabelText('Location')).toHaveValue('l1')
    await userEvent.selectOptions(screen.getByLabelText('Location'), 'l2')
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }))

    await vi.waitFor(() => expect(onSaved).toHaveBeenCalled())
    expect(sent().locationId).toBe('l2')
  })

  it('keeps the rack where it is when another setting is saved', async () => {
    const { onSaved, sent } = showPlaced()

    await userEvent.selectOptions(screen.getByLabelText('Cabinet'), 'c22')
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }))

    await vi.waitFor(() => expect(onSaved).toHaveBeenCalled())
    expect(sent()).toMatchObject({ rackTypeId: 'c22', locationId: 'l1' })
  })

  it('saves the changed settings and hands the saved rack back', async () => {
    const { onSaved, onConflict } = show(() => json({ ...rack, rackTypeId: 'c22', cabinet: 'Generic 22U cabinet', heightU: 22 }))

    await userEvent.selectOptions(screen.getByLabelText('Cabinet'), 'c22')
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Rack settings saved')
    expect(onSaved).toHaveBeenCalledWith(expect.objectContaining({ rackTypeId: 'c22' }))
    expect(onConflict).not.toHaveBeenCalled()
  })

  it('says the rack changed elsewhere and asks for a reload on a version conflict', async () => {
    const { onSaved, onConflict } = show(() => json({ message: 'This rack changed since you loaded it.', currentLayoutVersion: 5 }, 409))

    await userEvent.selectOptions(screen.getByLabelText('Cabinet'), 'c22')
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }))

    expect(await screen.findByRole('status')).toHaveTextContent('This rack changed elsewhere — reloading')
    expect(onConflict).toHaveBeenCalledTimes(1)
    expect(onSaved).not.toHaveBeenCalled()
  })

  it('shows a field error under its field and says so', async () => {
    show(() => json({ errors: { rackTypeId: ['Rack has an item at U12. Move it before choosing a shorter cabinet.'] } }, 400))

    await userEvent.selectOptions(screen.getByLabelText('Cabinet'), 'c22')
    await userEvent.click(screen.getByRole('button', { name: 'Save settings' }))

    expect(await screen.findByText(/Move it before choosing a shorter cabinet/)).toBeInTheDocument()
    expect(screen.getByRole('status')).toHaveTextContent('Rack settings not saved — see the form')
  })
})
