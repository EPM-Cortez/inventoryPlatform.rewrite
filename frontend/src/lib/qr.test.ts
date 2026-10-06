import { describe, expect, it } from 'vitest';

import {
  encodeQr,
  formatInformation,
  qrToSvgPath,
  versionInformation,
  type QrCode,
} from './qr';

/**
 * The table from the standard, repeated here independently of the encoder so a
 * transcription error in one is not silently mirrored by the other.
 */
const TOTAL_CODEWORDS: Record<number, number> = {
  1: 26, 2: 44, 3: 70, 4: 100, 5: 134, 6: 172, 7: 196, 8: 242, 9: 292, 10: 346,
  11: 404, 12: 466, 13: 532, 14: 581, 15: 655, 16: 733, 17: 815, 18: 901,
  19: 991, 20: 1085,
};

// ---------------------------------------------------------------------------
// A decoder, written only for these tests.
//
// Reading the matrix back out is the check that matters: it exercises the
// zigzag placement, the mask choice, the format bits and the block interleaving
// together. A structural assertion can pass on a code no scanner would read;
// a successful round-trip cannot.
// ---------------------------------------------------------------------------

const MASKS: ((x: number, y: number) => boolean)[] = [
  (x, y) => (x + y) % 2 === 0,
  (_x, y) => y % 2 === 0,
  (x) => x % 3 === 0,
  (x, y) => (x + y) % 3 === 0,
  (x, y) => (Math.floor(y / 2) + Math.floor(x / 3)) % 2 === 0,
  (x, y) => ((x * y) % 2) + ((x * y) % 3) === 0,
  (x, y) => (((x * y) % 2) + ((x * y) % 3)) % 2 === 0,
  (x, y) => (((x + y) % 2) + ((x * y) % 3)) % 2 === 0,
];

const BLOCKS: Record<number, [number, number, number, number, number]> = {
  1: [10, 1, 16, 0, 0], 2: [16, 1, 28, 0, 0], 3: [26, 1, 44, 0, 0],
  4: [18, 2, 32, 0, 0], 5: [24, 2, 43, 0, 0], 6: [16, 4, 27, 0, 0],
  7: [18, 4, 31, 0, 0], 8: [22, 2, 38, 2, 39], 9: [22, 3, 36, 2, 37],
  10: [26, 4, 43, 1, 44], 11: [30, 1, 50, 4, 51], 12: [22, 6, 36, 2, 37],
  13: [22, 8, 37, 1, 38], 14: [24, 4, 40, 5, 41], 15: [24, 5, 41, 5, 42],
  16: [28, 7, 45, 3, 46], 17: [28, 10, 46, 1, 47], 18: [26, 9, 43, 4, 44],
  19: [26, 3, 44, 11, 45], 20: [26, 3, 41, 13, 42],
};

const ALIGNMENT: Record<number, number[]> = {
  1: [], 2: [6, 18], 3: [6, 22], 4: [6, 26], 5: [6, 30], 6: [6, 34],
  7: [6, 22, 38], 8: [6, 24, 42], 9: [6, 26, 46], 10: [6, 28, 50],
  11: [6, 30, 54], 12: [6, 32, 58], 13: [6, 34, 62], 14: [6, 26, 46, 66],
  15: [6, 26, 48, 70], 16: [6, 26, 50, 74], 17: [6, 30, 54, 78],
  18: [6, 30, 56, 82], 19: [6, 30, 58, 86], 20: [6, 34, 62, 90],
};

/** Rebuilds the map of which modules are function patterns, not data. */
function reservedMap(version: number, size: number): boolean[][] {
  const reserved = Array.from({ length: size }, () =>
    new Array<boolean>(size).fill(false),
  );
  const reserve = (x: number, y: number) => {
    if (x >= 0 && x < size && y >= 0 && y < size) reserved[y]![x] = true;
  };

  for (let i = 0; i < size; i++) {
    reserve(6, i);
    reserve(i, 6);
  }
  for (const [cx, cy] of [
    [3, 3],
    [size - 4, 3],
    [3, size - 4],
  ]) {
    for (let dy = -4; dy <= 4; dy++) {
      for (let dx = -4; dx <= 4; dx++) reserve(cx! + dx, cy! + dy);
    }
  }
  const centres = ALIGNMENT[version]!;
  for (let i = 0; i < centres.length; i++) {
    for (let j = 0; j < centres.length; j++) {
      const corner =
        (i === 0 && j === 0) ||
        (i === 0 && j === centres.length - 1) ||
        (i === centres.length - 1 && j === 0);
      if (corner) continue;
      for (let dy = -2; dy <= 2; dy++) {
        for (let dx = -2; dx <= 2; dx++) reserve(centres[i]! + dx, centres[j]! + dy);
      }
    }
  }
  // Format information, both copies, plus the always-dark module.
  for (let i = 0; i <= 8; i++) {
    reserve(8, i);
    reserve(i, 8);
  }
  for (let i = 0; i < 8; i++) reserve(size - 1 - i, 8);
  for (let i = 8; i < 15; i++) reserve(8, size - 15 + i);
  // The module that is always dark. Omitting it shifts every codeword after it
  // by one bit — late enough in the zigzag that a short payload still decodes,
  // which is exactly how it hid until the parity check went in.
  reserve(8, size - 8);
  if (version >= 7) {
    for (let i = 0; i < 18; i++) {
      const a = size - 11 + (i % 3);
      const b = Math.floor(i / 3);
      reserve(a, b);
      reserve(b, a);
    }
  }
  return reserved;
}

/** Reads the 15 format bits and returns the mask index they encode. */
function readMask(code: QrCode): number {
  let bits = 0;
  const read = (x: number, y: number) => (code.modules[y]![x] ? 1 : 0);
  for (let i = 0; i <= 5; i++) bits |= read(8, i) << i;
  bits |= read(8, 7) << 6;
  bits |= read(8, 8) << 7;
  bits |= read(7, 8) << 8;
  for (let i = 9; i < 15; i++) bits |= read(14 - i, 8) << i;

  const unmasked = bits ^ 0b101010000010010;
  const eccBits = (unmasked >>> 13) & 0b11;
  expect(eccBits).toBe(0b00); // level M
  return (unmasked >>> 10) & 0b111;
}

/** Undoes the mask, walks the zigzag, de-interleaves, and returns the payload. */
function decode(code: QrCode): string {
  const { size, version, modules } = code;
  const mask = MASKS[readMask(code)]!;
  const reserved = reservedMap(version, size);

  const bits: number[] = [];
  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) right = 5;
    const upward = ((right + 1) & 2) === 0;
    for (let vertical = 0; vertical < size; vertical++) {
      const y = upward ? size - 1 - vertical : vertical;
      for (let column = 0; column < 2; column++) {
        const x = right - column;
        if (reserved[y]![x]) continue;
        const dark = modules[y]![x]! !== mask(x, y);
        bits.push(dark ? 1 : 0);
      }
    }
  }

  const interleaved: number[] = [];
  for (let i = 0; i + 7 < bits.length; i += 8) {
    let byte = 0;
    for (let j = 0; j < 8; j++) byte = (byte << 1) | bits[i + j]!;
    interleaved.push(byte);
  }

  // Reverse the interleaving to recover each block's data codewords in order.
  const [, g1Blocks, g1Data, g2Blocks, g2Data] = BLOCKS[version]!;
  const blockLengths = [
    ...Array.from({ length: g1Blocks }, () => g1Data),
    ...Array.from({ length: g2Blocks }, () => g2Data),
  ];
  const blocks: number[][] = blockLengths.map(() => []);
  let cursor = 0;
  for (let i = 0; i < Math.max(g1Data, g2Data); i++) {
    for (let b = 0; b < blockLengths.length; b++) {
      if (i < blockLengths[b]!) blocks[b]!.push(interleaved[cursor++]!);
    }
  }
  const data = blocks.flat();

  // Byte mode: 4-bit mode indicator, then the character count, then the bytes.
  let bitCursor = 0;
  const take = (count: number) => {
    let value = 0;
    for (let i = 0; i < count; i++) {
      const byte = data[(bitCursor >>> 3) % data.length]!;
      value = (value << 1) | ((byte >>> (7 - (bitCursor & 7))) & 1);
      bitCursor++;
    }
    return value;
  };
  expect(take(4)).toBe(0b0100);
  const length = take(version <= 9 ? 8 : 16);
  const bytes = new Uint8Array(length);
  for (let i = 0; i < length; i++) bytes[i] = take(8);
  return new TextDecoder().decode(bytes);
}

// ---------------------------------------------------------------------------

describe('the block table', () => {
  it('accounts for every codeword in each version', () => {
    for (const [version, [ec, g1, d1, g2, d2]] of Object.entries(BLOCKS)) {
      const total = g1 * d1 + g2 * d2 + (g1 + g2) * ec;
      expect(total, `version ${version}`).toBe(TOTAL_CODEWORDS[Number(version)]);
    }
  });
});

describe('encodeQr', () => {
  it('sizes the matrix as 4·version + 17', () => {
    const code = encodeQr('https://example.school/scan/abc');
    expect(code.size).toBe(code.version * 4 + 17);
  });

  it('picks the smallest version that fits', () => {
    // Version 1 at level M holds 16 data codewords, minus 2 for the header.
    expect(encodeQr('a'.repeat(14)).version).toBe(1);
    expect(encodeQr('a'.repeat(15)).version).toBe(2);
  });

  it('places the three finder patterns', () => {
    const { modules, size } = encodeQr('finder patterns');
    for (const [ox, oy] of [
      [0, 0],
      [size - 7, 0],
      [0, size - 7],
    ]) {
      // Dark ring, light ring, solid 3×3 core.
      expect(modules[oy!]![ox!]).toBe(true);
      expect(modules[oy! + 1]![ox! + 1]).toBe(false);
      expect(modules[oy! + 3]![ox! + 3]).toBe(true);
    }
  });

  it('lays the timing patterns down as alternating modules', () => {
    const { modules, size } = encodeQr('timing');
    for (let i = 8; i < size - 8; i++) {
      expect(modules[6]![i], `row 6 at ${i}`).toBe(i % 2 === 0);
      expect(modules[i]![6], `column 6 at ${i}`).toBe(i % 2 === 0);
    }
  });

  it('always sets the dark module below the top-left format block', () => {
    const { modules, size } = encodeQr('dark module');
    expect(modules[size - 8]![8]).toBe(true);
  });

  it('rejects text too long for the supported versions', () => {
    expect(() => encodeQr('x'.repeat(700))).toThrow(RangeError);
  });
});

describe('round trip', () => {
  it('reads back a realistic otpauth URI', () => {
    const uri =
      'otpauth://totp/Inventory:admin@inventory.local?secret=JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP&issuer=Inventory&algorithm=SHA1&digits=6&period=30';
    const code = encodeQr(uri);
    expect(decode(code)).toBe(uri);
  });

  it.each([1, 13, 14, 40, 100, 180, 400, 660])(
    'reads back %i bytes of content',
    (length) => {
      const text = Array.from({ length }, (_, i) =>
        String.fromCharCode(33 + (i % 90)),
      ).join('');
      expect(decode(encodeQr(text))).toBe(text);
    },
  );

  it('reads back multi-byte UTF-8', () => {
    const text = 'Séamus — ICT Suite 1 · 校舎 ✓';
    expect(decode(encodeQr(text))).toBe(text);
  });

  it('survives the odd data lengths that shift the pad alternation', () => {
    // The filler alternates from the first pad byte, so an odd-length payload
    // is where an absolute-index implementation goes wrong.
    for (let length = 1; length <= 40; length++) {
      const text = 'z'.repeat(length);
      expect(decode(encodeQr(text)), `length ${length}`).toBe(text);
    }
  });
});

// ---------------------------------------------------------------------------
// Error correction, verified independently of how the encoder computed it.
//
// A codeword block is valid exactly when the polynomial formed by its data and
// parity bytes is divisible by the generator polynomial. Recomputing that from
// the matrix — with a GF implementation written separately below — is what a
// scanner effectively does, and it is the half of the format the round-trip
// test above does not touch.
// ---------------------------------------------------------------------------

/** Multiplication in GF(256) by shift-and-add, deliberately not table-driven. */
function gfMul(a: number, b: number): number {
  let product = 0;
  for (let i = 0; i < 8; i++) {
    if (b & (1 << i)) product ^= a << i;
  }
  for (let i = 15; i >= 8; i--) {
    if (product & (1 << i)) product ^= 0x11d << (i - 8);
  }
  return product & 0xff;
}

function generator(degree: number): number[] {
  let poly = [1];
  let root = 1;
  for (let i = 0; i < degree; i++) {
    const next = new Array<number>(poly.length + 1).fill(0);
    for (let j = 0; j < poly.length; j++) {
      next[j]! ^= poly[j]!;
      next[j + 1]! ^= gfMul(poly[j]!, root);
    }
    poly = next;
    root = gfMul(root, 2);
  }
  return poly;
}

/** Every block's data and parity codewords, recovered from the finished matrix. */
function blocksFrom(code: QrCode): { data: number[]; ec: number[] }[] {
  const { size, version, modules } = code;
  const mask = MASKS[readMask(code)]!;
  const reserved = reservedMap(version, size);

  const bits: number[] = [];
  for (let right = size - 1; right >= 1; right -= 2) {
    if (right === 6) right = 5;
    const upward = ((right + 1) & 2) === 0;
    for (let vertical = 0; vertical < size; vertical++) {
      const y = upward ? size - 1 - vertical : vertical;
      for (let column = 0; column < 2; column++) {
        const x = right - column;
        if (reserved[y]![x]) continue;
        bits.push(modules[y]![x]! !== mask(x, y) ? 1 : 0);
      }
    }
  }
  const codewords: number[] = [];
  for (let i = 0; i + 7 < bits.length; i += 8) {
    let byte = 0;
    for (let j = 0; j < 8; j++) byte = (byte << 1) | bits[i + j]!;
    codewords.push(byte);
  }

  const [ecPerBlock, g1Blocks, g1Data, g2Blocks, g2Data] = BLOCKS[version]!;
  const lengths = [
    ...Array.from({ length: g1Blocks }, () => g1Data),
    ...Array.from({ length: g2Blocks }, () => g2Data),
  ];
  const blocks = lengths.map(() => ({ data: [] as number[], ec: [] as number[] }));

  let cursor = 0;
  for (let i = 0; i < Math.max(g1Data, g2Data); i++) {
    for (let b = 0; b < lengths.length; b++) {
      if (i < lengths[b]!) blocks[b]!.data.push(codewords[cursor++]!);
    }
  }
  for (let i = 0; i < ecPerBlock; i++) {
    for (const block of blocks) block.ec.push(codewords[cursor++]!);
  }
  return blocks;
}

/** Remainder of the block polynomial modulo the generator; zero when valid. */
function syndrome(block: { data: number[]; ec: number[] }): number[] {
  const gen = generator(block.ec.length);
  const remainder = new Array<number>(block.ec.length).fill(0);
  for (const byte of [...block.data, ...block.ec]) {
    const factor = byte ^ remainder[0]!;
    remainder.shift();
    remainder.push(0);
    for (let i = 0; i < remainder.length; i++) {
      remainder[i]! ^= gfMul(gen[i + 1]!, factor);
    }
  }
  return remainder;
}

describe('error correction', () => {
  it.each([1, 40, 120, 200, 400, 660])(
    'produces parity a scanner will accept for %i bytes',
    (length) => {
      const text = 'Q'.repeat(length);
      const blocks = blocksFrom(encodeQr(text));
      expect(blocks.length).toBeGreaterThan(0);
      for (const [index, block] of blocks.entries()) {
        expect(syndrome(block), `block ${index}`).toEqual(
          new Array<number>(block.ec.length).fill(0),
        );
      }
    },
  );

  it('detects a corrupted codeword, proving the check is not vacuous', () => {
    const blocks = blocksFrom(encodeQr('corruption check'));
    const damaged = { ...blocks[0]!, data: [...blocks[0]!.data] };
    damaged.data[0] = damaged.data[0]! ^ 0xff;
    expect(syndrome(damaged)).not.toEqual(new Array<number>(damaged.ec.length).fill(0));
  });
});

describe('format and version information', () => {
  it('matches the published word for level M with mask 0', () => {
    expect(formatInformation(0)).toBe(0b101010000010010);
  });

  it('keeps the minimum Hamming distance of 7 across all 32 words', () => {
    const words: number[] = [];
    for (const ecc of [0b00, 0b01, 0b10, 0b11]) {
      for (let mask = 0; mask < 8; mask++) words.push(formatInformation(mask, ecc));
    }
    expect(new Set(words).size).toBe(32);
    for (let i = 0; i < words.length; i++) {
      for (let j = i + 1; j < words.length; j++) {
        const distance = popcount(words[i]! ^ words[j]!);
        expect(distance, `words ${i} and ${j}`).toBeGreaterThanOrEqual(7);
      }
    }
  });

  it('keeps the version words separated and self-describing', () => {
    const words = new Map<number, number>();
    for (let version = 7; version <= 20; version++) {
      const word = versionInformation(version);
      // The top six bits are the version itself; the rest is the BCH check.
      expect(word >>> 12).toBe(version);
      words.set(version, word);
    }
    const list = [...words.values()];
    for (let i = 0; i < list.length; i++) {
      for (let j = i + 1; j < list.length; j++) {
        expect(popcount(list[i]! ^ list[j]!)).toBeGreaterThanOrEqual(8);
      }
    }
  });
});

function popcount(value: number): number {
  let count = 0;
  for (let v = value; v !== 0; v >>>= 1) count += v & 1;
  return count;
}

describe('qrToSvgPath', () => {
  it('emits one subpath per dark module', () => {
    const code = encodeQr('path');
    const dark = code.modules.flat().filter(Boolean).length;
    expect(qrToSvgPath(code).match(/M/g)).toHaveLength(dark);
  });
});
