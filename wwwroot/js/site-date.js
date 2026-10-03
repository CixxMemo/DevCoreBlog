// Keep recovery timestamps independent of the browser locale and device timezone.
export function formatRecoveryDate(value, timeZone) {
    const date = new Date(value);
    if (!value || !Number.isFinite(date.getTime())) return 'unknown time';
    const parts = new Intl.DateTimeFormat('en-US', {
        timeZone, month: 'short', day: '2-digit', year: 'numeric',
        hour: '2-digit', minute: '2-digit', hourCycle: 'h23'
    }).formatToParts(date);
    const part = type => parts.find(item => item.type === type).value;
    return `${part('month')} ${part('day')}, ${part('year')} ${part('hour')}:${part('minute')} (${timeZone})`;
}
