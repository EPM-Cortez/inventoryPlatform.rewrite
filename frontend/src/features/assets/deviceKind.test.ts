import { describe, expect, it } from 'vitest';

import { deviceKindFor, type DeviceKindSource } from './deviceKind';

function kind(source: DeviceKindSource) {
  return deviceKindFor(source);
}

const category = (name: string, code = name.toUpperCase()) => ({ name, code });

describe('deviceKindFor', () => {
  it('reads the categories this register actually uses', () => {
    expect(kind({ category: category('Monitor') })).toBe('monitor');
    expect(kind({ category: category('Desktops', 'DESKTOP') })).toBe('desktop');
    expect(kind({ category: category('Laptop') })).toBe('laptop');
    expect(
      kind({ category: category('Interactive Whiteboards', 'INTERACTIVE-WHITEBOARDS') }),
    ).toBe('interactivePanel');
    expect(kind({ category: category('Signage') })).toBe('signage');
    expect(kind({ category: category('TV') })).toBe('tv');
    expect(kind({ category: category('Visualisers') })).toBe('visualiser');
  });

  it('trusts the model over the category it was filed under', () => {
    expect(
      kind({
        category: category('Label printing test devices', 'LBL-ceb1985520'),
        manufacturer: 'Lenovo',
        model: 'ThinkPad L14 Gen 5',
      }),
    ).toBe('laptop');
    // A BRIX drives a signage screen, but it is a small box, not the screen.
    expect(
      kind({ category: category('Signage'), manufacturer: 'Gigabyte', model: 'BRIX' }),
    ).toBe('miniPc');
  });

  it('recognises the models in the register', () => {
    expect(kind({ manufacturer: 'Dell', model: 'OptiPlex 3090' })).toBe('desktop');
    expect(kind({ manufacturer: 'HP', model: 'HP ProDesk 400 G7' })).toBe('desktop');
    expect(kind({ manufacturer: 'StonePC', model: 'BOAMOT-524' })).toBe('desktop');
    expect(kind({ manufacturer: 'HP', model: 'HP 250/255 G7' })).toBe('laptop');
    expect(kind({ manufacturer: 'Promethean', model: 'ActivPanel' })).toBe(
      'interactivePanel',
    );
    expect(kind({ manufacturer: 'LG', model: '43UT640S' })).toBe('tv');
    expect(kind({ manufacturer: 'Elmo', model: 'MX-P3' })).toBe('visualiser');
  });

  it('tells tablets, phones and laptops from Apple apart', () => {
    expect(kind({ manufacturer: 'Apple', model: 'iPad (10th generation)' })).toBe('tablet');
    expect(kind({ manufacturer: 'Apple', model: 'iPhone 15' })).toBe('phone');
    expect(kind({ manufacturer: 'Apple', model: 'MacBook Air M2' })).toBe('laptop');
    expect(kind({ manufacturer: 'Apple', model: 'iMac 24' })).toBe('allInOne');
    expect(kind({ manufacturer: 'Apple', model: 'Mac mini' })).toBe('miniPc');
  });

  it('spots small form factors before calling something a desktop', () => {
    expect(kind({ manufacturer: 'Dell', model: 'OptiPlex 7010 Micro' })).toBe('miniPc');
    expect(kind({ manufacturer: 'Lenovo', model: 'ThinkCentre M70q Tiny' })).toBe('miniPc');
  });

  it('falls back to the operating system when the model is blank', () => {
    expect(kind({ operatingSystem: 'iPadOS 17' })).toBe('tablet');
    expect(kind({ operatingSystem: 'ChromeOS' })).toBe('laptop');
  });

  it('does not mistake a Microsoft product for a micro PC', () => {
    expect(kind({ manufacturer: 'Microsoft', model: 'Surface Laptop 5' })).toBe('laptop');
    expect(kind({ manufacturer: 'Microsoft', model: 'Surface Pro 9' })).toBe('tablet');
  });

  it('draws something neutral when nothing is known', () => {
    expect(kind({})).toBe('generic');
    expect(kind({ category: category('Furniture') })).toBe('generic');
  });
});
