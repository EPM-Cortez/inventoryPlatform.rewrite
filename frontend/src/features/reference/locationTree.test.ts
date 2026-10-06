import { describe, expect, it } from 'vitest';

import type { Location } from '@/api/types';

import {
  ancestorsOf,
  buildLocationTree,
  descendantsOf,
  rollUpTotals,
} from './locationTree';

function location(
  id: string,
  parentLocationId: string | null = null,
  name: string = id,
): Location {
  return {
    id,
    code: id.toUpperCase(),
    name,
    type: null,
    parentLocationId,
    isActive: true,
    version: `${id}-version`,
  };
}

describe('descendantsOf', () => {
  const tree = [
    location('block-a'),
    location('floor-1', 'block-a'),
    location('room-101', 'floor-1'),
    location('room-102', 'floor-1'),
    location('floor-2', 'block-a'),
    location('block-b'),
    location('store', 'block-b'),
  ];

  it('excludes the location itself', () => {
    expect(descendantsOf('room-101', tree)).toEqual(new Set(['room-101']));
  });

  it('excludes every level beneath, not just direct children', () => {
    expect(descendantsOf('block-a', tree)).toEqual(
      new Set(['block-a', 'floor-1', 'floor-2', 'room-101', 'room-102']),
    );
  });

  it('leaves a sibling branch selectable', () => {
    const excluded = descendantsOf('block-a', tree);
    expect(excluded.has('block-b')).toBe(false);
    expect(excluded.has('store')).toBe(false);
  });

  it('handles a location that is not in the list', () => {
    expect(descendantsOf('unknown', tree)).toEqual(new Set(['unknown']));
  });

  it('terminates when the stored data already contains a cycle', () => {
    // Not reachable through this UI, but a hand-edited database or a future
    // server bug must not spin the browser.
    const cyclic = [location('a', 'b'), location('b', 'a')];
    expect(descendantsOf('a', cyclic)).toEqual(new Set(['a', 'b']));
  });
});

describe('buildLocationTree', () => {
  it('nests children under their parents', () => {
    const { roots } = buildLocationTree([
      location('room-1', 'block'),
      location('block'),
      location('room-2', 'block'),
    ]);
    expect(roots.map((node) => node.id)).toEqual(['block']);
    expect(roots[0]?.children.map((node) => node.id)).toEqual(['room-1', 'room-2']);
  });

  it('sorts room numbers naturally rather than as text', () => {
    const { roots } = buildLocationTree([
      location('a', null, 'EN10'),
      location('b', null, 'EN2'),
      location('c', null, 'EN1'),
    ]);
    expect(roots.map((node) => node.name)).toEqual(['EN1', 'EN2', 'EN10']);
  });

  it('promotes a location whose parent is missing instead of dropping it', () => {
    const { roots } = buildLocationTree([location('room', 'inactive-block')]);
    expect(roots.map((node) => node.id)).toEqual(['room']);
  });

  it('keeps every member of a stored cycle reachable', () => {
    const { roots, byId } = buildLocationTree([location('a', 'b'), location('b', 'a')]);
    const reachable = new Set<string>();
    const walk = (nodes: typeof roots) =>
      nodes.forEach((node) => {
        reachable.add(node.id);
        walk(node.children);
      });
    walk(roots);
    expect(reachable).toEqual(new Set(byId.keys()));
  });
});

describe('ancestorsOf', () => {
  const { byId } = buildLocationTree([
    location('site'),
    location('block', 'site'),
    location('room', 'block'),
  ]);

  it('lists the path outermost first, without the location itself', () => {
    expect(ancestorsOf('room', byId).map((node) => node.id)).toEqual(['site', 'block']);
  });

  it('is empty for a top-level location', () => {
    expect(ancestorsOf('site', byId)).toEqual([]);
  });

  it('stops on a cycle', () => {
    const cyclic = buildLocationTree([location('a', 'b'), location('b', 'a')]).byId;
    expect(ancestorsOf('a', cyclic).length).toBeLessThanOrEqual(1);
  });
});

describe('rollUpTotals', () => {
  it('adds every level beneath a location to its own count', () => {
    const { roots } = buildLocationTree([
      location('block'),
      location('floor', 'block'),
      location('room', 'floor'),
      location('store'),
    ]);
    const totals = rollUpTotals(
      roots,
      new Map([
        ['block', 1],
        ['room', 4],
        ['store', 2],
      ]),
    );
    expect(totals.get('room')).toBe(4);
    expect(totals.get('floor')).toBe(4);
    expect(totals.get('block')).toBe(5);
    expect(totals.get('store')).toBe(2);
  });
});
