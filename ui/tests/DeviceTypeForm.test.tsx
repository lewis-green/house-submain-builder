import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { DeviceTypeForm } from '../src/routes/CataloguePage'

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'content-type': 'application/json' } })

const dimmer = {
  id: 'd1', manufacturer: 'Shelly', model: 'Pro Dimmer 2PM', partNumber: 'SHELLY-DIM',
  category: 'Dimmer240' as const, moduleWidth: 3, channelCount: 2,
  maxLoadPerChannelW: null, maxTotalLoadW: null, cost: 60, active: true,
}

const terminal = { ...dimmer, id: 't1', category: 'Terminal240' as const, moduleWidth: 1, channelCount: 0, partNumber: '2003-7646' }

const noop = () => {}

describe('DeviceTypeForm', () => {
  beforeEach(() => vi.restoreAllMocks())

  it('shows a 3 slot unit device as 1 DIN module', () => {
    render(<DeviceTypeForm device={dimmer} onClose={noop} onSaved={noop} />)

    expect(screen.getByLabelText(/width in din modules/i)).toHaveValue(1)
  })

  it('shows a 9 slot unit device as 3 modules', () => {
    render(<DeviceTypeForm device={{ ...dimmer, moduleWidth: 9 }} onClose={noop} onSaved={noop} />)

    expect(screen.getByLabelText(/width in din modules/i)).toHaveValue(3)
  })

  it('saves modules back as slot units', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json(dimmer))
    vi.stubGlobal('fetch', fetchMock)

    render(<DeviceTypeForm device={dimmer} onClose={noop} onSaved={noop} />)

    const width = screen.getByLabelText(/width in din modules/i)
    await userEvent.clear(width)
    await userEvent.type(width, '2')
    await userEvent.click(screen.getByRole('button', { name: /save/i }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string)
    expect(body.moduleWidth).toBe(6)
  })

  it('asks a terminal for blocks per module and converts it back', async () => {
    const fetchMock = vi.fn().mockResolvedValue(json(terminal))
    vi.stubGlobal('fetch', fetchMock)

    render(<DeviceTypeForm device={terminal} onClose={noop} onSaved={noop} />)

    expect(screen.getByLabelText(/blocks per din module/i)).toHaveValue(3)

    await userEvent.click(screen.getByRole('button', { name: /save/i }))

    const body = JSON.parse(fetchMock.mock.calls[0][1].body as string)
    expect(body.moduleWidth).toBe(1)
  })

  it('offers no channel field for a terminal block', () => {
    render(<DeviceTypeForm device={terminal} onClose={noop} onSaved={noop} />)

    expect(screen.queryByLabelText(/channels/i)).not.toBeInTheDocument()
  })

  it('shows the API validation message against its field', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(
      { title: 'invalid', errors: { moduleWidth: ['Only accessories may have no width'] } },
      400)))

    render(<DeviceTypeForm device={dimmer} onClose={noop} onSaved={noop} />)
    await userEvent.click(screen.getByRole('button', { name: /save/i }))

    expect(await screen.findByText(/only accessories may have no width/i)).toBeInTheDocument()
  })

  it('reports a refusal that is not tied to a field', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Ruleset still uses this device' }, 400)))

    render(<DeviceTypeForm device={dimmer} onClose={noop} onSaved={noop} />)
    await userEvent.click(screen.getByRole('button', { name: /save/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/still uses this device/i)
  })
})
