import { startStack } from './stack'

/** Brings up the throwaway database and API before the first test. */
export default async function globalSetup() {
  await startStack()
}
