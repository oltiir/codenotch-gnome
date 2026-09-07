import GObject from 'gi://GObject';
import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import St from 'gi://St';
import Clutter from 'gi://Clutter';
import Soup from 'gi://Soup?version=3.0';
import Cairo from 'cairo';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

// ---- knobs -----------------------------------------------------------------

const ENDPOINT = 'http://127.0.0.1:8787/usage';
const POLL_SECONDS = 30;
const SHOW_EDGE_NOTCH = true;
const RING_SIZE = 46;        // px, the dial in the notch
const PANEL_RING_SIZE = 14;  // px, the mini dial in the top bar
const BAR_WIDTH = 120;       // px, bars in the hover callout and the popup

// Percent *used* at which the colour turns. Mirrors claude.ai's usage panel.
const WARN_AT = 70;
const CRITICAL_AT = 90;

const PROVIDERS = {
    claude:  {name: 'Claude',  glyph: '✱'},
    codex:   {name: 'Codex',   glyph: '◎'},
    cursor:  {name: 'Cursor',  glyph: '△'},
    copilot: {name: 'Copilot', glyph: '⌘'},
    gemini:  {name: 'Gemini',  glyph: '✦'},
};

// Usage-state colours, shared by CSS classes below and the Cairo rings.
const TONE = {
    ok:       {cls: 'cn-ok',       rgb: [0.34, 0.89, 0.54]},   // #57e389
    warn:     {cls: 'cn-warn',     rgb: [0.97, 0.89, 0.36]},   // #f8e45c
    critical: {cls: 'cn-critical', rgb: [1.00, 0.48, 0.39]},   // #ff7b63
    stale:    {cls: 'cn-stale',    rgb: [0.60, 0.60, 0.59]},   // #9a9996
};
const TRACK_RGBA = [1, 1, 1, 0.12];

// ---- parsing ---------------------------------------------------------------

// `codexbar serve` mirrors the CLI's JSON, which is a single object for one
// provider and a collection for several. Be liberal about the envelope.
function providersFrom(json) {
    if (Array.isArray(json))
        return json;
    if (!json || typeof json !== 'object')
        return [];
    for (const key of ['providers', 'usages', 'results', 'items']) {
        if (Array.isArray(json[key]))
            return json[key];
    }
    if (json.usage || json.provider)
        return [json];
    return [];
}

function clampPct(n) {
    return Math.max(0, Math.min(100, Math.round(n)));
}

// Every rate window CodexBar knows about for one provider, in display order
// and named the way claude.ai names them: the session window, then the weekly
// windows -- the all-models cap and any scoped ones such as a per-model cap.
// `group` lets the detail view put a "Weekly limits" divider before the
// weekly rows; `key` lets the notch pick out session/weekly for its rings.
function windowsFrom(entry) {
    const usage = entry?.usage ?? {};
    const out = [];
    const push = (key, group, label, w) => {
        if (w && typeof w.usedPercent === 'number')
            out.push({key, group, label, used: clampPct(w.usedPercent), resetsAt: w.resetsAt ?? null});
    };
    push('session', 'session', 'Current session', usage.primary);
    push('weekly', 'weekly', 'All models', usage.secondary);
    push('tertiary', 'weekly', 'Model', usage.tertiary);
    for (const extra of usage.extraRateWindows ?? []) {
        // CodexBar titles these "Fable only"; claude.ai just says "Fable".
        const label = (extra.title ?? 'Scoped').replace(/\s+only$/i, '');
        push(extra.id ?? 'extra', 'weekly', label, extra.window);
    }
    return out;
}

function tone(used) {
    if (used >= CRITICAL_AT)
        return TONE.critical;
    if (used >= WARN_AT)
        return TONE.warn;
    return TONE.ok;
}

// "59 min", "13 hr 49 min", "2 days 3 hr" -- claude.ai's phrasing.
function countdown(iso) {
    if (!iso)
        return '';
    const ms = Date.parse(iso) - Date.now();
    if (!Number.isFinite(ms))
        return '';
    if (ms <= 0)
        return 'now';
    const mins = Math.round(ms / 60000);
    if (mins < 60)
        return `${mins} min`;
    const hours = Math.floor(mins / 60);
    if (hours < 24)
        return mins % 60 ? `${hours} hr ${mins % 60} min` : `${hours} hr`;
    const days = Math.floor(hours / 24);
    const rem = hours % 24;
    return `${days} ${days === 1 ? 'day' : 'days'}${rem ? ` ${rem} hr` : ''}`;
}

// "17:10" for today, "Tue 06:00" otherwise.
function clockText(iso) {
    if (!iso)
        return '';
    const dt = GLib.DateTime.new_from_iso8601(iso, null)?.to_local();
    if (!dt)
        return '';
    const sameDay = dt.format('%Y-%m-%d') === GLib.DateTime.new_now_local().format('%Y-%m-%d');
    return dt.format(sameDay ? '%H:%M' : '%a %H:%M');
}

function resetLine(iso) {
    const cd = countdown(iso);
    if (!cd)
        return '';
    if (cd === 'now')
        return 'Resetting';
    const clock = clockText(iso);
    return clock ? `Resets in ${cd}  ·  ${clock}` : `Resets in ${cd}`;
}

// ---- widgets ---------------------------------------------------------------

// Concentric dials: the outer ring is the session window, the inner one the
// weekly window. Both fill clockwise as you use them -- an empty ring means
// nothing used -- and the colour tracks the session window's severity.
const Rings = GObject.registerClass(
class Rings extends St.DrawingArea {
    _init(size) {
        super._init({width: size, height: size, style_class: 'cn-rings'});
        this._session = null;   // fraction used, 0..1, or null for no data
        this._weekly = null;
        this._tone = TONE.stale;
        this.connect('repaint', () => this._paint());
    }

    setWindows(sessionUsed, weeklyUsed) {
        this._session = sessionUsed === null ? null : sessionUsed / 100;
        this._weekly = weeklyUsed === null ? null : weeklyUsed / 100;
        this._tone = sessionUsed === null ? TONE.stale : tone(sessionUsed);
        this.queue_repaint();
    }

    _paint() {
        const cr = this.get_context();
        const [w, h] = this.get_surface_size();
        const cx = w / 2, cy = h / 2;
        const outerW = Math.max(2.5, w * 0.095);
        const innerW = Math.max(1.5, w * 0.06);
        const rOuter = w / 2 - outerW / 2 - 0.5;
        const rInner = rOuter - outerW / 2 - innerW / 2 - 2.5;
        const top = -Math.PI / 2;

        cr.setLineCap(Cairo.LineCap.ROUND);

        const ring = (r, lw, frac, rgb, alpha) => {
            cr.setLineWidth(lw);
            cr.setSourceRGBA(...TRACK_RGBA);
            cr.arc(cx, cy, r, 0, 2 * Math.PI);
            cr.stroke();
            if (frac !== null && frac > 0) {
                cr.setSourceRGBA(rgb[0], rgb[1], rgb[2], alpha);
                cr.arc(cx, cy, r, top, top + 2 * Math.PI * Math.min(1, frac));
                cr.stroke();
            }
        };

        ring(rOuter, outerW, this._session, this._tone.rgb, 1.0);
        if (this._weekly !== null || rInner > 3)
            ring(rInner, innerW, this._weekly, this._tone.rgb, 0.55);

        cr.$dispose();
    }
});

// A dial with the provider's glyph sitting in the middle of it.
const Dial = GObject.registerClass(
class Dial extends St.Widget {
    _init(size, glyph) {
        super._init({
            layout_manager: new Clutter.BinLayout(),
            width: size,
            height: size,
        });
        this.rings = new Rings(size);
        this.add_child(this.rings);
        this.glyph = new St.Label({
            text: glyph,
            style_class: 'cn-glyph',
            x_align: Clutter.ActorAlign.CENTER,
            y_align: Clutter.ActorAlign.CENTER,
        });
        this.add_child(this.glyph);
    }
});

// A bar that fills left-to-right with percent used.
const UsageBar = GObject.registerClass(
class UsageBar extends St.Bin {
    _init(width) {
        super._init({
            style_class: 'cn-bar-track',
            style: `width: ${width}px;`,
            y_align: Clutter.ActorAlign.CENTER,
        });
        this._width = width;
        this._fill = new St.Widget({
            style_class: 'cn-bar-fill cn-ok',
            x_align: Clutter.ActorAlign.START,
            y_expand: true,
        });
        this.set_child(this._fill);
    }

    setUsed(used) {
        const px = used <= 0 ? 0 : Math.max(3, Math.round((this._width * used) / 100));
        this._fill.style = `width: ${px}px;`;
        this._fill.style_class = `cn-bar-fill ${tone(used).cls}`;
    }
});

// One provider's block, laid out like claude.ai's usage panel: the window
// name with its reset time stacked underneath on the left, a bar in the
// middle, "NN% used" on the right, and a "Weekly limits" divider before the
// weekly rows.
function buildDetail(provider, windows, opts = {}) {
    const box = new St.BoxLayout({vertical: true, style_class: 'cn-detail'});

    const head = new St.BoxLayout({style_class: 'cn-detail-head'});
    head.add_child(new St.Label({text: provider.glyph, style_class: 'cn-detail-glyph'}));
    head.add_child(new St.Label({text: provider.name, style_class: 'cn-detail-name'}));
    box.add_child(head);

    let dividerDone = false;
    for (const w of windows) {
        if (w.group === 'weekly' && !dividerDone) {
            box.add_child(new St.Label({text: 'Weekly limits', style_class: 'cn-detail-group'}));
            dividerDone = true;
        }

        const row = new St.BoxLayout({style_class: 'cn-detail-row'});

        const left = new St.BoxLayout({vertical: true, style_class: 'cn-detail-left'});
        left.add_child(new St.Label({text: w.label, style_class: 'cn-detail-label'}));
        const reset = resetLine(w.resetsAt);
        if (reset)
            left.add_child(new St.Label({text: reset, style_class: 'cn-detail-reset'}));
        row.add_child(left);

        const bar = new UsageBar(opts.barWidth ?? BAR_WIDTH);
        bar.setUsed(w.used);
        row.add_child(bar);

        row.add_child(new St.Label({
            text: `${w.used}% used`,
            style_class: `cn-detail-pct ${tone(w.used).cls}`,
            y_align: Clutter.ActorAlign.CENTER,
        }));
        box.add_child(row);
    }
    return box;
}

const Indicator = GObject.registerClass(
class Indicator extends PanelMenu.Button {
    _init() {
        super._init(0.0, 'Codenotch', false);

        // Top bar: a mini dial and the highest percent used across providers.
        const panelBox = new St.BoxLayout({style_class: 'cn-panel'});
        this._panelRings = new Rings(PANEL_RING_SIZE);
        this._panelRings.y_align = Clutter.ActorAlign.CENTER;
        panelBox.add_child(this._panelRings);
        this._label = new St.Label({
            text: '—',
            y_align: Clutter.ActorAlign.CENTER,
            style_class: 'cn-panel-label',
        });
        panelBox.add_child(this._label);
        this.add_child(panelBox);

        this._section = new PopupMenu.PopupMenuSection();
        this.menu.addMenuItem(this._section);
        this.menu.addMenuItem(new PopupMenu.PopupSeparatorMenuItem());
        this.menu.addAction('Refresh now', () => this._fetch());

        this._session = new Soup.Session({timeout: 10});
        this._cancellable = new Gio.Cancellable();
        this._notch = null;
        this._dials = null;
        this._callout = null;
        this._placeId = 0;

        if (SHOW_EDGE_NOTCH)
            this._buildNotch();

        this._monitorsId = Main.layoutManager.connect('monitors-changed',
            () => this._queuePlaceNotch());

        this._fetch();
        this._timerId = GLib.timeout_add_seconds(GLib.PRIORITY_DEFAULT,
            POLL_SECONDS, () => {
                this._fetch();
                return GLib.SOURCE_CONTINUE;
            });
    }

    // -- the edge notch ------------------------------------------------------

    _buildNotch() {
        // [ callout (hidden until hover) ][ dials column ] flush to the right edge.
        this._notch = new St.BoxLayout({
            style_class: 'cn-notch',
            reactive: true,
            track_hover: true,
        });

        this._callout = new St.BoxLayout({
            vertical: true,
            style_class: 'cn-callout',
            visible: false,
            opacity: 0,
        });
        this._notch.add_child(this._callout);

        this._dials = new St.BoxLayout({vertical: true, style_class: 'cn-dials'});
        this._notch.add_child(this._dials);

        this._notch.connect('notify::hover', () => this._setOpen(this._notch.hover));
        this._notch.connect('button-press-event', () => {
            this.menu.toggle();
            return Clutter.EVENT_STOP;
        });

        Main.layoutManager.addChrome(this._notch, {
            affectsStruts: false,
            trackFullscreen: true,
        });
    }

    _setOpen(open) {
        if (!this._callout)
            return;
        this._callout.remove_all_transitions();
        if (open) {
            this._notch.add_style_class_name('cn-notch-open');
            this._callout.show();
            this._callout.ease({
                opacity: 255,
                duration: 160,
                mode: Clutter.AnimationMode.EASE_OUT_QUAD,
            });
        } else {
            this._notch.remove_style_class_name('cn-notch-open');
            this._callout.ease({
                opacity: 0,
                duration: 120,
                mode: Clutter.AnimationMode.EASE_IN_QUAD,
                onComplete: () => {
                    this._callout?.hide();
                    this._queuePlaceNotch();
                },
            });
        }
        this._queuePlaceNotch();
    }

    _queuePlaceNotch() {
        if (!this._notch || this._placeId)
            return;
        this._placeId = GLib.idle_add(GLib.PRIORITY_DEFAULT_IDLE, () => {
            this._placeId = 0;
            this._placeNotch();
            return GLib.SOURCE_REMOVE;
        });
    }

    _placeNotch() {
        const mon = Main.layoutManager.primaryMonitor;
        if (!mon || !this._notch)
            return;
        const [, natWidth] = this._notch.get_preferred_width(-1);
        const [, natHeight] = this._notch.get_preferred_height(natWidth);
        this._notch.set_position(
            mon.x + mon.width - natWidth,
            mon.y + Math.round((mon.height - natHeight) / 2));
    }

    // -- data ----------------------------------------------------------------

    _fetch() {
        const msg = Soup.Message.new('GET', ENDPOINT);
        if (!msg) {
            this._renderError('bad endpoint');
            return;
        }
        this._session.send_and_read_async(msg, GLib.PRIORITY_DEFAULT,
            this._cancellable, (session, res) => {
                try {
                    const bytes = session.send_and_read_finish(res);
                    const status = msg.get_status();
                    if (status !== Soup.Status.OK)
                        throw new Error(`HTTP ${status}`);
                    const text = new TextDecoder().decode(bytes.get_data());
                    this._render(providersFrom(JSON.parse(text)));
                } catch (e) {
                    if (!e.matches?.(Gio.IOErrorEnum, Gio.IOErrorEnum.CANCELLED))
                        this._renderError(e.message ?? String(e));
                }
            });
    }

    _renderError(reason) {
        this._label.text = '—';
        this._label.style_class = 'cn-panel-label cn-stale';
        this._panelRings.setWindows(null, null);

        this._section.removeAll();
        this._section.addMenuItem(new PopupMenu.PopupMenuItem(
            `No reading: ${reason}`, {reactive: false}));
        this._section.addMenuItem(new PopupMenu.PopupMenuItem(
            'Check: systemctl --user status codexbar-serve', {reactive: false}));

        if (this._dials) {
            this._dials.destroy_all_children();
            this._callout.destroy_all_children();
            const cell = new St.BoxLayout({vertical: true, style_class: 'cn-cell'});
            const dial = new Dial(RING_SIZE, '!');
            dial.rings.setWindows(null, null);
            cell.add_child(dial);
            cell.add_child(new St.Label({
                text: 'offline',
                style_class: 'cn-cell-name',
                x_align: Clutter.ActorAlign.CENTER,
            }));
            this._dials.add_child(cell);
            this._callout.add_child(new St.Label({
                text: `Can't reach codexbar serve\n${reason}`,
                style_class: 'cn-detail-reset',
            }));
            this._queuePlaceNotch();
        }
    }

    _render(entries) {
        this._section.removeAll();
        if (this._dials) {
            this._dials.destroy_all_children();
            this._callout.destroy_all_children();
        }

        let worst = null;

        for (const entry of entries) {
            const id = entry.provider ?? 'unknown';
            const provider = PROVIDERS[id] ?? {
                name: id.charAt(0).toUpperCase() + id.slice(1),
                glyph: id.charAt(0).toUpperCase(),
            };
            const windows = windowsFrom(entry);

            if (entry.error || windows.length === 0) {
                this._section.addMenuItem(new PopupMenu.PopupMenuItem(
                    `${provider.name}: no data`, {reactive: false}));
                continue;
            }

            const session = windows.find(w => w.key === 'session') ?? windows[0];
            const weekly = windows.find(w => w.key === 'weekly') ?? null;

            // Popup menu block.
            const item = new PopupMenu.PopupBaseMenuItem({reactive: false, can_focus: false});
            const detail = buildDetail(provider, windows, {barWidth: BAR_WIDTH + 40});
            const pace = entry.pace?.primary?.summary;
            if (pace)
                detail.add_child(new St.Label({text: pace, style_class: 'cn-detail-pace'}));
            item.add_child(detail);
            this._section.addMenuItem(item);

            // Notch: a dial per provider, and its block in the hover callout.
            if (this._dials) {
                const cell = new St.BoxLayout({vertical: true, style_class: 'cn-cell'});
                const dial = new Dial(RING_SIZE, provider.glyph);
                dial.rings.setWindows(session.used, weekly ? weekly.used : null);
                cell.add_child(dial);
                cell.add_child(new St.Label({
                    text: `${session.used}%`,
                    style_class: `cn-cell-pct ${tone(session.used).cls}`,
                    x_align: Clutter.ActorAlign.CENTER,
                }));
                if (weekly) {
                    cell.add_child(new St.Label({
                        text: `wk ${weekly.used}%`,
                        style_class: 'cn-cell-sub',
                        x_align: Clutter.ActorAlign.CENTER,
                    }));
                }
                this._dials.add_child(cell);
                this._callout.add_child(buildDetail(provider, windows));
            }

            if (!worst || session.used > worst.used)
                worst = {used: session.used, weekly: weekly?.used ?? null};
        }

        if (worst) {
            this._label.text = `${worst.used}%`;
            this._label.style_class = `cn-panel-label ${tone(worst.used).cls}`;
            this._panelRings.setWindows(worst.used, worst.weekly);
        } else {
            this._label.text = '—';
            this._label.style_class = 'cn-panel-label cn-stale';
            this._panelRings.setWindows(null, null);
            this._section.addMenuItem(new PopupMenu.PopupMenuItem(
                'No providers enabled', {reactive: false}));
        }

        this._queuePlaceNotch();
    }

    destroy() {
        if (this._timerId) {
            GLib.Source.remove(this._timerId);
            this._timerId = 0;
        }
        if (this._placeId) {
            GLib.Source.remove(this._placeId);
            this._placeId = 0;
        }
        if (this._monitorsId) {
            Main.layoutManager.disconnect(this._monitorsId);
            this._monitorsId = 0;
        }
        this._cancellable?.cancel();
        this._cancellable = null;
        this._session?.abort();
        this._session = null;
        if (this._notch) {
            this._callout?.remove_all_transitions();
            Main.layoutManager.removeChrome(this._notch);
            this._notch.destroy();
            this._notch = null;
            this._dials = null;
            this._callout = null;
        }
        super.destroy();
    }
});

export default class CodenotchExtension extends Extension {
    enable() {
        this._indicator = new Indicator();
        Main.panel.addToStatusArea(this.uuid, this._indicator, 0, 'right');
    }

    disable() {
        this._indicator?.destroy();
        this._indicator = null;
    }
}
