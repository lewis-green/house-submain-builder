import { AppShell } from '../components/AppShell'
import { ButtonLink } from '../components/Button'
import { EmptyState } from '../components/EmptyState'

export function NotFoundPage() {
  return (
    <AppShell title="Nothing here" back={{ to: '/', label: 'Houses' }}>
      <EmptyState
        title="That address does not match anything in the app"
        hint="The link may be old, or the house it pointed at has gone."
        action={<ButtonLink to="/" variant="primary">Open the houses list</ButtonLink>}
      />
    </AppShell>
  )
}
