// Format explicit instants in the site's timezone, never the device locale or default timezone.
export function formatSiteDate(value, timeZone, includeTime = false) {
    try {
        if (typeof value !== 'string' || value.length > 40 ||
            !/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(value) ||
            typeof timeZone !== 'string' || !timeZone.trim()) return 'Zaman gösterilemedi.';
        const date = new Date(value);
        if (!Number.isFinite(date.getTime())) return 'Zaman gösterilemedi.';
        const parts = new Intl.DateTimeFormat('tr-TR', {
            timeZone, month: 'short', day: '2-digit', year: 'numeric',
            ...(includeTime ? { hour: '2-digit', minute: '2-digit', hourCycle: 'h23' } : {})
        }).formatToParts(date);
        const part = type => parts.find(item => item.type === type).value;
        const result = `${part('day')} ${part('month')} ${part('year').padStart(4, '0')}`;
        return includeTime ? `${result} ${part('hour')}:${part('minute')} (${timeZone})` : result;
    } catch { return 'Zaman gösterilemedi.'; }
}

export function formatRecoveryDate(value, timeZone) {
    return formatSiteDate(value, timeZone, true);
}
