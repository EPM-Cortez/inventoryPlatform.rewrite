/**
 * Mirrors the Identity password policy configured in
 * `Inventory.Infrastructure/DependencyInjection.cs`:
 *
 *   RequiredLength = 12, RequiredUniqueChars = 4,
 *   RequireDigit, RequireLowercase, RequireUppercase, RequireNonAlphanumeric
 *
 * A generated password that misses any of these is rejected with
 * `identity-operation-failed`, so the whole create/reset flow fails. If the
 * server policy changes, change this and `temporaryPassword.test.ts` together.
 */
export const MIN_PASSWORD_LENGTH = 12;

/**
 * Character classes, minus the ones that are misread when a password is read
 * aloud or copied off a screen: `O`/`0`, `I`/`l`/`1`. Symbols avoid quotes,
 * backslash and backtick, which get mangled in shells and spreadsheets.
 */
const UPPERCASE = 'ABCDEFGHJKLMNPQRSTUVWXYZ';
const LOWERCASE = 'abcdefghijkmnopqrstuvwxyz';
const DIGITS = '23456789';
const SYMBOLS = '!@#$%^&*?-_+=';
const ALL = UPPERCASE + LOWERCASE + DIGITS + SYMBOLS;

const LENGTH = 20;

/** Uniform index in `[0, max)` from the platform CSPRNG, rejecting modulo bias. */
function randomIndex(max: number): number {
  const limit = Math.floor(0xffffffff / max) * max;
  const buffer = new Uint32Array(1);
  let value: number;
  do {
    crypto.getRandomValues(buffer);
    value = buffer[0]!;
  } while (value >= limit);
  return value % max;
}

function pick(alphabet: string): string {
  return alphabet[randomIndex(alphabet.length)]!;
}

/**
 * Generates a temporary password that satisfies every rule the server enforces.
 *
 * One character is drawn from each required class first so the result can never
 * fail the policy, then the remainder comes from the full pool and the whole
 * thing is shuffled — otherwise the class of each leading character would be
 * predictable.
 *
 * It never leaves the browser except as the request body that creates or resets
 * the account, and the account is flagged `mustChangePassword`, so it is used
 * exactly once. `Math.random` would not be acceptable here.
 */
export function generateTemporaryPassword(): string {
  const characters = [pick(UPPERCASE), pick(LOWERCASE), pick(DIGITS), pick(SYMBOLS)];

  while (characters.length < LENGTH) {
    characters.push(pick(ALL));
  }

  // Fisher-Yates, CSPRNG-driven.
  for (let i = characters.length - 1; i > 0; i--) {
    const j = randomIndex(i + 1);
    [characters[i], characters[j]] = [characters[j]!, characters[i]!];
  }

  return characters.join('');
}
