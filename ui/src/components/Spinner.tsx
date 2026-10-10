/** For the moments before there is a page to put a skeleton in, such as signing in. */
export function Spinner({ label }: { label: string }) {
  return (
    <div role="status" className="flex items-center gap-3 p-4 text-ink-2">
      <span className="size-5 animate-spin rounded-full border-2 border-rule border-t-ink motion-reduce:animate-none" />
      {label}
    </div>
  )
}
