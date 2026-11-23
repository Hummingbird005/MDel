/** Concatenează clase CSS condiționale. */
export const cx = (...classes: (string | false | null | undefined)[]) => classes.filter(Boolean).join(' ')

export const initials = (name: string) =>
  name
    .split(/\s+/)
    .filter(Boolean)
    .map((p) => p[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()
