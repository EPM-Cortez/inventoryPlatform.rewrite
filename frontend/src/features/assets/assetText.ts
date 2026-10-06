/** "HP ProDesk 400 G7", not "HP HP ProDesk 400 G7" when the model repeats the make. */
export function makeAndModel({
  manufacturer,
  model,
}: {
  manufacturer: string | null;
  model: string | null;
}): string | null {
  if (!manufacturer) return model || null;
  if (!model) return manufacturer;
  return model.toLowerCase().startsWith(manufacturer.toLowerCase())
    ? model
    : `${manufacturer} ${model}`;
}

/**
 * The device name, unless it only repeats the asset tag — imported registers
 * often copy one into the other, and showing it twice is noise.
 */
export function distinctDeviceName({
  assetTag,
  deviceName,
}: {
  assetTag: string;
  deviceName: string | null;
}): string | null {
  if (!deviceName) return null;
  return deviceName.trim().toLowerCase() === assetTag.trim().toLowerCase()
    ? null
    : deviceName;
}
