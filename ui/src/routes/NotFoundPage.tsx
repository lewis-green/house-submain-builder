import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <div className="space-y-3 p-6">
      <h1 className="text-xl font-semibold">Nothing here</h1>
      <p className="text-slate-600">
        That address does not match anything in the app.
      </p>
      <Link to="/" className="text-sm underline">Back to houses</Link>
    </div>
  )
}
