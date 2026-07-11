// One consistent icon set, hand-rolled (no dependency).
// 24×24 grid, 1.6 stroke, round joins. `currentColor` inherits text colour.

import type { SVGProps } from 'react'

export type IconName =
  | 'today' | 'diary' | 'calendar' | 'compass' | 'bell'
  | 'plus' | 'trash' | 'edit' | 'check' | 'close'
  | 'left' | 'right' | 'arrow' | 'logout' | 'sparkle' | 'dot' | 'layers'

const PATHS: Record<IconName, string> = {
  today: '<circle cx="12" cy="12" r="4"/><path d="M12 2v2.5M12 19.5V22M4.2 4.2l1.8 1.8M18 18l1.8 1.8M2 12h2.5M19.5 12H22M4.2 19.8 6 18M18 6l1.8-1.8"/>',
  diary: '<path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H20v15H6.5A2.5 2.5 0 0 0 4 20.5z"/><path d="M4 20.5A2.5 2.5 0 0 1 6.5 18H20v3H6.5A2.5 2.5 0 0 1 4 18.5z"/><path d="M10 7.5h6M10 11h4"/>',
  calendar: '<rect x="3.5" y="5" width="17" height="15.5" rx="2.5"/><path d="M3.5 9.5h17M8 3v3.5M16 3v3.5"/><circle cx="8.5" cy="13.5" r=".9" fill="currentColor" stroke="none"/>',
  compass: '<circle cx="12" cy="12" r="9"/><path d="m15.5 8.5-2 5-5 2 2-5z"/><circle cx="12" cy="12" r=".9" fill="currentColor" stroke="none"/>',
  bell: '<path d="M6 9a6 6 0 0 1 12 0c0 5 1.5 6.5 1.5 6.5h-15S6 14 6 9"/><path d="M10.5 19a1.8 1.8 0 0 0 3 0"/>',
  plus: '<path d="M12 5v14M5 12h14"/>',
  trash: '<path d="M4 6.5h16M9.5 6.5V5a2 2 0 0 1 2-2h1a2 2 0 0 1 2 2v1.5M6 6.5 7 20a2 2 0 0 0 2 1.8h6A2 2 0 0 0 17 20l1-13.5"/><path d="M10 10.5v6M14 10.5v6"/>',
  edit: '<path d="M14.5 5.5 18.5 9.5 8 20l-4.5 1 1-4.5z"/><path d="m13 7 4 4"/>',
  check: '<path d="m4.5 12.5 5 5 10-11"/>',
  close: '<path d="M6 6l12 12M18 6 6 18"/>',
  left: '<path d="m14.5 5-6.5 7 6.5 7"/>',
  right: '<path d="m9.5 5 6.5 7-6.5 7"/>',
  arrow: '<path d="M5 12h13.5M13 6.5 19.5 12 13 17.5"/>',
  logout: '<path d="M14 4.5H7A2.5 2.5 0 0 0 4.5 7v10A2.5 2.5 0 0 0 7 19.5h7"/><path d="M13 12h8M18 8.5 21.5 12 18 15.5"/>',
  sparkle: '<path d="M12 3c.6 4.2 1.8 5.4 6 6-4.2.6-5.4 1.8-6 6-.6-4.2-1.8-5.4-6-6 4.2-.6 5.4-1.8 6-6z"/>',
  dot: '<circle cx="12" cy="12" r="5" fill="currentColor" stroke="none"/>',
  layers: '<path d="m12 3 9 5-9 5-9-5 9-5z"/><path d="m3 12 9 5 9-5"/><path d="m3 8.5 9 5 9-5"/>',
}

export function Icon({
  name,
  size = 18,
  ...rest
}: { name: IconName; size?: number } & SVGProps<SVGSVGElement>) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.6}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      focusable="false"
      dangerouslySetInnerHTML={{ __html: PATHS[name] }}
      {...rest}
    />
  )
}
