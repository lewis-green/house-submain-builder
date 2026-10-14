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
  /** Terminals only: the circuit landing on this block. Absent from older responses. */
  circuitId?: string | null
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

/** A stored circuit, as GET /submains/{id}/circuits reads it back to edit the panel. */
export interface CircuitResponse {
  id: string
  type: CircuitType
  name: string
  room: string | null
  sequence: number
  wattsPerMetre: number | null
  lengthMetres: number | null
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
  /** Set on a panel revision. */
  submainId?: string
  /** Set on a rack revision. */
  rackId?: string
  /** Where it stands in its history, counted from one by the server. */
  number: number
  layoutVersion: number
  issuedAt: string
  issuedBy: string
  /** Server-decided: does this revision still match the panel or rack as it is now? */
  current: boolean
}

export type RackDeviceKind = 'Switch' | 'PatchPanel' | 'Accessory'

/** A run of consecutively numbered ports that share a name, and whether PoE can be ticked on them. */
export interface PortGroup {
  name: string
  from: number
  to: number
  poeTick: boolean
}

/** A rack cabinet. Heights are in rack units; U1 is the bottom. */
export interface RackTypeResponse {
  id: string
  manufacturer: string
  model: string
  partNumber: string
  heightU: number
  mounting: 'Wall' | 'Floor'
  description: string
}

export interface RackDeviceTypeResponse {
  id: string
  manufacturer: string
  model: string
  partNumber: string
  kind: RackDeviceKind
  heightU: number
  portGroups: PortGroup[]
  poeBudgetW: number | null
  requiredAccessoryId: string | null
  active: boolean
  description: string
  portCount: number
}

export interface RackResponse {
  id: string
  projectId: string
  name: string
  rackTypeId: string
  cabinet: string
  heightU: number
  usedU: number
  fedFromSubmainId: string | null
  fedFromSubmainName: string | null
  notes: string | null
  layoutVersion: number
}

export interface RackPortResponse {
  number: number
  label: string | null
  poe: boolean
}

export interface RackItemResponse {
  id: string
  rackDeviceTypeId: string
  kind: RackDeviceKind
  model: string
  partNumber: string
  label: string
  /** The highest U the item occupies; U1 is the bottom of the cabinet. */
  topU: number
  heightU: number
  portGroups: PortGroup[]
  poeBudgetW: number | null
  /** Only ports with a label or a PoE tick; the rest are implied by the groups. */
  ports: RackPortResponse[]
}

/** What the server decided. The UI draws it and never places anything itself. */
export interface RackLayoutResponse {
  rackId: string
  heightU: number
  freeU: number
  layoutVersion: number
  /** Top U first. */
  items: RackItemResponse[]
}

export interface FedRack {
  id: string
  name: string
}

/** One circuit as the terminal sheet shows it, in terminal order. */
export interface TerminalCircuit {
  circuitId: string
  name: string
  room: string | null
  type: CircuitType
  terminalCount: number
}

export interface TerminalsResponse {
  layoutVersion: number
  circuits: TerminalCircuit[]
}

/** The most terminals one circuit may land on. Mirrors TerminalBandBuilder.MaxTerminalsPerCircuit. */
export const MAX_TERMINALS_PER_CIRCUIT = 12
