/**
 * What kind of physical thing an asset is, so it can be drawn.
 *
 * There is no device-type field on an asset: the category is free text chosen
 * per school ("Desktops", "Interactive Whiteboards", "Label printing test
 * devices"), so the kind is inferred from the words the record already holds.
 * The model is checked before the category because it is the more specific
 * fact — a ThinkPad is a laptop whatever category it was filed under, and a
 * Gigabyte BRIX in "Signage" is a mini PC, not a screen.
 */
export const DEVICE_KINDS = [
  'desktop',
  'laptop',
  'tablet',
  'phone',
  'monitor',
  'allInOne',
  'interactivePanel',
  'tv',
  'signage',
  'miniPc',
  'projector',
  'visualiser',
  'printer',
  'server',
  'network',
  'camera',
  'audio',
  'peripheral',
  'generic',
] as const;

export type DeviceKind = (typeof DEVICE_KINDS)[number];

export const DEVICE_KIND_LABELS: Record<DeviceKind, string> = {
  desktop: 'Desktop PC',
  laptop: 'Laptop',
  tablet: 'Tablet',
  phone: 'Phone',
  monitor: 'Monitor',
  allInOne: 'All-in-one PC',
  interactivePanel: 'Interactive panel',
  tv: 'Television',
  signage: 'Digital signage',
  miniPc: 'Mini PC',
  projector: 'Projector',
  visualiser: 'Visualiser',
  printer: 'Printer',
  server: 'Server',
  network: 'Network device',
  camera: 'Camera',
  audio: 'Audio',
  peripheral: 'Peripheral',
  generic: 'Equipment',
};

export interface DeviceKindSource {
  category?: { name: string; code: string } | null;
  manufacturer?: string | null;
  model?: string | null;
  deviceName?: string | null;
  operatingSystem?: string | null;
}

type Rule = readonly [DeviceKind, RegExp];

/** Product names that settle the question on their own. Order matters. */
const MODEL_RULES: readonly Rule[] = [
  ['tablet', /\bipad\b|galaxy tab|\btab [a-z]?\d|surface (pro|go)|\bkindle\b|\bfire hd\b|\bmediapad\b|\btablet\b/],
  ['phone', /\biphone\b|\bpixel \d|galaxy [asz]\d|\bphone\b|\bsmartphone\b/],
  ['allInOne', /\bimac\b|all[- ]?in[- ]?one|\baio\b|optiplex .*aio|ideacentre aio/],
  ['laptop', /\bmacbook\b|\bthinkpad\b|\blatitude\b|\belitebook\b|\bprobook\b|\bzbook\b|\bvostro\b|\binspiron\b|\bxps\b|\bideapad\b|\bchromebook\b|\bnotebook\b|\blaptop\b|\bsurface laptop\b|\b2[45]0 g\d|\b25[05]\/25[05]\b|\btravelmate\b|\baspire\b|\bzenbook\b|\bvivobook\b/],
  ['miniPc', /\bbrix\b|\bnuc\b|\bmini ?pc\b|\btiny\b|\bmicro\b|\bmac mini\b|\bthinkcentre m\d+q\b|\bdesk mini\b/],
  ['interactivePanel', /activpanel|activboard|clevertouch|smart ?board|\bsmart mx\b|newline|\bboxlight\b|interactive|promethean/],
  ['visualiser', /visuali[sz]er|document camera|\bdoc ?cam\b|\belmo\b|\bipevo\b|\bhue hd\b/],
  ['projector', /projector|\bepson eb|\bbenq m[swx]|\boptoma\b|\bbeamer\b/],
  ['printer', /printer|laserjet|officejet|deskjet|\bmfp\b|\bmfc-|\bhl-l|\bcopier\b|\bphotocopier\b|\bplotter\b|\bzebra\b|\bdymo\b|\bkyocera\b|\becosys\b/],
  ['server', /\bserver\b|poweredge|proliant|\bnas\b|synology|qnap|\brack\b|\bblade\b/],
  ['network', /\bswitch\b|\brouter\b|access point|\bwap\b|firewall|\bunifi\b|\bmeraki\b|\baruba\b|\bcisco\b|\bfortigate\b|\bnetgear\b|\bubiquiti\b|\bmodem\b/],
  ['monitor', /\bmonitor\b|\bp2[0-9]{3}h?\b|\be2[0-9]{3}h?\b|\bu2[0-9]{3}\b|ultrasharp|\bproart\b/],
  ['tv', /\btv\b|television|\b\d{2}u[a-z]\d{3}|\bbravia\b|\bqled\b|\boled\b/],
  ['camera', /webcam|\bcctv\b|\bcamera\b|\bgopro\b|\bdslr\b|\bcamcorder\b|\bptz\b/],
  ['audio', /headphone|headset|speaker|soundbar|microphone|\bmic\b|\bamplifier\b|\bmixer\b|\bairpods\b/],
  ['peripheral', /keyboard|\bmouse\b|\bdock\b|docking|\bcharger\b|\bwacom\b|\bstylus\b|\bscanner\b|\bhub\b|\badapter\b/],
  ['desktop', /optiplex|prodesk|elitedesk|thinkcentre|\bveriton\b|\bdesktop\b|\btower\b|\bpc\b|\bworkstation\b|\bmac pro\b|\bboamot\b|\bstonepc\b/],
];

/** Category wording, consulted only when the model said nothing useful. */
const CATEGORY_RULES: readonly Rule[] = [
  ['interactivePanel', /interactive|whiteboard|smart ?board|activ/],
  ['signage', /signage|display board|digital sign/],
  ['visualiser', /visuali[sz]er|document camera/],
  ['allInOne', /all[- ]?in[- ]?one|\baio\b/],
  ['laptop', /laptop|notebook|chromebook|macbook/],
  ['tablet', /tablet|ipad/],
  ['phone', /phone|mobile/],
  ['desktop', /desktop|\bpcs?\b|computer|workstation|tower/],
  ['miniPc', /mini ?pc|thin client|\bnuc\b/],
  ['monitor', /monitor|screen|display/],
  ['tv', /\btvs?\b|television/],
  ['projector', /projector/],
  ['printer', /printer|copier|print/],
  ['server', /server|storage|\bnas\b/],
  ['network', /network|switch|router|wireless|wi-?fi|access point|firewall/],
  ['camera', /camera|cctv|webcam/],
  ['audio', /audio|speaker|headphone|headset|sound|microphone/],
  ['peripheral', /keyboard|mouse|peripheral|accessor|dock|charger|cable/],
];

/** The OS gives the game away for devices whose model is blank. */
const OS_RULES: readonly Rule[] = [
  ['tablet', /ipados/],
  ['phone', /\bios\b|android/],
  ['laptop', /chrome ?os/],
];

function firstMatch(rules: readonly Rule[], text: string): DeviceKind | null {
  for (const [kind, pattern] of rules) {
    if (pattern.test(text)) return kind;
  }
  return null;
}

export function deviceKindFor(source: DeviceKindSource): DeviceKind {
  const product = [source.manufacturer, source.model, source.deviceName]
    .filter(Boolean)
    .join(' ')
    .toLowerCase();
  const category = [source.category?.name, source.category?.code]
    .filter(Boolean)
    .join(' ')
    .toLowerCase()
    // Codes like INTERACTIVE-WHITEBOARDS read as words once hyphens are spaces.
    .replace(/[-_]/g, ' ');
  const os = (source.operatingSystem ?? '').toLowerCase();

  return (
    (product && firstMatch(MODEL_RULES, product)) ||
    (category && firstMatch(CATEGORY_RULES, category)) ||
    (os && firstMatch(OS_RULES, os)) ||
    'generic'
  );
}
