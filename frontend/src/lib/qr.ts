/**
 * A QR Code encoder, byte mode, error-correction level M.
 *
 * Written out rather than pulled from npm because the only thing this app needs
 * to encode is a short `otpauth://` URI on one screen, and a dependency that
 * renders a *slightly* wrong QR fails in the worst way available: it looks
 * plausible, and nobody finds out until someone cannot enrol.
 *
 * Scope is deliberately narrow. Byte mode covers any UTF-8 text, level M is the
 * conventional choice for on-screen codes, and versions stop at 20 (97×97
 * modules, 669 data codewords) because nothing here comes close to that. Asking
 * for more throws rather than silently truncating.
 *
 * Reference: ISO/IEC 18004. The block, alignment and remainder tables below are
 * from that standard; `qr.test.ts` asserts their internal arithmetic so a typo
 * in one cannot pass unnoticed.
 */

/** Level M: recovers roughly 15% damage. Encoded as `0b00` in the format bits. */
const ECC_FORMAT_BITS = 0b00;

const MAX_VERSION = 20;

/**
 * Per version: EC codewords per block, then the two block groups as
 * `[blockCount, dataCodewordsPerBlock]`. Group 2 is `[0, 0]` when a version
 * splits evenly.
 *
 * Total codewords for a version must equal
 * `g1Blocks * g1Data + g2Blocks * g2Data + (g1Blocks + g2Blocks) * ecPerBlock`,
 * which the tests check for every row.
 */
const BLOCKS: Record<number, readonly [number, number, number, number, number]> = {
  1: [10, 1, 16, 0, 0],
  2: [16, 1, 28, 0, 0],
  3: [26, 1, 44, 0, 0],
  4: [18, 2, 32, 0, 0],
  5: [24, 2, 43, 0, 0],
  6: [16, 4, 27, 0, 0],
  7: [18, 4, 31, 0, 0],
  8: [22, 2, 38, 2, 39],
  9: [22, 3, 36, 2, 37],
  10: [26, 4, 43, 1, 44],
  11: [30, 1, 50, 4, 51],
  12: [22, 6, 36, 2, 37],
  13: [22, 8, 37, 1, 38],
  14: [24, 4, 40, 5, 41],
  15: [24, 5, 41, 5, 42],
  16: [28, 7, 45, 3, 46],
  17: [28, 10, 46, 1, 47],
  18: [26, 9, 43, 4, 44],
  19: [26, 3, 44, 11, 45],
  20: [26, 3, 41, 13, 42],
};

/** Row and column centres of the alignment patterns, by version. */
const ALIGNMENT_CENTRES: Record<number, readonly number[]> = {
  1: [],
  2: [6, 18],
  3: [6, 22],
  4: [6, 26],
  5: [6, 30],
  6: [6, 34],
  7: [6, 22, 38],
  8: [6, 24, 42],
  9: [6, 26, 46],
  10: [6, 28, 50],
  11: [6, 30, 54],
  12: [6, 32, 58],
  13: [6, 34, 62],
  14: [6, 26, 46, 66],
  15: [6, 26, 48, 70],
  16: [6, 26, 50, 74],
  17: [6, 30, 54, 78],
  18: [6, 30, 56, 82],
  19: [6, 30, 58, 86],
  20: [6, 34, 62, 90],
};

/** Unused bits after the last codeword, which are left at zero. */
function remainderBits(version: number): number {
  if (version === 1) return 0;
  if (version <= 6) return 7;
  if (version <= 13) return 0;
  return 3; // versions 14–20
}

// ---------------------------------------------------------------------------
// GF(256) arithmetic, for Reed–Solomon
// ---------------------------------------------------------------------------

/**
 * Log and antilog tables over GF(256) with primitive polynomial 0x11D, the one
 * QR specifies. Multiplication becomes addition of logarithms, which keeps the
 * Reed–Solomon inner loop to table lookups.
 */
const EXP = new Uint8Array(512);
const LOG = new Uint8Array(256);
{
  let x = 1;
  for (let i = 0; i < 255; i++) {
    EXP[i] = x;
    LOG[x] = i;
    x <<= 1;
    if (x & 0x100) x ^= 0x11d;
  }
  // Duplicated so an index sum up to 508 needs no modulo.
  for (let i = 255; i < 512; i++) EXP[i] = EXP[i - 255]!;
}

function gfMultiply(a: number, b: number): number {
  if (a === 0 || b === 0) return 0;
  return EXP[LOG[a]! + LOG[b]!]!;
}

/** Generator polynomial for `degree` error-correction codewords. */
function generatorPolynomial(degree: number): Uint8Array {
  let result = new Uint8Array([1]);
  for (let i = 0; i < degree; i++) {
    // Multiply by (x - α^i); subtraction is XOR here, so the sign is irrelevant.
    const next = new Uint8Array(result.length + 1);
    for (let j = 0; j < result.length; j++) {
      next[j] = next[j]! ^ result[j]!;
      next[j + 1] = next[j + 1]! ^ gfMultiply(result[j]!, EXP[i]!);
    }
    result = next;
  }
  return result;
}

/** The remainder of `data · x^degree` divided by the generator polynomial. */
function errorCorrectionCodewords(data: Uint8Array, degree: number): Uint8Array {
  const generator = generatorPolynomial(degree);
  const remainder = new Uint8Array(degree);
  for (const byte of data) {
    const factor = byte ^ remainder[0]!;
    remainder.copyWithin(0, 1);
    remainder[degree - 1] = 0;
    for (let i = 0; i < degree; i++) {
      remainder[i] = remainder[i]! ^ gfMultiply(generator[i + 1]!, factor);
    }
  }
  return remainder;
}

// ---------------------------------------------------------------------------
// BCH codes, for the format and version information
// ---------------------------------------------------------------------------

/** Remainder of `data · x^degree` modulo `generator`, over GF(2). */
function bchRemainder(data: number, generator: number, degree: number): number {
  let value = data << degree;
  const generatorBits = 32 - Math.clz32(generator);
  for (let i = 32 - Math.clz32(value); i >= generatorBits; i--) {
    if (value & (1 << (i - 1))) value ^= generator << (i - generatorBits);
  }
  return value;
}

/**
 * The 15-bit format information: two bits of EC level, three of mask, a BCH
 * check, then a fixed XOR so that an all-zero format never reads as valid.
 *
 * Exported for `qr.test.ts`, which checks the resulting code words keep the
 * minimum Hamming distance the standard guarantees. Nothing else should need it.
 */
export function formatInformation(mask: number, eccBits: number = ECC_FORMAT_BITS): number {
  const data = (eccBits << 3) | mask;
  return ((data << 10) | bchRemainder(data, 0b10100110111, 10)) ^ 0b101010000010010;
}

/** The 18-bit version information, present only from version 7. Exported for tests. */
export function versionInformation(version: number): number {
  return (version << 12) | bchRemainder(version, 0b1111100100101, 12);
}

// ---------------------------------------------------------------------------
// Encoding
// ---------------------------------------------------------------------------

export interface QrCode {
  /** Width and height in modules, excluding the quiet zone. */
  size: number;
  version: number;
  /** `modules[y][x]` — true is a dark module. */
  modules: boolean[][];
}

/** Byte mode uses an 8-bit character count up to version 9, 16 bits after. */
function characterCountBits(version: number): number {
  return version <= 9 ? 8 : 16;
}

function dataCodewordCount(version: number): number {
  const [, g1Blocks, g1Data, g2Blocks, g2Data] = BLOCKS[version]!;
  return g1Blocks * g1Data + g2Blocks * g2Data;
}

function smallestVersionFor(byteLength: number): number {
  for (let version = 1; version <= MAX_VERSION; version++) {
    const capacity = dataCodewordCount(version) * 8;
    const needed = 4 + characterCountBits(version) + byteLength * 8;
    if (needed <= capacity) return version;
  }
  throw new RangeError(
    `${byteLength} bytes is too long for a version ${MAX_VERSION} QR code at error-correction level M.`,
  );
}

/** Accumulates a big-endian bit stream. */
class BitBuffer {
  private readonly bits: number[] = [];

  push(value: number, length: number): void {
    for (let i = length - 1; i >= 0; i--) this.bits.push((value >>> i) & 1);
  }

  get length(): number {
    return this.bits.length;
  }

  /** Pads to a whole number of bytes and returns them. */
  toBytes(): Uint8Array {
    const bytes = new Uint8Array(Math.ceil(this.bits.length / 8));
    this.bits.forEach((bit, index) => {
      if (bit) bytes[index >>> 3]! |= 0x80 >>> (index & 7);
    });
    return bytes;
  }
}

/** Mode indicator, length, payload, terminator and the alternating pad bytes. */
function buildDataCodewords(data: Uint8Array, version: number): Uint8Array {
  const capacity = dataCodewordCount(version);
  const buffer = new BitBuffer();
  buffer.push(0b0100, 4); // byte mode
  buffer.push(data.length, characterCountBits(version));
  for (const byte of data) buffer.push(byte, 8);

  // Terminator, truncated if the capacity does not allow all four bits.
  buffer.push(0, Math.min(4, capacity * 8 - buffer.length));
  // Then to a byte boundary.
  buffer.push(0, (8 - (buffer.length % 8)) % 8);

  const encoded = buffer.toBytes();
  const codewords = new Uint8Array(capacity);
  codewords.set(encoded);
  for (let i = encoded.length; i < capacity; i++) {
    // The standard's alternating filler, chosen because it produces no long runs.
    // The alternation starts at the first pad byte, not at an even absolute
    // index — getting that wrong only shows up when the data length is odd.
    codewords[i] = (i - encoded.length) % 2 === 0 ? 0xec : 0x11;
  }
  return codewords;
}

/**
 * Splits the data into blocks, computes each block's error correction, and
 * interleaves both — which is what makes the format survive a localised smudge
 * rather than losing one whole block.
 */
function interleave(codewords: Uint8Array, version: number): Uint8Array {
  const [ecPerBlock, g1Blocks, g1Data, g2Blocks, g2Data] = BLOCKS[version]!;
  const blocks: { data: Uint8Array; ec: Uint8Array }[] = [];

  let offset = 0;
  for (let i = 0; i < g1Blocks + g2Blocks; i++) {
    const length = i < g1Blocks ? g1Data : g2Data;
    const data = codewords.subarray(offset, offset + length);
    offset += length;
    blocks.push({ data, ec: errorCorrectionCodewords(data, ecPerBlock) });
  }

  const result: number[] = [];
  const longestData = Math.max(g1Data, g2Data);
  for (let i = 0; i < longestData; i++) {
    for (const block of blocks) {
      if (i < block.data.length) result.push(block.data[i]!);
    }
  }
  for (let i = 0; i < ecPerBlock; i++) {
    for (const block of blocks) result.push(block.ec[i]!);
  }
  return new Uint8Array(result);
}

// ---------------------------------------------------------------------------
// Matrix construction
// ---------------------------------------------------------------------------

class Matrix {
  readonly size: number;
  readonly modules: boolean[][];
  /** Function patterns must not be overwritten by data or by masking. */
  readonly reserved: boolean[][];

  constructor(version: number) {
    this.size = version * 4 + 17;
    this.modules = Array.from({ length: this.size }, () =>
      new Array<boolean>(this.size).fill(false),
    );
    this.reserved = Array.from({ length: this.size }, () =>
      new Array<boolean>(this.size).fill(false),
    );
  }

  set(x: number, y: number, dark: boolean): void {
    this.modules[y]![x] = dark;
    this.reserved[y]![x] = true;
  }
}

function drawFinder(matrix: Matrix, centreX: number, centreY: number): void {
  // One pass covers the 7×7 finder and its separator: rings at Chebyshev
  // distance 2 and 4 are light, everything else in the 9×9 is dark.
  for (let dy = -4; dy <= 4; dy++) {
    for (let dx = -4; dx <= 4; dx++) {
      const x = centreX + dx;
      const y = centreY + dy;
      if (x < 0 || x >= matrix.size || y < 0 || y >= matrix.size) continue;
      const ring = Math.max(Math.abs(dx), Math.abs(dy));
      matrix.set(x, y, ring !== 2 && ring !== 4);
    }
  }
}

function drawAlignment(matrix: Matrix, centreX: number, centreY: number): void {
  for (let dy = -2; dy <= 2; dy++) {
    for (let dx = -2; dx <= 2; dx++) {
      matrix.set(centreX + dx, centreY + dy, Math.max(Math.abs(dx), Math.abs(dy)) !== 1);
    }
  }
}

function drawFunctionPatterns(matrix: Matrix, version: number): void {
  const { size } = matrix;

  for (let i = 0; i < size; i++) {
    matrix.set(6, i, i % 2 === 0);
    matrix.set(i, 6, i % 2 === 0);
  }

  drawFinder(matrix, 3, 3);
  drawFinder(matrix, size - 4, 3);
  drawFinder(matrix, 3, size - 4);

  const centres = ALIGNMENT_CENTRES[version]!;
  for (let i = 0; i < centres.length; i++) {
    for (let j = 0; j < centres.length; j++) {
      // The three corners already hold finder patterns.
      const atCorner =
        (i === 0 && j === 0) ||
        (i === 0 && j === centres.length - 1) ||
        (i === centres.length - 1 && j === 0);
      if (!atCorner) drawAlignment(matrix, centres[i]!, centres[j]!);
    }
  }

  if (version >= 7) {
    const bits = versionInformation(version);
    for (let i = 0; i < 18; i++) {
      const dark = ((bits >>> i) & 1) === 1;
      const a = size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      matrix.set(a, b, dark);
      matrix.set(b, a, dark);
    }
  }
}

/** Both copies of the format information, plus the module that is always dark. */
function drawFormat(matrix: Matrix, mask: number): void {
  const { size } = matrix;
  const bits = formatInformation(mask);
  const bit = (i: number) => ((bits >>> i) & 1) === 1;

  for (let i = 0; i <= 5; i++) matrix.set(8, i, bit(i));
  matrix.set(8, 7, bit(6));
  matrix.set(8, 8, bit(7));
  matrix.set(7, 8, bit(8));
  for (let i = 9; i < 15; i++) matrix.set(14 - i, 8, bit(i));

  for (let i = 0; i < 8; i++) matrix.set(size - 1 - i, 8, bit(i));
  for (let i = 8; i < 15; i++) matrix.set(8, size - 15 + i, bit(i));
  matrix.set(8, size - 8, true);
}

/**
 * Lays the bit stream out in the zigzag the standard defines: two-module columns
 * from the right edge leftwards, alternating direction, skipping the vertical
 * timing column.
 */
function drawData(matrix: Matrix, data: Uint8Array): void {
  const { size } = matrix;
  let bitIndex = 0;

  for (let right = size - 1; right >= 1; right -= 2) {
    // Column 6 is the timing pattern, so the pairs shift left by one past it.
    if (right === 6) right = 5;
    // Derived from the column index rather than a flip-flop, so the shift past
    // the timing column above cannot desynchronise the direction.
    const upward = ((right + 1) & 2) === 0;
    for (let vertical = 0; vertical < size; vertical++) {
      const y = upward ? size - 1 - vertical : vertical;
      for (let column = 0; column < 2; column++) {
        const x = right - column;
        if (matrix.reserved[y]![x]) continue;
        const byte = data[bitIndex >>> 3];
        matrix.modules[y]![x] =
          byte !== undefined && ((byte >>> (7 - (bitIndex & 7))) & 1) === 1;
        bitIndex++;
      }
    }
  }
}

const MASKS: readonly ((x: number, y: number) => boolean)[] = [
  (x, y) => (x + y) % 2 === 0,
  (_x, y) => y % 2 === 0,
  (x) => x % 3 === 0,
  (x, y) => (x + y) % 3 === 0,
  (x, y) => (Math.floor(y / 2) + Math.floor(x / 3)) % 2 === 0,
  (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
  (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
  (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0,
];

function applyMask(matrix: Matrix, mask: number): void {
  const test = MASKS[mask]!;
  for (let y = 0; y < matrix.size; y++) {
    for (let x = 0; x < matrix.size; x++) {
      if (!matrix.reserved[y]![x] && test(x, y)) {
        matrix.modules[y]![x] = !matrix.modules[y]![x];
      }
    }
  }
}

/**
 * The standard's four penalty rules. The mask with the lowest total wins, which
 * is what keeps a code from containing large blank areas or sequences a scanner
 * could mistake for a finder pattern.
 */
function penalty(matrix: Matrix): number {
  const { size, modules } = matrix;
  let score = 0;

  // Rule 1: runs of five or more identical modules in a row or column.
  for (const transposed of [false, true]) {
    for (let a = 0; a < size; a++) {
      let runColour = false;
      let runLength = 0;
      for (let b = 0; b < size; b++) {
        const dark = transposed ? modules[b]![a]! : modules[a]![b]!;
        if (dark === runColour) {
          runLength++;
          if (runLength === 5) score += 3;
          else if (runLength > 5) score += 1;
        } else {
          runColour = dark;
          runLength = 1;
        }
      }
    }
  }

  // Rule 2: every 2×2 block of one colour.
  for (let y = 0; y < size - 1; y++) {
    for (let x = 0; x < size - 1; x++) {
      const first = modules[y]![x]!;
      if (
        first === modules[y]![x + 1] &&
        first === modules[y + 1]![x] &&
        first === modules[y + 1]![x + 1]
      ) {
        score += 3;
      }
    }
  }

  // Rule 3: the finder-like 1:1:3:1:1 sequence with four light modules beside it.
  const patternA = [true, false, true, true, true, false, true, false, false, false, false];
  const patternB = [false, false, false, false, true, false, true, true, true, false, true];
  for (const transposed of [false, true]) {
    for (let a = 0; a < size; a++) {
      for (let b = 0; b <= size - 11; b++) {
        let matchesA = true;
        let matchesB = true;
        for (let i = 0; i < 11; i++) {
          const dark = transposed ? modules[b + i]![a]! : modules[a]![b + i]!;
          if (dark !== patternA[i]) matchesA = false;
          if (dark !== patternB[i]) matchesB = false;
        }
        if (matchesA) score += 40;
        if (matchesB) score += 40;
      }
    }
  }

  // Rule 4: deviation from an even split of dark and light.
  let dark = 0;
  for (const row of modules) for (const module of row) if (module) dark++;
  const percent = (dark * 100) / (size * size);
  score += Math.floor(Math.abs(percent - 50) / 5) * 10;

  return score;
}

/**
 * Encodes `text` and returns the finished module matrix.
 *
 * Every mask is built and scored, and the best is returned — the standard
 * requires this, and a fixed mask can produce a code that some scanners refuse.
 */
export function encodeQr(text: string): QrCode {
  const data = new TextEncoder().encode(text);
  const version = smallestVersionFor(data.length);
  const codewords = interleave(buildDataCodewords(data, version), version);

  // The remainder bits are already zero, so only the codewords need writing.
  const padded = new Uint8Array(codewords.length + Math.ceil(remainderBits(version) / 8));
  padded.set(codewords);

  let best: Matrix | null = null;
  let bestScore = Infinity;
  for (let mask = 0; mask < 8; mask++) {
    const matrix = new Matrix(version);
    drawFunctionPatterns(matrix, version);
    drawFormat(matrix, mask);
    drawData(matrix, padded);
    applyMask(matrix, mask);
    const score = penalty(matrix);
    if (score < bestScore) {
      bestScore = score;
      best = matrix;
    }
  }

  const chosen = best!;
  return { size: chosen.size, version, modules: chosen.modules };
}

/**
 * The dark modules as a single SVG path, in a `0 0 size size` user space.
 *
 * One path rather than a rect per module keeps the markup small — a version 9
 * code has well over a thousand dark modules — and avoids the hairline seams
 * that adjacent rects produce when a browser rounds their edges.
 */
export function qrToSvgPath(code: QrCode): string {
  const parts: string[] = [];
  for (let y = 0; y < code.size; y++) {
    for (let x = 0; x < code.size; x++) {
      if (code.modules[y]![x]) parts.push(`M${x} ${y}h1v1h-1z`);
    }
  }
  return parts.join('');
}
