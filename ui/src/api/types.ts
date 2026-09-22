export type DeviceCategory =
  | 'Terminal240'
  | 'Isolator'
  | 'Dimmer240'
  | 'Dimmer0_10V'
  | 'Relay'
  | 'Cover'
  | 'LedController'
  | 'EnergyMeter'
  | 'Network'
  | 'Dc24VPositive'
  | 'Dc24VNegative'
  /** Sized and costed but never placed: LED drivers are not DIN mount. */
  | 'ExternalDriver'
  | 'Accessory'
export type TerminalRole = 'None' | 'All' | 'Line' | 'Neutral' | 'Earth'
export type Severity = 'Info' | 'Warning' | 'Error'
export type CircuitType = 'DimmedLighting' | 'Switched' | 'LedTape' | 'Cover' | 'RgbwTape'

export interface ProjectResponse {
  id: string
  name: string
  address: string | null
  notes: string | null
  submainCount: number
}

export interface SubmainResponse {
  id: string
  projectId: string
  name: string
  reference: string | null
  feedCableSize: string | null
  originBreakerAmps: number | null
  phase: string | null
  enclosureTypeId: string | null
  ruleSetId: string | null
  notes: string | null
  layoutVersion: number
  hasIsolator: boolean
  terminalsAtBottom: boolean
  extraFixtures: ExtraFixture[]
  circuitCount: number
  deviceCount: number
}

export interface ChannelResponse {
  channelIndex: number
  circuitId: string | null
  circuitName: string | null
  circuitRoom: string | null
  isSpare: boolean
}

export interface PlacedDeviceResponse {
  id: string | null
  deviceTypeId: string
  category: DeviceCategory
  rowIndex: number
  startSlot: number
  moduleWidth: number
  label: string
  terminalRole: TerminalRole
  channels: ChannelResponse[]
}

export interface LayoutResponse {
  rows: number
  slotsPerRow: number
  devices: PlacedDeviceResponse[]
}

export interface DiagnosticResponse {
  severity: Severity
  code: string
  message: string
  suggestion: string | null
}

export interface BomLineResponse {
  catalogueId: string
  partNumber: string
  description: string
  quantity: number
  panelMounted: boolean
}

export interface DesignSummary {
  rowsUsed: number
  slotsUsed: number
  totalSlots: number
  deviceCount: number
  spareChannels: number
}

export interface DesignResponse {
  submainId: string
  layoutVersion: number
  layout: LayoutResponse
  diagnostics: DiagnosticResponse[]
  bom: BomLineResponse[]
  summary: DesignSummary
}

export interface EnclosureType {
  id: string
  manufacturer: string
  model: string
  rows: number
  slotsPerRow: number
  ipRating: string
  totalSlots: number
  description: string
}

/** A device on the panel that no circuit asks for: a meter, a LAN switch. */
export interface ExtraFixture {
  deviceTypeId: string
  quantity: number
}

export interface DeviceTypeResponse {
  id: string
  manufacturer: string
  model: string
  partNumber: string
  category: DeviceCategory
  moduleWidth: number
  channelCount: number
  maxLoadPerChannelW: number | null
  maxTotalLoadW: number | null
  active: boolean
}

export interface CircuitInput {
  id?: string
  type: CircuitType
  name: string
  room?: string | null
  sequence: number
  wattsPerMetre?: number | null
  lengthMetres?: number | null
}

export interface BomLineView {
  catalogueId: string
  partNumber: string
  description: string
  quantity: number
  /** False for anything bought but never mounted on the rail. */
  panelMounted: boolean
}

/** A parts list. Costing happens wherever your pricing actually lives. */
export interface BomView {
  lines: BomLineView[]
}

export interface RevisionView {
  id: string
  submainId: string
  layoutVersion: number
  issuedAt: string
  issuedBy: string
}
