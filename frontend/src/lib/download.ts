/**
 * Saves a blob to the user's downloads.
 *
 * The object URL is revoked on the next frame rather than immediately: some
 * browsers have not finished reading the blob when `click()` returns, and
 * revoking too early produces a silently empty file.
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  document.body.append(link);
  link.click();
  link.remove();
  requestAnimationFrame(() => URL.revokeObjectURL(url));
}
