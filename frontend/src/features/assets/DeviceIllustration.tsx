import {
  Computer,
  Headphones,
  HardDrive,
  Keyboard,
  LampDesk,
  Laptop,
  Monitor,
  MonitorPlay,
  Package,
  PcCase,
  Presentation,
  Printer,
  Projector,
  Router,
  Server,
  Smartphone,
  Tablet,
  Tv,
  Webcam,
  type LucideIcon,
} from 'lucide-react';
import { useId, type ReactNode } from 'react';

import { cn } from '@/lib/cn';

import { DEVICE_KIND_LABELS, type DeviceKind } from './deviceKind';

/*
 * Drawn here rather than fetched: product photos belong to their makers, and a
 * consistent set reads better than a mix of catalogue shots. Every colour is a
 * theme role, so the drawings follow light and dark mode like the rest of the UI.
 */

const SHELL = 'fill-device-shell stroke-device-edge';
const SHADE = 'fill-device-shell-shade stroke-device-edge';
const LIGHT = 'fill-device-shell-light stroke-device-edge';
const DETAIL = 'fill-device-detail';
const SLOT = 'fill-device-screen';
const PAPER = 'fill-device-paper stroke-device-edge';

interface ArtProps {
  /** `url(#…)` of the screen-glow gradient defined once per drawing. */
  glow: string;
}

function Shadow({ cx = 100, rx }: { cx?: number; rx: number }) {
  return <ellipse cx={cx} cy={136} rx={rx} ry={5} className="fill-device-shadow" />;
}

/** Dark glass with a lit corner and a soft diagonal reflection. */
function Screen({
  x,
  y,
  w,
  h,
  r = 2,
  glow,
  children,
}: {
  x: number;
  y: number;
  w: number;
  h: number;
  r?: number;
  glow: string;
  children?: ReactNode;
}) {
  return (
    <g>
      <rect x={x} y={y} width={w} height={h} rx={r} className={SLOT} />
      <rect x={x} y={y} width={w} height={h} rx={r} fill={glow} />
      {children}
      <path
        d={`M${x + w * 0.42} ${y} h${w * 0.2} l${-w * 0.3} ${h} h${-w * 0.2} z`}
        className="fill-white"
        opacity={0.05}
      />
    </g>
  );
}

function MonitorArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={48} />
      <rect x={66} y={124} width={68} height={8} rx={4} className={LIGHT} />
      <rect x={92} y={100} width={16} height={26} rx={2} className={SHADE} />
      <rect x={26} y={16} width={148} height={90} rx={6} className={SHELL} />
      <Screen x={32} y={22} w={136} h={76} glow={glow} />
      <circle cx={100} cy={102} r={1.3} className={DETAIL} />
    </>
  );
}

function DesktopArt() {
  return (
    <>
      <Shadow cx={110} rx={46} />
      <path d="M128 22 L160 12 V116 L128 128 Z" className={SHADE} />
      <path d="M76 22 L108 12 H160 L128 22 Z" className={LIGHT} />
      <rect x={76} y={22} width={52} height={106} rx={3} className={SHELL} />
      <rect x={84} y={32} width={36} height={6} rx={1.5} className={SLOT} />
      <circle cx={102} cy={52} r={5.5} className={LIGHT} />
      <circle cx={102} cy={52} r={2} className="fill-accent" />
      <rect x={87} y={64} width={8} height={3} rx={1} className={SLOT} />
      <rect x={99} y={64} width={8} height={3} rx={1} className={SLOT} />
      <circle cx={114} cy={65.5} r={1.6} className={SLOT} />
      {[94, 100, 106, 112, 118].map((y) => (
        <rect key={y} x={84} y={y} width={36} height={2} rx={1} className={DETAIL} />
      ))}
      <path d="M136 52 L152 47 V91 L136 96 Z" className={DETAIL} opacity={0.55} />
    </>
  );
}

function LaptopArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={80} />
      <rect x={44} y={18} width={112} height={78} rx={6} className={SHELL} />
      <Screen x={50} y={24} w={100} h={66} glow={glow} />
      <circle cx={100} cy={21} r={1} className={DETAIL} />
      <path d="M42 96 H158 L178 112 H22 Z" className={LIGHT} />
      <path d="M56 99 H144 L153 107 H47 Z" className={DETAIL} opacity={0.7} />
      <rect x={22} y={112} width={156} height={6} rx={3} className={SHADE} />
      <rect x={88} y={112} width={24} height={2.5} rx={1.25} className={DETAIL} />
    </>
  );
}

function TabletArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={44} />
      <g transform="rotate(-8 100 72)">
        <rect x={64} y={12} width={72} height={116} rx={10} className={SHELL} />
        <Screen x={69} y={17} w={62} h={106} r={5} glow={glow} />
        <circle cx={100} cy={14.6} r={1} className={DETAIL} />
      </g>
      {/* A stylus beside it: the quickest way to say "tablet" rather than "phone". */}
      <g transform="rotate(14 150 80)">
        <rect x={147} y={32} width={6} height={88} rx={3} className={LIGHT} />
        <path d="M147 118 L150 128 L153 118 Z" className={DETAIL} />
      </g>
    </>
  );
}

function PhoneArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={28} />
      <rect x={122} y={40} width={2} height={16} rx={1} className={SHADE} />
      <rect x={78} y={14} width={44} height={112} rx={10} className={SHELL} />
      <Screen x={82} y={18} w={36} h={104} r={7} glow={glow} />
      <rect x={93} y={22} width={14} height={4} rx={2} className={SLOT} />
    </>
  );
}

function AllInOneArt({ glow }: ArtProps) {
  // Told apart from a plain monitor by the deep pale chin that holds the
  // computer, and by the keyboard and mouse in front of it.
  return (
    <>
      <Shadow rx={72} />
      <path d="M88 96 L84 116 H116 L112 96 Z" className={SHADE} />
      <rect x={74} y={114} width={52} height={4} rx={2} className={LIGHT} />
      <rect x={34} y={10} width={132} height={86} rx={7} className={SHELL} />
      <Screen x={40} y={16} w={120} h={60} glow={glow} />
      <path d="M34 78 H166 V89 Q166 96 159 96 H41 Q34 96 34 89 Z" className={LIGHT} />
      <circle cx={100} cy={87} r={2.2} className={DETAIL} />
      <path d="M46 121 H150 L157 130 H39 Z" className={LIGHT} />
      <rect x={52} y={124} width={92} height={3} rx={1.5} className={DETAIL} />
      <ellipse cx={174} cy={125} rx={6} ry={8} className={SHELL} />
    </>
  );
}

function InteractivePanelArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={72} />
      <rect x={95} y={104} width={10} height={20} className={SHADE} />
      <rect x={50} y={122} width={100} height={6} rx={3} className={SHADE} />
      <circle cx={56} cy={131} r={4} className={SHELL} />
      <circle cx={144} cy={131} r={4} className={SHELL} />
      <rect x={14} y={10} width={172} height={96} rx={4} className={SHELL} />
      <Screen x={20} y={16} w={160} h={80} glow={glow}>
        {/* Handwriting on the glass: this is a board you draw on. */}
        <path
          d="M36 70 C48 44 62 82 78 56 S104 70 116 46"
          fill="none"
          className="stroke-white"
          strokeOpacity={0.75}
          strokeWidth={2.5}
          strokeLinecap="round"
        />
        <circle
          cx={146}
          cy={58}
          r={13}
          fill="none"
          className="stroke-accent"
          strokeWidth={2.5}
        />
        <path
          d="M40 84 H92"
          className="stroke-white"
          strokeOpacity={0.4}
          strokeWidth={2}
          strokeLinecap="round"
        />
      </Screen>
      <rect x={30} y={99} width={30} height={3} rx={1.5} className={DETAIL} />
      <rect x={140} y={99} width={30} height={3} rx={1.5} className={DETAIL} />
      {[95, 100, 105].map((cx) => (
        <circle key={cx} cx={cx} cy={100.5} r={1.3} className={DETAIL} />
      ))}
    </>
  );
}

function TvArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={74} />
      <path d="M46 112 L36 128 H47 L55 112 Z" className={SHADE} />
      <path d="M145 112 L153 128 H164 L154 112 Z" className={SHADE} />
      <rect x={18} y={22} width={164} height={92} rx={3} className={SHELL} />
      <Screen x={21} y={25} w={158} h={84} r={1} glow={glow} />
      <rect x={95} y={110.5} width={10} height={1.6} rx={0.8} className={DETAIL} />
    </>
  );
}

function SignageArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={34} />
      <rect x={74} y={124} width={52} height={8} rx={3} className={LIGHT} />
      <rect x={95} y={104} width={10} height={22} className={SHADE} />
      <rect x={66} y={8} width={68} height={100} rx={4} className={SHELL} />
      <Screen x={70} y={12} w={60} h={92} glow={glow}>
        {/* Notice-board content: a heading, a picture, a few lines. */}
        <rect x={76} y={19} width={48} height={9} rx={2} className="fill-white" opacity={0.35} />
        <rect x={76} y={34} width={40} height={3} rx={1.5} className="fill-white" opacity={0.25} />
        <rect x={76} y={41} width={30} height={3} rx={1.5} className="fill-white" opacity={0.25} />
        <rect x={76} y={51} width={48} height={28} rx={2} className="fill-accent" opacity={0.55} />
        <rect x={76} y={85} width={44} height={3} rx={1.5} className="fill-white" opacity={0.25} />
        <rect x={76} y={92} width={34} height={3} rx={1.5} className="fill-white" opacity={0.25} />
      </Screen>
    </>
  );
}

function MiniPcArt() {
  return (
    <>
      <Shadow cx={104} rx={56} />
      {/* Drawn at the same scale as the rest, it would be a speck; enlarged
          about its own centre instead, with the stroke thinned to match. */}
      <g transform="translate(-31.2 -22) scale(1.3)" strokeWidth={1.15}>
        <path d="M60 74 L82 62 H150 L128 74 Z" className={LIGHT} />
        <path d="M128 74 L150 62 V96 L128 108 Z" className={SHADE} />
        <rect x={60} y={74} width={68} height={34} rx={4} className={SHELL} />
        <circle cx={72} cy={91} r={3.6} className={LIGHT} />
        <circle cx={72} cy={91} r={1.5} className="fill-accent" />
        <rect x={84} y={89} width={9} height={3.4} rx={0.8} className={SLOT} />
        <rect x={97} y={89} width={9} height={3.4} rx={0.8} className={SLOT} />
        <circle cx={114} cy={90.7} r={1.7} className={SLOT} />
        <path d="M92 66 L98 63 H134 L128 66 Z" className={DETAIL} opacity={0.6} />
      </g>
    </>
  );
}

function ProjectorArt({ glow }: ArtProps) {
  return (
    <>
      {/* The beam, faint, so it reads as light rather than an object. */}
      <path d="M58 82 L2 58 V122 L58 98 Z" className="fill-accent" opacity={0.08} />
      <Shadow cx={106} rx={62} />
      <rect x={52} y={110} width={8} height={7} rx={2} className={SHADE} />
      <rect x={136} y={110} width={8} height={7} rx={2} className={SHADE} />
      <path d="M40 66 L58 52 H170 L152 66 Z" className={LIGHT} />
      <path d="M152 66 L170 52 V96 L152 112 Z" className={SHADE} />
      <rect x={40} y={66} width={112} height={46} rx={5} className={SHELL} />
      <circle cx={70} cy={89} r={15} className={SHADE} />
      <circle cx={70} cy={89} r={10} className={SLOT} />
      <circle cx={70} cy={89} r={10} fill={glow} />
      <circle cx={66} cy={85} r={3} className="fill-white" opacity={0.35} />
      {[78, 84, 90, 96, 102].map((y) => (
        <rect key={y} x={100} y={y} width={40} height={2} rx={1} className={DETAIL} />
      ))}
    </>
  );
}

function VisualiserArt() {
  return (
    <>
      <Shadow rx={62} />
      {/* Light from the head onto the page it is filming. */}
      <path d="M118 49 L134 49 L154 114 L98 114 Z" className="fill-accent" opacity={0.1} />
      <rect x={42} y={114} width={116} height={14} rx={6} className={SHELL} />
      <path d="M98 110 L152 110 L157 116 L93 116 Z" className={PAPER} />
      <rect x={66} y={38} width={9} height={80} rx={4.5} className={SHADE} />
      <rect x={64} y={28} width={78} height={18} rx={9} className={SHELL} />
      <ellipse cx={126} cy={47} rx={9} ry={3} className={SLOT} />
      <circle cx={76} cy={37} r={1.8} className="fill-accent" />
    </>
  );
}

function PrinterArt() {
  return (
    <>
      <Shadow rx={68} />
      <rect x={62} y={18} width={76} height={42} rx={1.5} className={PAPER} />
      {[28, 34, 40].map((y, index) => (
        <rect
          key={y}
          x={70}
          y={y}
          width={[52, 44, 56][index]}
          height={2.5}
          rx={1.25}
          className={DETAIL}
        />
      ))}
      <rect x={30} y={52} width={140} height={60} rx={8} className={SHELL} />
      <rect x={34} y={64} width={132} height={1.6} rx={0.8} className={DETAIL} />
      <rect x={126} y={71} width={32} height={13} rx={2} className={SLOT} />
      <rect x={129} y={74} width={14} height={2.5} rx={1} className="fill-accent" opacity={0.8} />
      <circle cx={117} cy={74} r={2.2} className={DETAIL} />
      <circle cx={117} cy={81} r={2.2} className={DETAIL} />
      <rect x={46} y={92} width={108} height={4} rx={2} className={SLOT} />
      <path d="M54 96 H146 L152 112 H48 Z" className={PAPER} />
      <rect x={40} y={112} width={120} height={6} rx={3} className={SHADE} />
    </>
  );
}

function ServerArt() {
  return (
    <>
      <Shadow cx={106} rx={64} />
      <path d="M38 26 L52 16 H176 L162 26 Z" className={LIGHT} />
      <path d="M162 26 L176 16 V108 L162 118 Z" className={SHADE} />
      {[26, 57, 88].map((y) => (
        <g key={y}>
          <rect x={38} y={y} width={124} height={30} rx={3} className={SHELL} />
          <rect x={42} y={y + 6} width={4} height={18} rx={2} className={SHADE} />
          <rect x={154} y={y + 6} width={4} height={18} rx={2} className={SHADE} />
          {[54, 72, 90, 108].map((x) => (
            <rect key={x} x={x} y={y + 7} width={14} height={16} rx={1.5} className={SHADE} />
          ))}
          <circle cx={140} cy={y + 11} r={1.8} className="fill-success" />
          <circle cx={140} cy={y + 19} r={1.8} className="fill-accent" />
        </g>
      ))}
    </>
  );
}

function NetworkArt() {
  return (
    <>
      <Shadow cx={102} rx={66} />
      <rect
        x={54}
        y={22}
        width={7}
        height={54}
        rx={3.5}
        className={SHADE}
        transform="rotate(-12 57 76)"
      />
      <rect
        x={138}
        y={22}
        width={7}
        height={54}
        rx={3.5}
        className={SHADE}
        transform="rotate(12 141 76)"
      />
      <path d="M30 78 L44 66 H170 L156 78 Z" className={LIGHT} />
      <path d="M156 78 L170 66 V90 L156 104 Z" className={SHADE} />
      <rect x={30} y={78} width={126} height={26} rx={3} className={SHELL} />
      {[0, 1, 2].map((index) => (
        <circle key={index} cx={42 + index * 7} cy={91} r={1.7} className="fill-success" />
      ))}
      {[0, 1, 2, 3, 4].map((index) => (
        <rect
          key={index}
          x={84 + index * 13}
          y={86}
          width={10}
          height={9}
          rx={1.2}
          className={SLOT}
        />
      ))}
    </>
  );
}

function CameraArt({ glow }: ArtProps) {
  return (
    <>
      <Shadow rx={40} />
      <path d="M90 80 L86 104 H114 L110 80 Z" className={SHADE} />
      <rect x={70} y={102} width={60} height={9} rx={4.5} className={LIGHT} />
      <rect x={52} y={38} width={96} height={44} rx={22} className={SHELL} />
      <circle cx={100} cy={60} r={16} className={SHADE} />
      <circle cx={100} cy={60} r={10.5} className={SLOT} />
      <circle cx={100} cy={60} r={10.5} fill={glow} />
      <circle cx={96} cy={56} r={3} className="fill-white" opacity={0.35} />
      <circle cx={130} cy={52} r={2.2} className="fill-success" />
    </>
  );
}

function AudioArt() {
  return (
    <>
      <Shadow rx={52} />
      <path
        d="M52 94 V74 C52 44 74 26 100 26 C126 26 148 44 148 74 V94"
        fill="none"
        className="stroke-device-edge"
        strokeWidth={12}
        strokeLinecap="round"
      />
      <path
        d="M52 94 V74 C52 44 74 26 100 26 C126 26 148 44 148 74 V94"
        fill="none"
        className="stroke-device-shell-shade"
        strokeWidth={9}
        strokeLinecap="round"
      />
      <rect x={36} y={80} width={28} height={44} rx={13} className={SHELL} />
      <rect x={136} y={80} width={28} height={44} rx={13} className={SHELL} />
      <rect x={58} y={84} width={9} height={36} rx={4.5} className={SHADE} />
      <rect x={133} y={84} width={9} height={36} rx={4.5} className={SHADE} />
    </>
  );
}

function PeripheralArt() {
  const rows = [0, 1, 2];
  return (
    <>
      <Shadow cx={96} rx={80} />
      <path d="M28 84 H150 L160 112 H18 Z" className={LIGHT} />
      {rows.map((row) =>
        Array.from({ length: 11 }, (_, index) => (
          <rect
            key={`${row}-${index}`}
            x={34 - row * 3 + index * 10.6}
            y={88 + row * 6.5}
            width={8}
            height={4.2}
            rx={1}
            className={SHADE}
          />
        )),
      )}
      <rect x={60} y={107} width={52} height={3.6} rx={1} className={SHADE} />
      <rect x={18} y={112} width={142} height={6} rx={3} className={SHADE} />
      <ellipse cx={180} cy={104} rx={11} ry={15} className={SHELL} />
      <path d="M180 89 V101" className="stroke-device-edge" />
    </>
  );
}

function GenericArt() {
  return (
    <>
      <Shadow cx={104} rx={50} />
      <path d="M56 50 L84 36 H156 L128 50 Z" className={LIGHT} />
      <path d="M128 50 L156 36 V96 L128 112 Z" className={SHADE} />
      <rect x={56} y={50} width={72} height={62} rx={2} className={SHELL} />
      <path d="M84 50 L112 36 H124 L96 50 Z" className={DETAIL} />
      <rect x={66} y={62} width={32} height={20} rx={2} className={PAPER} />
      <rect x={70} y={67} width={20} height={2.2} rx={1.1} className={DETAIL} />
      <rect x={70} y={73} width={14} height={2.2} rx={1.1} className={DETAIL} />
    </>
  );
}

const ART: Record<DeviceKind, (props: ArtProps) => ReactNode> = {
  desktop: DesktopArt,
  laptop: LaptopArt,
  tablet: TabletArt,
  phone: PhoneArt,
  monitor: MonitorArt,
  allInOne: AllInOneArt,
  interactivePanel: InteractivePanelArt,
  tv: TvArt,
  signage: SignageArt,
  miniPc: MiniPcArt,
  projector: ProjectorArt,
  visualiser: VisualiserArt,
  printer: PrinterArt,
  server: ServerArt,
  network: NetworkArt,
  camera: CameraArt,
  audio: AudioArt,
  peripheral: PeripheralArt,
  generic: GenericArt,
};

/** A drawing of the kind of device an asset is, for detail headers and previews. */
export function DeviceIllustration({
  kind,
  className,
}: {
  kind: DeviceKind;
  className?: string;
}) {
  // Gradient ids must be unique per drawing, and `useId` output is not a valid
  // bare fragment in every engine, so it is reduced to safe characters.
  const id = `device-glow-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`;
  const Art = ART[kind];

  return (
    <svg
      viewBox="0 0 200 150"
      role="img"
      aria-label={`Illustration of a ${DEVICE_KIND_LABELS[kind].toLowerCase()}`}
      className={cn('h-auto w-full', className)}
    >
      <defs>
        <linearGradient id={id} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" style={{ stopColor: 'var(--accent)', stopOpacity: 0.55 }} />
          <stop offset="0.55" style={{ stopColor: 'var(--accent)', stopOpacity: 0.14 }} />
          <stop offset="1" style={{ stopColor: 'var(--accent)', stopOpacity: 0 }} />
        </linearGradient>
      </defs>
      <g strokeWidth={1.5} strokeLinejoin="round">
        <Art glow={`url(#${id})`} />
      </g>
    </svg>
  );
}

const GLYPHS: Record<DeviceKind, LucideIcon> = {
  desktop: PcCase,
  laptop: Laptop,
  tablet: Tablet,
  phone: Smartphone,
  monitor: Monitor,
  allInOne: Computer,
  interactivePanel: Presentation,
  tv: Tv,
  signage: MonitorPlay,
  miniPc: HardDrive,
  projector: Projector,
  visualiser: LampDesk,
  printer: Printer,
  server: Server,
  network: Router,
  camera: Webcam,
  audio: Headphones,
  peripheral: Keyboard,
  generic: Package,
};

/**
 * The same kind as a small icon, for table rows. The full drawing turns to mush
 * at row height; a line icon stays crisp.
 */
export function DeviceGlyph({ kind, className }: { kind: DeviceKind; className?: string }) {
  const Icon = GLYPHS[kind];
  return (
    <span
      className={cn(
        'flex size-9 shrink-0 items-center justify-center rounded-md bg-surface-sunken text-text-muted',
        className,
      )}
      title={DEVICE_KIND_LABELS[kind]}
    >
      <Icon className="size-[1.125rem]" aria-hidden />
      <span className="sr-only">{DEVICE_KIND_LABELS[kind]}</span>
    </span>
  );
}
