import GObject from 'gi://GObject';
import GLib from 'gi://GLib';
import Gio from 'gi://Gio';
import St from 'gi://St';
import Clutter from 'gi://Clutter';
import Soup from 'gi://Soup?version=3.0';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PanelMenu from 'resource:///org/gnome/shell/ui/panelMenu.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

// ---- knobs -----------------------------------------------------------------

const ENDPOINT = 'http://127.0.0.1:8787/usage';
const POLL_SECONDS = 30;
const SHOW_EDGE_NOTCH = true;
const BAR_WIDTH = 168; // px; must match .cn-bar-track width in stylesheet.css

const PROVIDER_NAMES = {
    claude: 'Claude Code',
    codex: 'Codex',
    cursor: 'Cursor',
    copilot: 'Copilot',
    gemini: 'Gemini',
};

const WINDOW_LABELS = [
    ['primary', 'Session'],
    ['secondary', 'Weekly'],
    ['tertiary', 'Model'],
];

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

function windowsFrom(entry) {
    const usage = entry?.usage ?? {};
    const out = [];
    for (const [key, label] of WINDOW_LABELS) {
        const w = usage[key];
        if (!w || typeof w.usedPercent !== 'number')
            continue;
        out.push({
            label,
            used: Math.max(0, Math.min(100, Math.round(w.usedPercent))),
            resetsAt: w.resetsAt ?? null,
        });
    }
    return out;
}

function severity(used) {
    if (used >= 90)
        return 'cn-critical';
    if (used >= 75)
        return 'cn-warn';
    return 'cn-ok';
}

function resetText(iso) {
    if (!iso)
        return '';
    const ms = Date.parse(iso) - Date.now();
    if (!Number.isFinite(ms))
        return '';
    if (ms <= 0)
        return 'resetting';
    const mins = Math.round(ms / 60000);
    if (mins < 60)
        return `resets in ${mins}m`;
    const hours = Math.floor(mins / 60);
    if (hours < 24)
        return `resets in ${hours}h ${mins % 60}m`;
    return `resets in ${Math.round(hours / 24)}d`;
}

// ---- widgets ---------------------------------------------------------------

const UsageBar = GObject.registerClass(
class UsageBar extends St.Bin {
    _init() {
        super._init({
            style_class: 'cn-bar-track',
            x_expand: false,
            y_align: Clutter.ActorAlign.CENTER,
        });
        this._fill = new St.Widget({
            style_class: 'cn-bar-fill cn-ok',
            x_align: Clutter.ActorAlign.START,
            y_expand: true,
        });
        this.set_child(this._fill);
    }

    setUsed(used) {
        const px = Math.max(2, Math.round((BAR_WIDTH * used) / 100));
        this._fill.style = `width: ${px}px;`;
        this._fill.style_class = `cn-bar-fill ${severity(used)}`;
    }
});

const Indicator = GObject.registerClass(
class Indicator extends PanelMenu.Button {
    _init() {
        super._init(0.0, 'Codenotch', false);

        this._label = new St.Label({
            text: '\u25CB',
            y_align: Clutter.ActorAlign.CENTER,
            style_class: 'cn-panel-label',
        });
        this.add_child(this._label);

        this._section = new PopupMenu.PopupMenuSection();
        this.menu.addMenuItem(this._section);
        this.menu.addMenuItem(new PopupMenu.PopupSeparatorMenuItem());
        this.menu.addAction('Refresh now', () => this._fetch());

        this._session = new Soup.Session({timeout: 10});
        this._cancellable = new Gio.Cancellable();
        this._notch = null;
        this._notchRows = null;
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
        this._notch = new St.BoxLayout({
            vertical: true,
            style_class: 'cn-notch',
            reactive: true,
            track_hover: true,
        });
        this._notchRows = new St.BoxLayout({vertical: true});
        this._notch.add_child(this._notchRows);

        this._notch.connect('notify::hover', () => {
            if (this._notch.hover)
                this._notch.add_style_class_name('cn-notch-open');
            else
                this._notch.remove_style_class_name('cn-notch-open');
            this._queuePlaceNotch();
        });
        this._notch.connect('button-press-event', () => {
            this.menu.toggle();
            return Clutter.EVENT_STOP;
        });

        Main.layoutManager.addChrome(this._notch, {
            affectsStruts: false,
            trackFullscreen: true,
        });
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
        this._label.text = '\u26A0';
        this._label.style_class = 'cn-panel-label cn-stale';
        this._section.removeAll();
        const item = new PopupMenu.PopupMenuItem(`No reading: ${reason}`, {
            reactive: false,
        });
        this._section.addMenuItem(item);
        const hint = new PopupMenu.PopupMenuItem(
            'Check: systemctl --user status codexbar-serve', {reactive: false});
        this._section.addMenuItem(hint);
        if (this._notchRows) {
            this._notchRows.destroy_all_children();
            const l = new St.Label({text: '\u26A0', style_class: 'cn-notch-pct cn-stale'});
            this._notchRows.add_child(l);
            this._queuePlaceNotch();
        }
    }

    _render(entries) {
        this._section.removeAll();
        if (this._notchRows)
            this._notchRows.destroy_all_children();

        let worst = null;

        for (const entry of entries) {
            const id = entry.provider ?? 'unknown';
            const name = PROVIDER_NAMES[id] ?? id;
            const windows = windowsFrom(entry);

            if (entry.error || windows.length === 0) {
                this._section.addMenuItem(new PopupMenu.PopupMenuItem(
                    `${name}: no data`, {reactive: false}));
                continue;
            }

            const item = new PopupMenu.PopupBaseMenuItem({
                reactive: false,
                can_focus: false,
            });
            const box = new St.BoxLayout({vertical: true, x_expand: true});
            box.add_child(new St.Label({text: name, style_class: 'cn-provider'}));

            for (const w of windows) {
                const row = new St.BoxLayout({style_class: 'cn-row', x_expand: true});
                row.add_child(new St.Label({
                    text: w.label,
                    style_class: 'cn-window',
                    y_align: Clutter.ActorAlign.CENTER,
                }));
                const bar = new UsageBar();
                bar.setUsed(w.used);
                row.add_child(bar);
                row.add_child(new St.Label({
                    text: `${100 - w.used}% left`,
                    style_class: `cn-pct ${severity(w.used)}`,
                    y_align: Clutter.ActorAlign.CENTER,
                }));
                box.add_child(row);

                const reset = resetText(w.resetsAt);
                if (reset) {
                    box.add_child(new St.Label({
                        text: reset,
                        style_class: 'cn-reset',
                    }));
                }
            }

            const pace = entry.pace?.primary?.summary;
            if (pace)
                box.add_child(new St.Label({text: pace, style_class: 'cn-reset'}));

            item.add_child(box);
            this._section.addMenuItem(item);

            // Notch: one line per provider, the session window only.
            const session = windows[0];
            if (this._notchRows) {
                const cell = new St.BoxLayout({vertical: true, style_class: 'cn-notch-cell'});
                cell.add_child(new St.Label({
                    text: `${100 - session.used}`,
                    style_class: `cn-notch-pct ${severity(session.used)}`,
                }));
                cell.add_child(new St.Label({
                    text: name.split(' ')[0],
                    style_class: 'cn-notch-name',
                }));
                this._notchRows.add_child(cell);
            }

            if (!worst || session.used > worst.used)
                worst = {used: session.used, name};
        }

        if (worst) {
            this._label.text = `${100 - worst.used}%`;
            this._label.style_class = `cn-panel-label ${severity(worst.used)}`;
        } else {
            this._label.text = '\u25CB';
            this._label.style_class = 'cn-panel-label';
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
            Main.layoutManager.removeChrome(this._notch);
            this._notch.destroy();
            this._notch = null;
            this._notchRows = null;
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
