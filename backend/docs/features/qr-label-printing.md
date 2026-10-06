# QR label printing

Status: Implemented as a milestone 3 extension.

The backend creates print-ready PDF labels containing:

- the asset tag;
- a device name built from `Manufacturer + Model`, falling back to the category name;
- the asset's opaque QR URL as a vector QR code.

No frontend or printer driver is required. The PDF response opens inline so a browser
or desktop client can send it to the installed printer.

## Supported stock

| API value | Physical layout | Intended use |
| --- | --- | --- |
| `brotherDk11209` | One 62 x 29 mm label per PDF page | Individual or batch output to a Brother-compatible roll-label printer |
| `averyL7159` | A4 sheet, 3 columns x 8 rows, 63.5 x 33.9 mm labels | Up to 24 labels per sheet on a normal A4 printer |

The roll preset follows Brother's published DK-11209 62 x 29 mm size. The sheet
preset follows Avery's L7159 63.5 x 33.9 mm, 24-label A4 template. See
[Brother QL consumables](https://www.brother.co.uk/en/supplies/ql1050) and
[Avery L7159 template](https://www.avery.co.uk/template-l7159).

`GET /api/v1/labels/stocks` returns these dimensions and layout metadata. Stock values
are explicit so adding another physical format later does not silently change existing
print output.

## Individual label

```http
POST /api/v1/assets/{assetId}/label
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "stock": "brotherDk11209",
  "copies": 1,
  "startPosition": 1,
  "issueMissingQr": true
}
```

`copies` is 1 to 100. For roll stock, every copy is a separate 62 x 29 mm PDF page and
`startPosition` must be `1`.

## Batch sheet

```http
POST /api/v1/labels/batch
Content-Type: application/json
X-CSRF-TOKEN: <authenticated request token>

{
  "stock": "averyL7159",
  "startPosition": 7,
  "issueMissingQr": true,
  "items": [
    {
      "assetId": "11111111-1111-1111-1111-111111111111",
      "quantity": 1
    },
    {
      "assetId": "22222222-2222-2222-2222-222222222222",
      "quantity": 2
    }
  ]
}
```

Sheet positions are one-based and filled left-to-right, then top-to-bottom. Use
`startPosition` 1 to 24 to reuse a partially consumed first sheet. Subsequent pages
start at position 1. A request can contain at most 240 labels, with 1 to 100 copies of
each listed asset.

The service validates every asset before issuing any missing QR identifier. By default,
an asset without an active QR gets one through the normal QR issue workflow and
receives its `QrIssued` history event. Set `issueMissingQr` to `false` when printing
must fail instead.

## Printing

- Print at **Actual size** or **100%**. Do not select Fit, Shrink, or Scale to page.
- The PDF declares `PrintScaling /None`, but the print dialog remains under user and
  printer-driver control.
- Use the label manufacturer's media and printer settings. Run a one-page alignment
  test before a large batch.
- `X-Label-Count` and `X-Label-Page-Count` response headers make the output easy for a
  future frontend to confirm before printing.

QR modules are drawn as vector shapes with error-correction level Q, so printer
resolution does not rasterise or blur the code inside the PDF.

## Security and audit

All three label endpoints require `qr.print`. The Administrator, InventoryManager,
and Technician presets hold it; ReadOnlyAuditor does not. Both print operations are
CSRF-protected.

Successful output creates a `qr.labels.printed` audit record containing the stock,
counts, start position, and asset IDs. It never logs the plaintext QR token, protected
token, or payload URL. PDF responses use `Cache-Control: no-store`.
