import type { APIRequestContext } from '@playwright/test'

const API = 'http://localhost:5020'

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
    data: { name: 'E2E submain', enclosureTypeId: enclosure.id, circuits },
  })).json()

  const design = await (await request.post(`${API}/submains/${submain.id}/design/generate`, {
    data: {},
  })).json()

  return { submainId: submain.id, layoutVersion: design.layoutVersion, devices: design.layout.devices }
}

export const threeDimmed = [
  { type: 'DimmedLighting', name: 'Kitchen ceiling', sequence: 1 },
  { type: 'DimmedLighting', name: 'Kitchen island', sequence: 2 },
  { type: 'DimmedLighting', name: 'Hall', sequence: 3 },
  { type: 'Switched', name: 'Immersion', sequence: 4 },
]
