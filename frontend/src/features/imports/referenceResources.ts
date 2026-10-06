import type { ReferenceDataImportResource } from '@/api/types';
import { Permission, type PermissionKey } from '@/auth/permissions';

/**
 * Everything that differs between the three reference-data imports.
 *
 * The pipeline is identical — only the vocabulary, the accepted headers and the
 * key column change — so keeping the differences in one table means the wizard
 * itself has no per-resource branching.
 */
export interface ReferenceResourceConfig {
  resource: ReferenceDataImportResource;
  /** Plural, sentence case, e.g. "people". */
  plural: string;
  /** Title case for headings, e.g. "People". */
  title: string;
  singular: string;
  /** Where the list lives. */
  listPath: string;
  /** What the natural key is called to a user. */
  keyNoun: string;
  /** Column header for the key in the preview table. */
  keyHeader: string;
  exportPermission: PermissionKey;
  /**
   * Value the `schemaVersion` column must carry. Reference data is on 1 while
   * assets are on 2, which is an easy and completely opaque mistake to make by
   * hand — the server rejects every row with "Only reference-data CSV schema
   * version 1 is supported."
   */
  schemaVersion: number;
  /** Canonical CSV columns for this resource. */
  canonicalHeaders: string[];
  /** The reduced layout also accepted, for files exported from other systems. */
  compatibilityHeaders: string[];
  /** How a missing key is derived, or null when keys are always required. */
  generatedKeyExplanation: string | null;
  /** People carry a person type; locations carry a location type. */
  showType: boolean;
  /** Only locations nest, via parentCode. */
  showParent: boolean;
}

export const REFERENCE_RESOURCES: Record<
  ReferenceDataImportResource,
  ReferenceResourceConfig
> = {
  people: {
    resource: 'people',
    plural: 'people',
    title: 'People',
    singular: 'person',
    listPath: '/people',
    keyNoun: 'external ID',
    keyHeader: 'External ID',
    exportPermission: Permission.ExportsPeople,
    schemaVersion: 1,
    canonicalHeaders: [
      'schemaVersion',
      'externalId',
      'displayName',
      'personType',
      'email',
      'isActive',
    ],
    compatibilityHeaders: ['displayName', 'personType', 'email'],
    generatedKeyExplanation:
      'Staff rows with no external ID get one derived from the local-part of their email address.',
    showType: true,
    showParent: false,
  },
  departments: {
    resource: 'departments',
    plural: 'departments',
    title: 'Departments',
    singular: 'department',
    listPath: '/departments',
    keyNoun: 'code',
    keyHeader: 'Code',
    exportPermission: Permission.ExportsDepartments,
    schemaVersion: 1,
    canonicalHeaders: ['schemaVersion', 'code', 'name', 'isActive'],
    compatibilityHeaders: ['name', 'code'],
    generatedKeyExplanation:
      'Rows with no code get a slug derived from the name — “Sixth Form” becomes SIXTH-FORM.',
    showType: false,
    showParent: false,
  },
  locations: {
    resource: 'locations',
    plural: 'locations',
    title: 'Locations',
    singular: 'location',
    listPath: '/locations',
    keyNoun: 'code',
    keyHeader: 'Code',
    exportPermission: Permission.ExportsLocations,
    schemaVersion: 1,
    canonicalHeaders: [
      'schemaVersion',
      'code',
      'name',
      'type',
      'parentCode',
      'isActive',
    ],
    compatibilityHeaders: ['Name', 'Type', 'Code'],
    generatedKeyExplanation:
      'Rows with no code get a slug derived from the name — “Sixth Form” becomes SIXTH-FORM.',
    showType: true,
    showParent: true,
  },
};

export function isReferenceResource(
  value: string | undefined,
): value is ReferenceDataImportResource {
  return value !== undefined && value in REFERENCE_RESOURCES;
}
