/**
 * Unit tests for lib/commands/app.ts: back, forward, getTitle, setWindowRect
 */
import {describe, it, expect, vi, beforeEach} from 'vitest';

import {back, forward, title, setWindowRect} from '../../../lib/commands/app';
import {Key} from '../../../lib/enums';
import {createMockDriver} from '../../fixtures/driver';

vi.mock('../../../lib/winapi/user32', () => ({
  getWindowAllHandlesForProcessIds: vi.fn().mockReturnValue([]),
  trySetForegroundWindow: vi.fn().mockReturnValue(true),
  keyDown: vi.fn(),
  keyUp: vi.fn(),
}));

const ELEMENT_ID = '1.2.3.4.5';

describe('back', () => {
  beforeEach(() => vi.clearAllMocks());

  it('sends Alt+Left when a window is active', async () => {
    const {keyDown, keyUp} = await import('../../../lib/winapi/user32');
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValue(ELEMENT_ID);

    await back.call(driver);

    expect(keyDown).toHaveBeenNthCalledWith(1, Key.ALT);
    expect(keyDown).toHaveBeenNthCalledWith(2, Key.LEFT);
    expect(keyUp).toHaveBeenNthCalledWith(1, Key.LEFT);
    expect(keyUp).toHaveBeenNthCalledWith(2, Key.ALT);
  });

  it('throws NoSuchWindowError when no active window', async () => {
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValue('');

    await expect(back.call(driver)).rejects.toThrow('No active window found');
  });

  it('performs exactly one sendCommand call (window check) before sending keys', async () => {
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValue(ELEMENT_ID);

    await back.call(driver);

    expect(driver.sendCommand).toHaveBeenCalledTimes(1);
    expect(driver.sendCommand).toHaveBeenCalledWith('saveRootElementToTable', {});
  });
});

describe('forward', () => {
  beforeEach(() => vi.clearAllMocks());

  it('sends Alt+Right when a window is active', async () => {
    const {keyDown, keyUp} = await import('../../../lib/winapi/user32');
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValue(ELEMENT_ID);

    await forward.call(driver);

    expect(keyDown).toHaveBeenNthCalledWith(1, Key.ALT);
    expect(keyDown).toHaveBeenNthCalledWith(2, Key.RIGHT);
    expect(keyUp).toHaveBeenNthCalledWith(1, Key.RIGHT);
    expect(keyUp).toHaveBeenNthCalledWith(2, Key.ALT);
  });

  it('throws NoSuchWindowError when no active window', async () => {
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValue('');

    await expect(forward.call(driver)).rejects.toThrow('No active window found');
  });
});

describe('title (getTitle)', () => {
  beforeEach(() => vi.clearAllMocks());

  it('returns the window title from the Name property', async () => {
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValueOnce(ELEMENT_ID).mockResolvedValueOnce('Untitled - Notepad');

    const result = await title.call(driver);

    expect(result).toBe('Untitled - Notepad');
    expect(driver.sendCommand).toHaveBeenCalledTimes(2);
    expect(driver.sendCommand).toHaveBeenNthCalledWith(1, 'saveRootElementToTable', {});
    expect(driver.sendCommand).toHaveBeenNthCalledWith(2, 'getProperty', {elementId: ELEMENT_ID, property: 'Name'});
  });

  it('returns an empty string when the window has no title', async () => {
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValueOnce(ELEMENT_ID).mockResolvedValueOnce('');

    const result = await title.call(driver);

    expect(result).toBe('');
  });

  it('throws NoSuchWindowError when no active window', async () => {
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValue('');

    await expect(title.call(driver)).rejects.toThrow('No active window found');
  });
});

describe('setWindowRect', () => {
  beforeEach(() => vi.clearAllMocks());

  // Validation, restore, merging omitted values and the resize-then-move order happen in
  // WincoreServer's setWindowRect RPC (csharp/WincoreServer.Tests/WindowRectTests.cs).
  it('forwards the values to the setWindowRect RPC on the root window and returns its rect', async () => {
    const driver = createMockDriver() as any;
    const newRect = {x: 200, y: 300, width: 1024, height: 768};
    driver.sendCommand.mockImplementation(async (command: string) =>
      command === 'setWindowRect' ? newRect : ELEMENT_ID,
    );

    const result = await setWindowRect.call(driver, 200, null, 1024, undefined);

    expect(driver.sendCommand.mock.calls).toEqual([
      ['saveRootElementToTable', {}],
      ['setWindowRect', {elementId: ELEMENT_ID, x: 200, y: null, width: 1024, height: undefined}],
    ]);
    expect(result).toEqual(newRect);
  });

  it('throws NoSuchWindowError when no active window', async () => {
    const driver = createMockDriver() as any;
    driver.sendCommand.mockResolvedValue('');

    await expect(setWindowRect.call(driver, 0, 0, 800, 600)).rejects.toThrow('No active window found');
    expect(driver.sendCommand).not.toHaveBeenCalledWith('setWindowRect', expect.anything());
  });
});
