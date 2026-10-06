import { useMemo } from 'react';

import { encodeQr, qrToSvgPath } from '@/lib/qr';

export interface QrCodeProps {
  /** The text to encode. */
  value: string;
  /**
   * Announced to screen readers. Say what the code *is* — its contents are
   * usually a secret and never worth reading out.
   */
  label: string;
  /** Rendered width and height in CSS pixels. */
  size?: number;
  className?: string;
}

/** The four-module margin the standard requires; scanners rely on it. */
const QUIET_ZONE = 4;

/**
 * A QR code, rendered as SVG.
 *
 * Deliberately always dark-on-white with a solid light background, in both
 * themes. Almost every scanner assumes dark modules on a light field, and an
 * inverted code — or one whose quiet zone shows the page behind it — is either
 * unreadable or slow enough to feel broken. This is one of the few places the
 * design tokens are the wrong answer.
 */
export function QrCode({ value, label, size = 208, className }: QrCodeProps) {
  const path = useMemo(() => {
    const code = encodeQr(value);
    return { d: qrToSvgPath(code), extent: code.size + QUIET_ZONE * 2 };
  }, [value]);

  return (
    <svg
      role="img"
      aria-label={label}
      width={size}
      height={size}
      viewBox={`0 0 ${path.extent} ${path.extent}`}
      // Modules are whole units in user space, so this keeps their edges hard
      // instead of letting the renderer antialias them into grey.
      shapeRendering="crispEdges"
      className={className}
    >
      <rect width={path.extent} height={path.extent} fill="#ffffff" />
      <g transform={`translate(${QUIET_ZONE} ${QUIET_ZONE})`}>
        <path d={path.d} fill="#000000" />
      </g>
    </svg>
  );
}
