import { beforeEach, describe, expect, test, vi } from 'vitest';

import {
    WALLPAPER_PAUSE_ACTIONS,
    WALLPAPER_PAUSE_DEFAULTS,
    WALLPAPER_PAUSE_KEYS,
    WALLPAPER_PAUSE_TRIGGERS,
    applyWallpaperPauseSettings,
    normalizeWallpaperPauseSettings
} from '../wallpaperPause';

let setWallpaperPauseSettings;

beforeEach(() => {
    vi.clearAllMocks();
    setWallpaperPauseSettings = vi.fn().mockResolvedValue();
    globalThis.AppApi = { SetWallpaperPauseSettings: setWallpaperPauseSettings };
});

describe('wallpaperPause constants', () => {
    test('defaults are off with safe values', () => {
        expect(WALLPAPER_PAUSE_DEFAULTS).toEqual({
            enabled: false,
            trigger: 'vrchat',
            action: 'stop',
            resumeOnExit: true
        });
    });

    test('allowed triggers and actions', () => {
        expect(WALLPAPER_PAUSE_TRIGGERS).toEqual(['vrchat', 'steamvr']);
        expect(WALLPAPER_PAUSE_ACTIONS).toEqual(['stop', 'pause']);
    });

    test('config keys are stable and prefixed', () => {
        expect(Object.values(WALLPAPER_PAUSE_KEYS)).toEqual(
            expect.arrayContaining([
                'VRCX_wallpaperPauseEnabled',
                'VRCX_wallpaperPauseTrigger',
                'VRCX_wallpaperPauseAction',
                'VRCX_wallpaperPauseResumeOnExit'
            ])
        );
    });
});

describe('normalizeWallpaperPauseSettings', () => {
    test('keeps valid input untouched', () => {
        const input = {
            enabled: true,
            trigger: 'steamvr',
            action: 'pause',
            resumeOnExit: false
        };
        expect(normalizeWallpaperPauseSettings(input)).toEqual(input);
    });

    test('falls back to previous values for invalid trigger', () => {
        const previous = { ...WALLPAPER_PAUSE_DEFAULTS, trigger: 'steamvr' };
        const result = normalizeWallpaperPauseSettings(
            { enabled: true, trigger: 'garbage', action: 'pause', resumeOnExit: true },
            previous
        );
        expect(result.trigger).toBe('steamvr');
    });

    test('falls back to defaults for invalid trigger with no previous', () => {
        const result = normalizeWallpaperPauseSettings({
            enabled: true,
            trigger: 'garbage',
            action: 'stop',
            resumeOnExit: true
        });
        expect(result.trigger).toBe('vrchat');
    });

    test('falls back for invalid action', () => {
        const previous = { ...WALLPAPER_PAUSE_DEFAULTS, action: 'pause' };
        const result = normalizeWallpaperPauseSettings(
            { enabled: true, trigger: 'vrchat', action: 'garbage', resumeOnExit: true },
            previous
        );
        expect(result.action).toBe('pause');
    });

    test('coerces booleans', () => {
        const result = normalizeWallpaperPauseSettings({
            enabled: 'yes',
            trigger: 'vrchat',
            action: 'stop',
            resumeOnExit: 0
        });
        expect(result.enabled).toBe(true);
        expect(result.resumeOnExit).toBe(false);
    });
});

describe('applyWallpaperPauseSettings', () => {
    test('forwards the four settings to AppApi in order', async () => {
        await applyWallpaperPauseSettings({
            enabled: true,
            trigger: 'steamvr',
            action: 'pause',
            resumeOnExit: false
        });

        expect(setWallpaperPauseSettings).toHaveBeenCalledTimes(1);
        expect(setWallpaperPauseSettings).toHaveBeenCalledWith(
            true,
            'steamvr',
            'pause',
            false
        );
    });
});
