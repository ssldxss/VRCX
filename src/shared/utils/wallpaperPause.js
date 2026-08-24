/**
 * Shared settings contract for the "pause/stop Wallpaper Engine in VR"
 * feature. The store and UI stay thin; the defaults, allowed values,
 * config keys and AppApi call shape live here so they are testable.
 */

export const WALLPAPER_PAUSE_KEYS = {
    enabled: 'VRCX_wallpaperPauseEnabled',
    trigger: 'VRCX_wallpaperPauseTrigger',
    action: 'VRCX_wallpaperPauseAction',
    resumeOnExit: 'VRCX_wallpaperPauseResumeOnExit'
};

export const WALLPAPER_PAUSE_DEFAULTS = {
    enabled: false,
    trigger: 'vrchat',
    action: 'stop',
    resumeOnExit: true
};

export const WALLPAPER_PAUSE_TRIGGERS = ['vrchat', 'steamvr'];
export const WALLPAPER_PAUSE_ACTIONS = ['stop', 'pause'];

/**
 * Normalizes raw wallpaper-pause settings. Invalid trigger/action values
 * fall back to the previous (or default) values instead of being applied.
 * @param {object} input
 * @param {object} [previous]
 * @returns {{enabled: boolean, trigger: string, action: string, resumeOnExit: boolean}}
 */
export function normalizeWallpaperPauseSettings(
    input,
    previous = WALLPAPER_PAUSE_DEFAULTS
) {
    return {
        enabled: !!input.enabled,
        trigger: WALLPAPER_PAUSE_TRIGGERS.includes(input.trigger)
            ? input.trigger
            : previous.trigger,
        action: WALLPAPER_PAUSE_ACTIONS.includes(input.action)
            ? input.action
            : previous.action,
        resumeOnExit: !!input.resumeOnExit
    };
}

/**
 * Pushes the settings to the .NET side.
 * @param {{enabled: boolean, trigger: string, action: string, resumeOnExit: boolean}} settings
 */
export function applyWallpaperPauseSettings(settings) {
    return AppApi.SetWallpaperPauseSettings(
        settings.enabled,
        settings.trigger,
        settings.action,
        settings.resumeOnExit
    );
}
