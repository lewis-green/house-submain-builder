/**
 * A circuit's terminals as the server labels them: "C3" on one block, or
 * "C3.1–C3.6" across six bridged ones. `position` counts from 1.
 */
export function terminalLabel(position: number, count: number): string {
  return count <= 1 ? `C${position}` : `C${position}.1–C${position}.${count}`
}
