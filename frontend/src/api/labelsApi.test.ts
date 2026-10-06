import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { invalidateCsrfToken, setUnauthenticatedHandler } from './client';
import { labelsApi } from './endpoints';

const ASSET_ID = '22222222-2222-2222-2222-222222222222';

function pdfResponse(headers: Record<string, string>): Response {
  return new Response('%PDF-1.7', {
    status: 200,
    headers: { 'Content-Type': 'application/pdf', ...headers },
  });
}

describe('labelsApi', () => {
  let fetchMock: ReturnType<typeof vi.fn>;
  let headers: Record<string, string>;

  beforeEach(() => {
    invalidateCsrfToken();
    setUnauthenticatedHandler(null);
    headers = {
      'Content-Disposition': 'inline; filename="asset-labels.pdf"',
      'X-Label-Count': '24',
      'X-Label-Page-Count': '1',
    };
    fetchMock = vi.fn().mockImplementation((url: string) =>
      Promise.resolve(
        url.includes('/auth/csrf')
          ? new Response(JSON.stringify({ token: 'token' }), {
              status: 200,
              headers: { 'Content-Type': 'application/json' },
            })
          : pdfResponse(headers),
      ),
    );
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  function lastCall() {
    const calls = fetchMock.mock.calls.filter(
      ([url]) => !String(url).includes('/auth/csrf'),
    );
    const [url, init] = calls[calls.length - 1]!;
    return { url: String(url), init: init as RequestInit };
  }

  // The counts exist only as response headers — parsing the PDF to find them
  // would be absurd, and dropping them would leave the user guessing how many
  // sheets to load.
  it('reads the label and page counts from the response headers', async () => {
    const document = await labelsApi.printBatch({
      stock: 'averyL7159',
      startPosition: 1,
      items: [{ assetId: ASSET_ID, quantity: 24 }],
      issueMissingQr: true,
    });

    expect(document.labelCount).toBe(24);
    expect(document.pageCount).toBe(1);
    expect(document.blob.type).toBe('application/pdf');
  });

  it('takes the filename from Content-Disposition', async () => {
    const document = await labelsApi.printBatch({
      stock: 'averyL7159',
      startPosition: 1,
      items: [{ assetId: ASSET_ID, quantity: 1 }],
      issueMissingQr: true,
    });

    expect(document.fileName).toBe('asset-labels.pdf');
  });

  it('falls back to a sensible filename when the header is absent', async () => {
    headers = { 'X-Label-Count': '1', 'X-Label-Page-Count': '1' };

    const document = await labelsApi.printAsset(ASSET_ID, {
      stock: 'brotherDk11209',
      copies: 1,
      startPosition: 1,
      issueMissingQr: true,
    });

    expect(document.fileName).toContain(ASSET_ID);
    expect(document.fileName).toMatch(/\.pdf$/);
  });

  it('defaults the counts to zero rather than NaN when headers are missing', async () => {
    headers = {};

    const document = await labelsApi.printBatch({
      stock: 'averyL7159',
      startPosition: 1,
      items: [{ assetId: ASSET_ID, quantity: 1 }],
      issueMissingQr: true,
    });

    expect(document.labelCount).toBe(0);
    expect(document.pageCount).toBe(0);
  });

  // The endpoints are POSTs that return binary, which is an unusual enough shape
  // that it is worth pinning both halves.
  it('POSTs the batch request as JSON and asks for a PDF back', async () => {
    await labelsApi.printBatch({
      stock: 'averyL7159',
      startPosition: 7,
      items: [{ assetId: ASSET_ID, quantity: 3 }],
      issueMissingQr: false,
    });

    const { url, init } = lastCall();
    expect(url).toBe('/api/v1/labels/batch');
    expect(init.method).toBe('POST');
    const sentHeaders = init.headers as Record<string, string>;
    expect(sentHeaders.Accept).toBe('application/pdf');
    expect(sentHeaders['Content-Type']).toBe('application/json');
    expect(sentHeaders['X-CSRF-TOKEN']).toBe('token');
    expect(JSON.parse(String(init.body))).toEqual({
      stock: 'averyL7159',
      startPosition: 7,
      items: [{ assetId: ASSET_ID, quantity: 3 }],
      issueMissingQr: false,
    });
  });

  it('sends the stock as a camel-case string, not an integer', async () => {
    await labelsApi.printAsset(ASSET_ID, {
      stock: 'brotherDk11209',
      copies: 2,
      startPosition: 1,
      issueMissingQr: true,
    });

    // The API is configured with `allowIntegerValues: false` and rejects an
    // integer, in the body and in the query string alike.
    const { init } = lastCall();
    expect(JSON.parse(String(init.body)).stock).toBe('brotherDk11209');
  });
});
