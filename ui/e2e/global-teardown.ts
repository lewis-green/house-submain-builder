import { stopStack } from './stack'

/** Takes the API down and removes the database container, data and all. */
export default function globalTeardown() {
  stopStack()
}
