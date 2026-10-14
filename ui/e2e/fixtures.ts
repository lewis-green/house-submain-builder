import type { APIRequestContext } from '@playwright/test'
import { stack } from './stack'

/** The suite's own API, never the development one: see stack.ts. */
export const API = stack.apiUrl

export interface SeededSubmain {
  submainId: string
  layoutVersion: number
  devices: { id: string; label: string; rowIndex: number; startSlot: number; moduleWidth: number }[]
}

/** Builds a generated submain straight through the API, so each test starts clean. */
export async function seedSubmain(request: APIRequestContext, circuits: unknown[]): Promise<SeededSubmain> {
  const enclosures = await (await request.get(`${API}/catalogue/enclosures`)).json()
  const enclosure = [...enclosures].sort((a, b) => b.totalSlots - a.totalSlots)[0]

  const project = await (await request.post(`${API}/projects`, {
    data: { name: `E2E ${Date.now()}-${Math.random()}` },
  })).json()

  const submain = await (await request.post(`${API}/projects/${project.id}/submains`, {
    data: { name: 'E2E automation panel', enclosureTypeId: enclosure.id, circuits },
  })).json()

  const design = await (await request.post(`${API}/submains/${submain.id}/design/generate`, {
    data: {},
  })).json()

  return { submainId: submain.id, layoutVersion: design.layoutVersion, devices: design.layout.devices }
}

export interface SeededRack {
  projectId: string
  rackId: string
  items: { id: string; label: string; topU: number }[]
}

/** A house with a 12U rack holding a 48-port switch and a patch panel, built straight through the API. */
export async function seedRack(request: APIRequestContext): Promise<SeededRack> {
  const cabinets = await (await request.get(`${API}/catalogue/rack-types`)).json()
  const types = await (await request.get(`${API}/catalogue/rack-device-types`)).json()
  const twelve = cabinets.find((c: { heightU: number }) => c.heightU === 12)
  const sw = types.find((t: { model: string }) => t.model === 'USW-Pro-Max-48-PoE')
  const panel = types.find((t: { kind: string }) => t.kind === 'PatchPanel')

  const project = await (await request.post(`${API}/projects`, { data: { name: `E2E rack ${Date.now()}-${Math.random()}` } })).json()
  const rack = await (await request.post(`${API}/projects/${project.id}/racks`, { data: { name: 'Comms cupboard', rackTypeId: twelve.id } })).json()
  await request.post(`${API}/racks/${rack.id}/items`, { data: { rackDeviceTypeId: sw.id } })
  const layout = await (await request.post(`${API}/racks/${rack.id}/items`, { data: { rackDeviceTypeId: panel.id } })).json()
  return { projectId: project.id, rackId: rack.id, items: layout.items }
}

export const threeDimmed = [
  { type: 'DimmedLighting', name: 'Kitchen ceiling', sequence: 1 },
  { type: 'DimmedLighting', name: 'Kitchen island', sequence: 2 },
  { type: 'DimmedLighting', name: 'Hall', sequence: 3 },
  { type: 'Switched', name: 'Immersion', sequence: 4 },
]

/**
 * Puts the rack in a new location of its house, so the house page lists it by
 * place. The rack edit replaces every setting, so it sends the others back.
 */
export async function placeRack(request: APIRequestContext, rackId: string, locationName: string) {
  const rack = await (await request.get(`${API}/racks/${rackId}`)).json()
  const location = await (await request.post(`${API}/projects/${rack.projectId}/locations`, { data: { name: locationName } })).json()
  await request.patch(`${API}/racks/${rackId}`, {
    data: {
      name: rack.name, rackTypeId: rack.rackTypeId, fedFromSubmainId: rack.fedFromSubmainId, notes: rack.notes,
      locationId: location.id,
    },
  })
  return location as { id: string; name: string }
}
