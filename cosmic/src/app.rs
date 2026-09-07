// SPDX-License-Identifier: MIT

use crate::draw::{Bar, Rings};
use crate::usage::{self, Provider, Tone};
use cosmic::app::Task;
use cosmic::applet::cosmic_panel_config::PanelAnchor;
use cosmic::iced::platform_specific::shell::wayland::commands::popup::{destroy_popup, get_popup};
use cosmic::iced::widget::{column, row, stack};
use cosmic::iced::{Alignment, Color, Length, Limits, Subscription, window::Id};
use cosmic::widget::{button, canvas, container, text};
use cosmic::{Element, theme};
use std::time::Instant;

const POPUP_RING: f32 = 56.0;
const BAR_WIDTH: f32 = 150.0;
const BAR_HEIGHT: f32 = 6.0;
const LABEL_WIDTH: f32 = 160.0;

pub struct Codenotch {
    core: cosmic::Core,
    popup: Option<Id>,
    providers: Vec<Provider>,
    /// Last fetch failure; the previous reading stays on screen, greyed.
    error: Option<String>,
    last_ok: Option<Instant>,
}

#[derive(Debug, Clone)]
pub enum Message {
    TogglePopup,
    PopupClosed(Id),
    Tick,
    Fetched(Result<Vec<Provider>, String>),
}

fn fetch_task() -> Task<Message> {
    Task::perform(usage::fetch(), |r| cosmic::Action::App(Message::Fetched(r)))
}

fn tone_text_color(tone: Tone) -> Color {
    let (r, g, b) = tone.rgb();
    Color { r, g, b, a: 1.0 }
}

impl Codenotch {
    fn stale(&self) -> bool {
        self.error.is_some()
    }

    /// The provider with the highest session usage drives the panel item.
    fn worst(&self) -> Option<&Provider> {
        self.providers
            .iter()
            .filter(|p| p.session().is_some())
            .max_by_key(|p| p.session().map(|w| w.used).unwrap_or(0))
    }

    fn rings_for(&self, p: &Provider) -> Rings {
        let session = p.session().map(|w| f32::from(w.used) / 100.0);
        let weekly = p.weekly().map(|w| f32::from(w.used) / 100.0);
        let tone = if self.stale() {
            Tone::Stale
        } else {
            p.session().map(|w| Tone::of(w.used)).unwrap_or(Tone::Stale)
        };
        Rings { session, weekly, tone }
    }

    fn provider_block(&self, p: &Provider) -> Element<'_, Message> {
        let rings = self.rings_for(p);
        let dial: Element<_> = stack![
            canvas(rings)
                .width(Length::Fixed(POPUP_RING))
                .height(Length::Fixed(POPUP_RING)),
            container(text::title4(p.glyph))
                .width(Length::Fill)
                .height(Length::Fill)
                .align_x(Alignment::Center)
                .align_y(Alignment::Center),
        ]
        .width(Length::Fixed(POPUP_RING))
        .height(Length::Fixed(POPUP_RING))
        .into();

        let mut head_text: Vec<Element<_>> = vec![text::heading(p.name.clone()).into()];
        if let Some(s) = p.session() {
            head_text.push(text::caption(s.reset_line()).into());
        }
        let head = row![
            dial,
            column(head_text).spacing(2).align_x(Alignment::Start),
        ]
        .spacing(14)
        .align_y(Alignment::Center);

        let mut rows: Vec<Element<_>> = vec![head.into()];

        if let Some(err) = &p.error {
            rows.push(text::caption(format!("No data: {err}")).into());
            return column(rows).spacing(10).into();
        }

        let mut divider_done = false;
        for w in &p.windows {
            if w.weekly_group && !divider_done {
                rows.push(text::caption_heading("Weekly limits").into());
                divider_done = true;
            }
            let tone = if self.stale() { Tone::Stale } else { Tone::of(w.used) };

            let left = column![
                text::body(w.label.clone()),
                text::caption(w.reset_line()),
            ]
            .spacing(1)
            .width(Length::Fixed(LABEL_WIDTH));

            let bar = canvas(Bar { used: w.used, tone })
                .width(Length::Fixed(BAR_WIDTH))
                .height(Length::Fixed(BAR_HEIGHT));

            let pct = text::body(format!("{}% used", w.used))
                .class(theme::Text::Color(tone_text_color(tone)))
                .width(Length::Fixed(72.0))
                .align_x(Alignment::End);

            rows.push(
                row![left, bar, pct]
                    .spacing(12)
                    .align_y(Alignment::Center)
                    .into(),
            );
        }

        column(rows).spacing(10).into()
    }
}

impl cosmic::Application for Codenotch {
    type Executor = cosmic::executor::Default;
    type Flags = ();
    type Message = Message;
    const APP_ID: &'static str = "com.github.oltiir.Codenotch";

    fn core(&self) -> &cosmic::Core {
        &self.core
    }

    fn core_mut(&mut self) -> &mut cosmic::Core {
        &mut self.core
    }

    fn init(core: cosmic::Core, _flags: Self::Flags) -> (Self, Task<Self::Message>) {
        let app = Codenotch {
            core,
            popup: None,
            providers: Vec::new(),
            error: None,
            last_ok: None,
        };
        (app, fetch_task())
    }

    fn on_close_requested(&self, id: Id) -> Option<Message> {
        Some(Message::PopupClosed(id))
    }

    fn subscription(&self) -> Subscription<Self::Message> {
        cosmic::iced::time::every(usage::POLL).map(|_| Message::Tick)
    }

    fn update(&mut self, message: Self::Message) -> Task<Self::Message> {
        match message {
            Message::Tick => return fetch_task(),
            Message::Fetched(Ok(providers)) => {
                self.providers = providers;
                self.error = None;
                self.last_ok = Some(Instant::now());
            }
            Message::Fetched(Err(e)) => {
                self.error = Some(e);
            }
            Message::TogglePopup => {
                return if let Some(p) = self.popup.take() {
                    destroy_popup(p)
                } else {
                    let new_id = Id::unique();
                    self.popup.replace(new_id);
                    let mut settings = self.core.applet.get_popup_settings(
                        self.core.main_window_id().unwrap(),
                        new_id,
                        None,
                        None,
                        None,
                    );
                    settings.positioner.size_limits = Limits::NONE
                        .min_width(320.0)
                        .max_width(480.0)
                        .min_height(120.0)
                        .max_height(900.0);
                    get_popup(settings)
                };
            }
            Message::PopupClosed(id) => {
                if self.popup.as_ref() == Some(&id) {
                    self.popup = None;
                }
            }
        }
        Task::none()
    }

    /// The panel item: a mini dial plus the highest percent used.
    fn view(&self) -> Element<'_, Self::Message> {
        let horizontal = matches!(self.core.applet.anchor, PanelAnchor::Top | PanelAnchor::Bottom);
        let (size, _) = self.core.applet.suggested_size(true);
        let (pad_w, pad_h) = self.core.applet.suggested_padding(true);
        let size = f32::from(size);

        let (rings, label, tone) = match self.worst() {
            Some(p) => {
                let s = p.session().expect("worst() only yields providers with a session");
                let tone = if self.stale() { Tone::Stale } else { Tone::of(s.used) };
                (self.rings_for(p), format!("{}%", s.used), tone)
            }
            None => (Rings::offline(), "\u{2014}".to_string(), Tone::Stale),
        };

        let dial: Element<_> = canvas(rings)
            .width(Length::Fixed(size))
            .height(Length::Fixed(size))
            .into();
        let pct: Element<_> = self
            .core
            .applet
            .text(label)
            .class(theme::Text::Color(tone_text_color(tone)))
            .into();

        let content: Element<_> = if horizontal {
            row![dial, pct].spacing(pad_h).align_y(Alignment::Center).into()
        } else {
            column![dial, pct].spacing(pad_h).align_x(Alignment::Center).into()
        };
        let (h_pad, v_pad) = if horizontal { (pad_w, pad_h) } else { (pad_h, pad_w) };

        let btn = button::custom(content)
            .on_press_down(Message::TogglePopup)
            .class(theme::Button::AppletIcon)
            .padding([v_pad, h_pad]);

        self.core.applet.autosize_window(btn).into()
    }

    /// The popup: one block per provider, laid out like claude.ai's usage panel.
    fn view_window(&self, _id: Id) -> Element<'_, Self::Message> {
        let mut blocks: Vec<Element<_>> = Vec::new();

        if self.providers.is_empty() {
            let msg = match &self.error {
                Some(e) => format!("Can't reach codexbar serve\n{e}"),
                None => "Loading\u{2026}".to_string(),
            };
            blocks.push(text::body(msg).into());
            if self.error.is_some() {
                blocks.push(text::caption("Check: systemctl --user status codexbar-serve").into());
            }
        } else {
            for p in &self.providers {
                blocks.push(self.provider_block(p));
            }
            if let Some(e) = &self.error {
                blocks.push(text::caption(format!("Showing the last good reading \u{b7} {e}")).into());
            }
        }

        let content = column(blocks).spacing(18).padding([14, 18]);
        self.core.applet.popup_container(content).into()
    }

    fn style(&self) -> Option<cosmic::iced::theme::Style> {
        Some(cosmic::applet::style())
    }
}
